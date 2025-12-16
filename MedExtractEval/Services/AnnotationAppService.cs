using MedExtractEval.Data;
using MedExtractEval.DTOs;
using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;
using System;

namespace MedExtractEval.Services
{
    public interface IAnnotationAppService
    {
        Task<AssignNextCaseResponse?> AssignNextCaseAsync(string loginName, CancellationToken ct = default);
        Task<SubmitAnnotationResponse> SubmitAsync(string loginName, SubmitAnnotationRequest req, CancellationToken ct = default);
    }

    public sealed class AnnotationAppService(IDbContextFactory<MedEvalDbContext> dbFactory) : IAnnotationAppService
    {
        public async Task<AssignNextCaseResponse?> AssignNextCaseAsync(string loginName, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var rater = await db.Raters.SingleOrDefaultAsync(x => x.LoginName == loginName, ct);
            if (rater is null)
            {
                // 也可以自动创建 rater
                rater = new Rater { Id = Guid.NewGuid(), LoginName = loginName, Name = loginName, IsAdmin = false };
                db.Raters.Add(rater);
                await db.SaveChangesAsync(ct);
            }

            // 1) 优先分配 NeedR2 的 case（Round=2），且该 case 的 R1 不是当前 rater
            // 这里我们用规则：如果一个 Case 已经有人 Round=1 标注，但还没有 Round=2 标注 -> 需要R2
            var needR2Case = await db.Cases
                .Where(c => c.FinalGoldLabel == null) // 未最终裁决
                .Where(c =>
                    db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1) &&
                    !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 2) &&
                    // 排除当前rater是R1
                    !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1 && a.RaterId == rater.Id)
                )
                .OrderBy(_ => Guid.NewGuid()) // 简单随机；大数据量可改成更高效随机策略
                .FirstOrDefaultAsync(ct);

            if (needR2Case is not null)
            {
                var assignment = await CreateAssignmentAsync(db, needR2Case.Id, rater.Id, round: 2, ct);
                if (assignment is null) return null; // 可能被别人抢走了，直接返回 null 让 UI 重试/再点一次

                return new AssignNextCaseResponse(
                    assignment.Id,
                    needR2Case.Id,
                    needR2Case.TaskType,
                    needR2Case.RawText,
                    needR2Case.MetaInfo,
                    Round: 2);
            }

            // 2) 否则分配尚未被任何人 Round=1 标注的 case（Round=1）
            var newCase = await db.Cases
                .Where(c => c.FinalGoldLabel == null)
                .Where(c => !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1))
                .OrderBy(_ => Guid.NewGuid())
                .FirstOrDefaultAsync(ct);

            if (newCase is null) return null;

            var a1 = await CreateAssignmentAsync(db, newCase.Id, rater.Id, round: 1, ct);
            if (a1 is null) return null;

            return new AssignNextCaseResponse(
                a1.Id,
                newCase.Id,
                newCase.TaskType,
                newCase.RawText,
                newCase.MetaInfo,
                Round: 1);
        }

        public async Task<SubmitAnnotationResponse> SubmitAsync(string loginName, SubmitAnnotationRequest req, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var rater = await db.Raters.SingleAsync(x => x.LoginName == loginName, ct);

            // 1) 校验 assignment 属于当前用户且未提交
            var assignment = await db.CaseAssignments.SingleOrDefaultAsync(x => x.Id == req.AssignmentId, ct);
            if (assignment is null || assignment.RaterId != rater.Id || assignment.CaseId != req.CaseId)
                return new SubmitAnnotationResponse(false, "Invalid assignment.", false);

            if (assignment.Status == "Submitted")
                return new SubmitAnnotationResponse(true, "Already submitted.", false); // 幂等

            // 2) 校验 case/task type
            var c = await db.Cases.SingleAsync(x => x.Id == req.CaseId, ct);
            if (!string.Equals(c.TaskType, req.TaskType, StringComparison.OrdinalIgnoreCase))
                return new SubmitAnnotationResponse(false, "Task type mismatch.", false);

            // 3) Normalize（建议你以后把 Normalize/规则抽到 EvaluationRule）
            var normalized = NormalizeAnnotatedValue(req.AnnotatedValue);

            // 4) 写 annotation（利用唯一索引防双写）
            var annotation = new Annotation
            {
                Id = Guid.NewGuid(),
                CaseId = c.Id,
                TaskType = c.TaskType,
                AnnotatedValue = normalized,
                Uncertainty = req.Uncertainty,
                RaterId = rater.Id,
                Round = assignment.Round,
                StartedAt = req.StartedAtUtc,
                SubmittedAt = req.SubmittedAtUtc,
                CreatedAt = req.SubmittedAtUtc,
                DifficultyScore = req.DifficultyScore,
                ConfidenceScore = req.ConfidenceScore
            };

            db.Annotations.Add(annotation);

            // 5) 更新 assignment 状态
            assignment.Status = "Submitted";
            assignment.CompletedAt = DateTime.UtcNow;

            // 6) 是否需要触发第二人复核：这里按你原始规则“与模型不一致才触发”
            // 但你当前没提供 ModelPrediction 类，所以这里给你占位：
            bool triggeredR2 = false;

            // 如果你暂时没有模型结果，也可以先用“所有R1都做R2抽检”策略（比如10%）来做质控
            // triggeredR2 = assignment.Round == 1 && Random.Shared.NextDouble() < 0.1;

            await db.SaveChangesAsync(ct);

            return new SubmitAnnotationResponse(true, "Saved.", triggeredR2);
        }

        private static string NormalizeAnnotatedValue(string value)
        {
            value = value.Trim();

            //if (string.Equals(taskType, "STENOSIS", StringComparison.OrdinalIgnoreCase))
            //{
            //    // 接受 yes/no/true/false/有/无
            //    var v = value.ToLowerInvariant();
            //    if (v is "yes" or "y" or "true" or "有" or "存在") return "Yes";
            //    if (v is "no" or "n" or "false" or "无" or "不存在") return "No";
            //    return value; // 保留原样，后续可标记为 Uncertainty
            //}

            //if (string.Equals(taskType, "LVEF", StringComparison.OrdinalIgnoreCase))
            //{
            //    // 统一成 0-100 的整数
            //    // 允许输入 "0.65" -> 65
            //    if (double.TryParse(value.Replace("%", ""), out var d))
            //    {
            //        if (d <= 1.0 && d >= 0) d *= 100.0;
            //        return Math.Round(d).ToString("0");
            //    }
            //}

            //if (string.Equals(taskType, "IMT", StringComparison.OrdinalIgnoreCase))
            //{
            //    // 统一成两位小数
            //    if (double.TryParse(value, out var d))
            //        return d.ToString("0.00");
            //}

            return value;
        }

        private static async Task<CaseAssignment?> CreateAssignmentAsync(
            MedEvalDbContext db, Guid caseId, Guid raterId, int round, CancellationToken ct)
        {
            var assignment = new CaseAssignment
            {
                Id = Guid.NewGuid(),
                CaseId = caseId,
                RaterId = raterId,
                Round = round,
                AssignedAt = DateTime.UtcNow,
                Status = "Assigned"
            };

            db.CaseAssignments.Add(assignment);

            try
            {
                await db.SaveChangesAsync(ct);
                return assignment;
            }
            catch (DbUpdateException)
            {
                // 可能被别人抢先分配了（唯一索引冲突）
                return null;
            }
        }
    }
}
