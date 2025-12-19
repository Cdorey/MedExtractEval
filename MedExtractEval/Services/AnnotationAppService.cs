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
        Task<bool> ExtendLeaseAsync(string loginName, Guid assignmentId, CancellationToken ct = default);

    }

    public sealed class AnnotationAppService(IDbContextFactory<MedEvalDbContext> dbFactory) : IAnnotationAppService
    {
        private const string StatusAssigned = "Assigned";
        private const string StatusSubmitted = "Submitted";
        private const string StatusExpired = "Expired";

        public async Task<AssignNextCaseResponse?> AssignNextCaseAsync(string loginName, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;
            await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned && a.ExpiresAt != null && a.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, StatusExpired).SetProperty(a => a.CompletedAt, now), ct);
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
            var needR2Case = db.Cases
                .Where(c => c.FinalGoldLabel == null) // 未最终裁决
                .Where(c => db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1) && !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 2) &&
                    // 排除当前rater是R1
                    !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1 && a.RaterId == rater.Id)
                )
                .Where(c => !db.CaseAssignments.Any(a => a.CaseId == c.Id && a.Round == 2 && a.Status == StatusAssigned && a.ExpiresAt > now));

            var count = await needR2Case.CountAsync(ct);
            if (count == 0) return null;
            if (count > 0)
            {
                var skip = Random.Shared.Next(count);
                var picked = await needR2Case.Skip(skip).FirstAsync(ct);
                if (picked is not null)
                {
                    var assignment = await CreateAssignmentAsync(db, picked.Id, rater.Id, round: 2, now, ct);
                    if (assignment is not null)
                        return new AssignNextCaseResponse(
                            assignment.Id,
                            picked.Id,
                            picked.TaskType,
                            picked.RawText,
                            picked.MetaInfo,
                            Round: 2);
                }
            }

            // 2) 否则分配尚未被任何人 Round=1 标注的 case（Round=1）
            var newCase = db.Cases
                .Where(c => c.FinalGoldLabel == null)
                .Where(c => !db.Annotations.Any(a => a.CaseId == c.Id && a.Round == 1))
                .Where(c => !db.CaseAssignments.Any(a => a.CaseId == c.Id && a.Round == 1 && a.Status == StatusAssigned && a.ExpiresAt > now));
            count = await newCase.CountAsync(ct);
            if (count == 0) return null;
            var newCaseSkip = Random.Shared.Next(count);
            var newCaseEntity = await newCase.Skip(newCaseSkip).FirstAsync(ct);

            if (newCaseEntity is null) return null;

            var a1 = await CreateAssignmentAsync(db, newCaseEntity.Id, rater.Id, round: 1, now, ct);
            if (a1 is null) return null;

            return new AssignNextCaseResponse(
                a1.Id,
                newCaseEntity.Id,
                newCaseEntity.TaskType,
                newCaseEntity.RawText,
                newCaseEntity.MetaInfo,
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

            if (assignment.Status == StatusSubmitted)
                return new SubmitAnnotationResponse(true, "Already submitted.", false); // 幂等

            var now = DateTime.UtcNow;
            if (assignment.Status != StatusAssigned)
                return new SubmitAnnotationResponse(false, $"Assignment is {assignment.Status}.", false);

            if (assignment.ExpiresAt <= now)
            {
                // 可选：顺手标记为 Expired，避免再次判断
                assignment.Status = StatusExpired;
                assignment.CompletedAt = now;
                await db.SaveChangesAsync(ct);
                return new SubmitAnnotationResponse(false, "Assignment expired. Please reload and get a new case.", false);
            }

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
            assignment.Status = StatusSubmitted;
            assignment.CompletedAt = DateTime.UtcNow;

            // 6) 是否需要触发第二人复核：这里按你原始规则“与模型不一致才触发”
            // 但你当前没提供 ModelPrediction 类，所以这里给你占位：
            bool triggeredR2 = false;

            // 如果你暂时没有模型结果，也可以先用“所有R1都做R2抽检”策略（比如10%）来做质控
            // triggeredR2 = assignment.Round == 1 && Random.Shared.NextDouble() < 0.1;

            await db.SaveChangesAsync(ct);

            return new SubmitAnnotationResponse(true, "Saved.", triggeredR2);
        }

        public async Task<bool> ExtendLeaseAsync(string loginName, Guid assignmentId, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rater = await db.Raters.SingleAsync(x => x.LoginName == loginName, ct);

            var now = DateTime.UtcNow;
            var updated = await db.CaseAssignments
                .Where(a => a.Id == assignmentId
                         && a.RaterId == rater.Id
                         && a.Status == StatusAssigned
                         && a.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.LastSeenAt, now)
                    .SetProperty(a => a.ExpiresAt, now.AddMinutes(20)), ct);

            return updated == 1;
        }

        private static string NormalizeAnnotatedValue(string value)
        {
            return value.Trim();
        }

        private static async Task<CaseAssignment?> CreateAssignmentAsync(
            MedEvalDbContext db, Guid caseId, Guid raterId, int round, DateTime now, CancellationToken ct)
        {
            var assignment = new CaseAssignment
            {
                Id = Guid.NewGuid(),
                CaseId = caseId,
                RaterId = raterId,
                Round = round,
                AssignedAt = now,
                ExpiresAt = now.AddMinutes(20),
                Status = StatusAssigned,
                LastSeenAt = now
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
