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
        //private const string StatusExpired = "Expired";
        private const string StatusReady = "Ready";
        private const string StatusSkipped = "Skipped";
        public async Task<AssignNextCaseResponse?> AssignNextCaseAsync(string loginName, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            // 0) 确保 rater 存在（这里仍然是 Create + SaveChanges）
            var rater = await db.Raters.SingleOrDefaultAsync(x => x.LoginName == loginName, ct);
            if (rater is null)
            {
                rater = new Rater
                {
                    Id = Guid.NewGuid(),
                    LoginName = loginName,
                    Name = loginName,
                    IsAdmin = false
                };
                db.Raters.Add(rater);
                await db.SaveChangesAsync(ct);
            }

            // 1) 回收过期任务：Assigned & ExpiresAt<=now -> Ready，并清空租约字段
            //    （注意：如果你想保留审计痕迹，用 StatusExpired 另存也行；这里按你想法直接回到 Ready）
            await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned && a.ExpiresAt != null && a.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusReady)
                    .SetProperty(a => a.RaterId, (Guid?)null)
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)   // 如果你的 AssignedAt 不可空，就别清空
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null)
                    .SetProperty(a => a.CompletedAt, now)
                    .SetProperty(a => a.Attempt, a => a.Attempt + 1)
                , ct);

            // 2) 当前 rater 是否已有未完成任务（Assigned & 未过期）？
            //    如果有：续租并返回这条（保证“刷新页面不会换题”）
            var existingId = await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned
                         && a.RaterId == rater.Id
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .OrderByDescending(a => a.AssignedAt) // 取最新一条
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (existingId != Guid.Empty)
            {
                // 续租：原子 update（防止并发场景）
                var renewed = await db.CaseAssignments
                    .Where(a => a.Id == existingId
                             && a.Status == StatusAssigned
                             && a.RaterId == rater.Id
                             && a.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.LastSeenAt, now)
                        .SetProperty(a => a.ExpiresAt, now.AddMinutes(20)), ct);

                if (renewed == 1)
                {
                    // 回读需要返回的字段
                    var payload = await db.CaseAssignments
                        .Where(a => a.Id == existingId)
                        .Select(a => new
                        {
                            a.Id,
                            a.CaseId,
                            a.Round
                        })
                        .SingleAsync(ct);

                    var c = await db.Cases
                        .Where(x => x.Id == payload.CaseId)
                        .Select(x => new { x.Id, x.TaskType, x.RawText, x.MetaInfo })
                        .SingleAsync(ct);

                    return new AssignNextCaseResponse(
                        AssignmentId: payload.Id,
                        CaseId: c.Id,
                        TaskType: c.TaskType,
                        RawText: c.RawText,
                        MetaInfo: c.MetaInfo,
                        Round: payload.Round);
                }
                // 如果 renewed != 1，说明这条在你续租前刚好过期被回收了，继续往下分配新题
            }

            // 3) 尝试分配 Round=2（NeedR2）
            //    我这里不再从 Cases 里随机抽一条再插入 assignment，
            //    而是：从 CaseAssignments(Ready) 中挑一条 Round=2 来 claim（更符合你“预生成任务”的思路）
            //    如果你还没有“预生成 Ready 任务”，也可以在这里临时生成 Ready，再 claim；但你说要服务A预生成，就按这个写。
            var r2 = await TryClaimReadyAssignmentAsync(db,
                raterId: rater.Id,
                round: 2,
                now: now,
                // 只允许领取 NeedR2 且 R1 不是自己
                extraFilter: q => q.Where(a =>
                    db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1) &&
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 2) &&
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1 && x.RaterId == rater.Id)
                ),
                ct);

            if (r2 is not null) return r2;

            // 4) 再尝试分配 Round=1（尚未做过 R1）
            var r1 = await TryClaimReadyAssignmentAsync(db,
                raterId: rater.Id,
                round: 1,
                now: now,
                extraFilter: q => q.Where(a =>
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1)
                ),
                ct);

            if (r1 is not null) return r1;

            // 5) 都没有
            return null;
        }


        public async Task<SubmitAnnotationResponse> SubmitAsync(string loginName,
                                                                SubmitAnnotationRequest req,
                                                                CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var raterId = await db.Raters
                .Where(x => x.LoginName == loginName)
                .Select(x => x.Id)
                .SingleAsync(ct);

            // 1) 先检查“是否已提交”（幂等）：如果 assignment 已是 Submitted，直接返回
            //    这里用轻量查询避免把整行 tracking 进来
            var status = await db.CaseAssignments
                .Where(a => a.Id == req.AssignmentId && a.RaterId == raterId)
                .Select(a => a.Status)
                .SingleOrDefaultAsync(ct);

            if (status is null)
                return new SubmitAnnotationResponse(false, "Invalid assignment.", false);

            if (status == StatusSubmitted)
                return new SubmitAnnotationResponse(true, "Already submitted.", false);

            // 2) 过期回收：如果这条任务已过期且仍是 Assigned，则原子重置为 Ready（并清空租约信息）
            //    注意：这里同时校验属于当前用户 + caseId 匹配，避免用户用别人的 assignmentId 提交
            var expiredReset = await db.CaseAssignments
                .Where(a => a.Id == req.AssignmentId
                         && a.CaseId == req.CaseId
                         && a.RaterId == raterId
                         && a.Status == StatusAssigned
                         && a.ExpiresAt != null
                         && a.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusReady)
                    .SetProperty(a => a.RaterId, (Guid?)null)
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)   // 如果不可空就删掉这行
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null)
                    .SetProperty(a => a.CompletedAt, (DateTime?)null)
                    .SetProperty(a => a.AnnotationId, (Guid?)null)
                    .SetProperty(a => a.Attempt, a => a.Attempt + 1),
                    ct);

            if (expiredReset == 1)
                return new SubmitAnnotationResponse(false, "Assignment expired. Please reload and get a new case.", false);

            // 3) 仍有效：验证这条 assignment 现在必须是 Assigned + 属于当前用户 + 未过期
            //    这里用投影把后面要用的 Round 一起拿到
            var arow = await db.CaseAssignments
                .Where(a => a.Id == req.AssignmentId
                         && a.CaseId == req.CaseId
                         && a.RaterId == raterId
                         && a.Status == StatusAssigned
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .Select(a => new { a.Round })
                .SingleOrDefaultAsync(ct);

            if (arow is null)
                return new SubmitAnnotationResponse(false, "Invalid or no longer active assignment.", false);

            // 4) 校验 case/task type（只查必要字段）
            var c = await db.Cases
                .Where(x => x.Id == req.CaseId)
                .Select(x => new { x.Id, x.TaskType })
                .SingleOrDefaultAsync(ct);

            if (c is null)
                return new SubmitAnnotationResponse(false, "Case not found.", false);

            if (!string.Equals(c.TaskType, req.TaskType, StringComparison.OrdinalIgnoreCase))
                return new SubmitAnnotationResponse(false, "Task type mismatch.", false);

            // 5) Normalize
            var normalized = NormalizeAnnotatedValue(req.AnnotatedValue);

            // 6) 写 annotation
            var annotationId = Guid.NewGuid();
            var annotation = new Annotation
            {
                Id = annotationId,
                CaseId = c.Id,
                TaskType = c.TaskType,
                AnnotatedValue = normalized,
                Uncertainty = req.Note,
                RaterId = raterId,
                Round = arow.Round,
                StartedAt = req.StartedAtUtc,
                SubmittedAt = req.SubmittedAtUtc,
                CreatedAt = req.SubmittedAtUtc,
                DifficultyScore = req.DifficultyScore,
                ConfidenceScore = req.ConfidenceScore
            };

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            try
            {
                // 1) 先再做一次“仍有效”的校验（或者把校验和更新合并）
                // 2) 先写 annotation
                db.Annotations.Add(annotation);
                await db.SaveChangesAsync(ct); // 先拿到 annotationId 确保落库

                // 3) 再更新 assignment（仍需带上 Status/ExpiresAt 条件防并发）
                var updated = await db.CaseAssignments
                    .Where(a => a.Id == req.AssignmentId
                             && a.CaseId == req.CaseId
                             && a.RaterId == raterId
                             && a.Status == StatusAssigned
                             && a.ExpiresAt != null
                             && a.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.Status, StatusSubmitted)
                        .SetProperty(a => a.CompletedAt, now)
                        .SetProperty(a => a.AnnotationId, annotation.Id)
                        .SetProperty(a => a.LastSeenAt, now), ct);

                if (updated != 1)
                    throw new InvalidOperationException("Assignment no longer active.");

                await tx.CommitAsync(ct);
                return new SubmitAnnotationResponse(true, "Saved.", false);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<bool> ExtendLeaseAsync(string loginName, Guid assignmentId, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var raterId = await db.Raters
                .Where(x => x.LoginName == loginName)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(ct);

            if (raterId is null) return false;

            var now = DateTime.UtcNow;

            var updated = await db.CaseAssignments
                .Where(a => a.Id == assignmentId
                         && a.RaterId == raterId
                         && a.Status == StatusAssigned
                         && a.ExpiresAt != null
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

        private async Task<AssignNextCaseResponse?> TryClaimReadyAssignmentAsync(MedEvalDbContext db,
                                                                                 Guid raterId,
                                                                                 int round,
                                                                                 DateTime now,
                                                                                 Func<IQueryable<CaseAssignment>, IQueryable<CaseAssignment>> extraFilter,
                                                                                 CancellationToken ct)
        {
            // 候选：Ready + round + Case 未最终裁决 + 还没被别人 Assigned
            IQueryable<CaseAssignment> baseQuery = db.CaseAssignments
                .Where(a => a.Status == StatusReady && a.Round == round && a.RaterId == null)
                .Where(a => db.Cases.Any(c => c.Id == a.CaseId && c.FinalGoldLabel == null));

            baseQuery = extraFilter(baseQuery);

            // 选一个候选 ID（建议“稳定排序”而不是随机）
            var candidateId = await baseQuery
                .OrderBy(a => a.Attempt)          // 先发放尝试少的
                .ThenBy(a => a.Id)                // 稳定
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (candidateId == Guid.Empty) return null;

            // 原子 claim：只有当仍是 Ready 且 RaterId 为空时才成功
            var updated = await db.CaseAssignments
                .Where(a => a.Id == candidateId && a.Status == StatusReady && a.RaterId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusAssigned)
                    .SetProperty(a => a.RaterId, raterId)
                    .SetProperty(a => a.AssignedAt, now)
                    .SetProperty(a => a.LastSeenAt, now)
                    .SetProperty(a => a.ExpiresAt, now.AddMinutes(20))
                , ct);

            if (updated != 1)
                return null; // 被抢走了，上层可以再调用一次 TryClaim... 或直接回到主流程继续

            // 回读 payload
            var a2 = await db.CaseAssignments
                .Where(a => a.Id == candidateId)
                .Select(a => new { a.Id, a.CaseId, a.Round })
                .SingleAsync(ct);

            var c = await db.Cases
                .Where(x => x.Id == a2.CaseId)
                .Select(x => new { x.Id, x.TaskType, x.RawText, x.MetaInfo })
                .SingleAsync(ct);

            return new AssignNextCaseResponse(
                AssignmentId: a2.Id,
                CaseId: c.Id,
                TaskType: c.TaskType,
                RawText: c.RawText,
                MetaInfo: c.MetaInfo,
                Round: a2.Round);
        }
    }
}
