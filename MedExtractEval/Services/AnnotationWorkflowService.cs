using MedExtractEval.Data;
using MedExtractEval.DTOs;
using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;

namespace MedExtractEval.Services
{
    public interface IAnnotationWorkflowService
    {
        // ========== Rater 侧（UI 调用） ==========
        Task<AcquireCaseResponse?> AcquireAsync(
            string loginName,
            CancellationToken ct = default);

        Task<HeartbeatResponse> HeartbeatAsync(
            string loginName,
            Guid assignmentId,
            CancellationToken ct = default);

        Task<SubmitAnnotationResponse> SubmitAsync(
            string loginName,
            SubmitAnnotationRequest req,
            CancellationToken ct = default);

        Task<bool> SkipAsync(
            string loginName,
            Guid assignmentId,
            string? reason = null,
            CancellationToken ct = default);

        // ========== Workflow 侧（后台/管理触发） ==========
        // 预生成任务池：每个 TaskType 抽 N 条，生成 Ready 的 assignments（通常是 R1）
        Task<SeedAssignmentsResult> SeedAsync(
            SeedAssignmentsRequest req,
            CancellationToken ct = default);

        // 扫描与回收：把过期 Assigned 重置回 Ready（你现在在 Acquire 里做，也可以后台兜底）
        Task<int> RecycleExpiredLeasesAsync(
            DateTime utcNow,
            CancellationToken ct = default);

        // 质控/裁决推进：对某个 case 或某个批次执行 “R1 vs LLM -> 决定 R2 / AutoConfirm”
        Task<QcProgressResult> RunQcAsync(
            QcProgressRequest req,
            CancellationToken ct = default);

        // 仲裁：R3 写入最终 FGL（可由管理员/仲裁员页面调用）
        Task<AdjudicationResult> AdjudicateAsync(
            string adjudicatorLoginName,
            AdjudicationRequest req,
            CancellationToken ct = default);
    }

    public sealed class AnnotationWorkflowService(IDbContextFactory<MedEvalDbContext> dbFactory) : IAnnotationWorkflowService
    {
        // --- Status constants (建议你也可以改成 enum + converter，但先保持字符串最省事)
        private const string StatusReady = "Ready";
        private const string StatusAssigned = "Assigned";
        private const string StatusSubmitted = "Submitted";
        private const string StatusSkipped = "Skipped";

        // --- Lease config
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(20);

        public async Task<AcquireCaseResponse?> AcquireAsync(string loginName, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            // 0) 确保 rater 存在
            var rater = await EnsureRaterAsync(db, loginName, ct);

            // 1) 回收过期任务：Assigned & ExpiresAt<=now -> Ready (清空租约)
            await RecycleExpiredAssignmentsAsync(db, now, ct);

            // 2) 如果该 rater 已有未完成任务：续租并返回（刷新不换题）
            var existing = await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned
                         && a.RaterId == rater.Id
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => new { a.Id, a.CaseId, a.Round })
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                // 续租（原子更新）
                var renewed = await db.CaseAssignments
                    .Where(a => a.Id == existing.Id
                             && a.Status == StatusAssigned
                             && a.RaterId == rater.Id
                             && a.ExpiresAt != null
                             && a.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.LastSeenAt, now)
                        .SetProperty(a => a.ExpiresAt, now.Add(LeaseDuration)), ct);

                if (renewed == 1)
                    return await BuildAcquireResponseAsync(db, existing.Id, now, ct);

                // renewed != 1: 说明刚好过期被回收，继续走分配新任务
            }

            // 3) 优先 claim Round=2（NeedR2 且 R1 不是自己）
            var r2 = await TryClaimReadyAssignmentAsync(
                db,
                raterId: rater.Id,
                round: 2,
                now: now,
                extraFilter: q => q.Where(a =>
                    db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1) &&
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 2) &&
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1 && x.RaterId == rater.Id)
                ),
                ct);

            if (r2 is not null) return r2;

            // 4) 再 claim Round=1（尚未做过 R1）
            var r1 = await TryClaimReadyAssignmentAsync(
                db,
                raterId: rater.Id,
                round: 1,
                now: now,
                extraFilter: q => q.Where(a =>
                    !db.Annotations.Any(x => x.CaseId == a.CaseId && x.Round == 1)
                ),
                ct);

            return r1; // 可能是 null（没任务）
        }

        public async Task<HeartbeatResponse> HeartbeatAsync(string loginName, Guid assignmentId, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var raterId = await db.Raters
                .Where(x => x.LoginName == loginName)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(ct);

            if (raterId is null)
                return new HeartbeatResponse(false, null, "Rater not found.");

            // 只允许：Assigned + 属于当前 rater + 未过期
            var updated = await db.CaseAssignments
                .Where(a => a.Id == assignmentId
                         && a.Status == StatusAssigned
                         && a.RaterId == raterId
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.LastSeenAt, now)
                    .SetProperty(a => a.ExpiresAt, now.Add(LeaseDuration)), ct);

            if (updated != 1)
                return new HeartbeatResponse(false, null, "Assignment not active (expired / not owned / not assigned).");

            return new HeartbeatResponse(true, now.Add(LeaseDuration), null);
        }

        public async Task<SubmitAnnotationResponse> SubmitAsync(string loginName, SubmitAnnotationRequest req, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var raterId = await db.Raters
                .Where(x => x.LoginName == loginName)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(ct);

            if (raterId == Guid.Empty)
                return new SubmitAnnotationResponse(false, "Rater not found.", false);

            // 幂等：已提交直接返回
            var status = await db.CaseAssignments
                .Where(a => a.Id == req.AssignmentId)
                .Select(a => a.Status)
                .SingleOrDefaultAsync(ct);

            if (status is null)
                return new SubmitAnnotationResponse(false, "Invalid assignment.", false);

            if (status == StatusSubmitted)
                return new SubmitAnnotationResponse(true, "Already submitted.", false);

            // 1) 如果已过期：原子回收为 Ready，提示用户刷新
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
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null)
                    .SetProperty(a => a.CompletedAt, now)
                    .SetProperty(a => a.AnnotationId, (Guid?)null)
                    .SetProperty(a => a.Attempt, a => a.Attempt + 1), ct);

            if (expiredReset == 1)
                return new SubmitAnnotationResponse(false, "Assignment expired. Please reload and get a new case.", false);

            // 2) 仍有效：必须属于当前用户 + Assigned + 未过期；拿到 round
            var round = await db.CaseAssignments
                .Where(a => a.Id == req.AssignmentId
                         && a.CaseId == req.CaseId
                         && a.RaterId == raterId
                         && a.Status == StatusAssigned
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .Select(a => (int?)a.Round)
                .SingleOrDefaultAsync(ct);

            if (round is null)
                return new SubmitAnnotationResponse(false, "Invalid or no longer active assignment.", false);

            // 3) 校验 Case/TaskType
            var caseInfo = await db.Cases
                .Where(c => c.Id == req.CaseId)
                .Select(c => new { c.Id, c.TaskType })
                .SingleOrDefaultAsync(ct);

            if (caseInfo is null)
                return new SubmitAnnotationResponse(false, "Case not found.", false);

            if (!string.Equals(caseInfo.TaskType, req.TaskType, StringComparison.OrdinalIgnoreCase))
                return new SubmitAnnotationResponse(false, "Task type mismatch.", false);

            // 4) 写 Annotation（先准备实体）
            var normalized = NormalizeAnnotatedValue(req.AnnotatedValue);
            var annotationId = Guid.NewGuid();

            var annotation = new Annotation
            {
                Id = annotationId,
                CaseId = caseInfo.Id,
                TaskType = caseInfo.TaskType,
                AnnotatedValue = normalized,
                Uncertainty = req.Note,
                RaterId = raterId,
                Round = round.Value,
                StartedAt = req.StartedAtUtc,
                SubmittedAt = req.SubmittedAtUtc,
                CreatedAt = req.SubmittedAtUtc,
                DifficultyScore = req.DifficultyScore,
                ConfidenceScore = req.ConfidenceScore
            };

            var strategy = db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                // A) 先落库 Annotation，确保 FK 指向存在
                db.Annotations.Add(annotation);
                await db.SaveChangesAsync(ct);

                // B) 再“原子提交 assignment”（仍需满足 active 条件）
                var submitUpdated = await db.CaseAssignments
                    .Where(a => a.Id == req.AssignmentId
                             && a.CaseId == req.CaseId
                             && a.RaterId == raterId
                             && a.Status == StatusAssigned
                             && a.ExpiresAt != null
                             && a.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.Status, StatusSubmitted)
                        .SetProperty(a => a.CompletedAt, now)
                        .SetProperty(a => a.AnnotationId, annotationId)
                        .SetProperty(a => a.LastSeenAt, now), ct);

                if (submitUpdated != 1)
                {
                    // assignment 已不再 active，回滚，Annotation 也不会残留
                    await tx.RollbackAsync(ct);
                    return new SubmitAnnotationResponse(false, "Assignment is no longer active. Please reload.", false);
                }

                await tx.CommitAsync(ct);
                return new SubmitAnnotationResponse(true, "Saved.", false);
            });
        }

        public async Task<bool> SkipAsync(
            string loginName,
            Guid assignmentId,
            string? reason = null,
            CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var raterId = await db.Raters
                .Where(r => r.LoginName == loginName)
                .Select(r => (Guid?)r.Id)
                .SingleOrDefaultAsync(ct);

            if (raterId is null) return false;

            // 只允许跳过：自己持有的 Assigned 且未过期
            var updated = await db.CaseAssignments
                .Where(a => a.Id == assignmentId
                         && a.Status == StatusAssigned
                         && a.RaterId == raterId
                         && a.ExpiresAt != null
                         && a.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusSkipped)
                    .SetProperty(a => a.CompletedAt, now)
                    .SetProperty(a => a.LastSeenAt, now)
                // 如果你有 SkipReason 字段，取消注释：
                // .SetProperty(a => a.SkipReason, reason)
                , ct);

            return updated == 1;
        }

        /// <summary>
        /// 预生成 Ready 任务池
        /// </summary>
        /// <param name="req"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<SeedAssignmentsResult> SeedAsync(SeedAssignmentsRequest req, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var per = Math.Max(0, req.PerTaskTypeN);
            var round = req.Round <= 0 ? 1 : req.Round;

            var results = new List<SeedPerTaskTypeResult>();
            var createdTotal = 0;
            var requestedTotal = per * (req.TaskTypes?.Length ?? 0);

            if (per == 0 || req.TaskTypes is null || req.TaskTypes.Length == 0)
                return new SeedAssignmentsResult(requestedTotal, 0, results);

            // 为了避免一次性拉太多数据：按 TaskType 分批
            foreach (var taskType in req.TaskTypes)
            {
                // 候选 case：未最终裁决
                var candidates = db.Cases
                    .Where(c => c.TaskType == taskType && c.FinalGoldLabel == null);

                if (req.ExcludeAlreadySeeded)
                {
                    // 排除已经存在该 Round 的 assignment（无论 Ready/Assigned/Submitted/Skipped）
                    candidates = candidates.Where(c =>
                        !db.CaseAssignments.Any(a => a.CaseId == c.Id && a.Round == round));
                }

                // 取 N 条（稳定排序，避免 OrderBy(Guid.NewGuid())）
                var pickedIds = await candidates
                    .OrderBy(c => c.Id)
                    .Select(c => c.Id)
                    .Take(per)
                    .ToListAsync(ct);

                var toCreate = pickedIds.Select(caseId => new CaseAssignment
                {
                    Id = Guid.NewGuid(),
                    CaseId = caseId,
                    Round = round,
                    Status = StatusReady,
                    RaterId = null,
                    AssignedAt = null,
                    ExpiresAt = null,
                    LastSeenAt = null,
                    CompletedAt = null,
                    Attempt = 0,
                    AnnotationId = null
                }).ToList();

                db.CaseAssignments.AddRange(toCreate);

                var created = 0;
                try
                {
                    created = await db.SaveChangesAsync(ct);
                    // SaveChanges 返回的是“受影响行”，对 AddRange 来说通常等于创建的实体数量（但也可能包含其他变化）
                    // 所以这里用 toCreate.Count 作为更直观的 Created
                    created = toCreate.Count;
                }
                catch (DbUpdateException)
                {
                    // 并发或唯一索引冲突：说明部分已存在
                    // 处理方式：回滚后逐条补（简单起见：重新查一下到底哪些缺）
                    db.ChangeTracker.Clear();

                    var existingCaseIds = await db.CaseAssignments
                        .Where(a => a.Round == round && pickedIds.Contains(a.CaseId))
                        .Select(a => a.CaseId)
                        .ToListAsync(ct);

                    var missing = pickedIds.Except(existingCaseIds).ToList();

                    var retry = missing.Select(caseId => new CaseAssignment
                    {
                        Id = Guid.NewGuid(),
                        CaseId = caseId,
                        Round = round,
                        Status = StatusReady,
                        RaterId = null,
                        Attempt = 0
                    }).ToList();

                    db.CaseAssignments.AddRange(retry);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        created = retry.Count;
                    }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        created = 0;
                    }
                }

                createdTotal += created;
                results.Add(new SeedPerTaskTypeResult(taskType, per, created));
            }

            return new SeedAssignmentsResult(requestedTotal, createdTotal, results);
        }

        /// <summary>
        /// RecycleExpiredLeases: 后台兜底回收
        /// </summary>
        /// <param name="utcNow"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<int> RecycleExpiredLeasesAsync(DateTime utcNow, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Assigned + 过期 => Ready（清空租约信息，Attempt++）
            var updated = await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned
                         && a.ExpiresAt != null
                         && a.ExpiresAt <= utcNow)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusReady)
                    .SetProperty(a => a.RaterId, (Guid?)null)
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null)
                    .SetProperty(a => a.CompletedAt, utcNow)
                    .SetProperty(a => a.AnnotationId, (Guid?)null)
                    .SetProperty(a => a.Attempt, a => a.Attempt + 1), ct);

            return updated;
        }

        /// <summary>
        /// RunQcAsync: R1 vs LLM -> R2 / AutoConfirm / Finalize / NeedsAdjudication
        /// </summary>
        /// <param name="req"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<QcProgressResult> RunQcAsync(QcProgressRequest req, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            var auditRate = Math.Clamp(req.AuditRate, 0.0, 1.0);

            // 仅处理：未 FinalGoldLabel 的 case
            var caseQuery = db.Cases.Where(c => c.FinalGoldLabel == null);

            if (req.TaskTypes is { Length: > 0 })
                caseQuery = caseQuery.Where(c => req.TaskTypes.Contains(c.TaskType));

            if (req.MaxCases is int max && max > 0)
                caseQuery = caseQuery.Take(max);

            // 拉取要 qc 的 caseId/taskType（不要一次性 join 大对象）
            var cases = await caseQuery
                .Select(c => new { c.Id, c.TaskType })
                .ToListAsync(ct);

            int scanned = 0, autoConfirmed = 0, sentToR2 = 0, auditedToR2 = 0, finalizedAgree = 0, needsAdj = 0, skippedMissing = 0;

            foreach (var c in cases)
            {
                scanned++;

                // 1) 取 LLM 抽取结果（ModelExtraction）
                var llmValue = await db.ModelExtractions
                    .Where(m => m.CaseId == c.Id)
                    .OrderByDescending(m => m.CreatedAt) // 如果你没有 CreatedAt，就删掉 OrderBy
                    .Select(m => m.ParsedValue)
                    .FirstOrDefaultAsync(ct);

                // 2) 取 R1 annotation
                var r1 = await db.Annotations
                    .Where(a => a.CaseId == c.Id && a.Round == 1)
                    .OrderByDescending(a => a.SubmittedAt)
                    .Select(a => a.AnnotatedValue)
                    .FirstOrDefaultAsync(ct);

                if (r1 is null || llmValue is null)
                {
                    skippedMissing++;
                    continue;
                }

                var r1n = NormalizeForCompare(r1);
                var llmn = NormalizeForCompare(llmValue);

                // 极少数：LLM 是“对不起…”这类 -> 当作缺失
                if (LooksLikeRefusal(llmn))
                {
                    skippedMissing++;
                    continue;
                }

                var r1EqualsLlm = string.Equals(r1n, llmn, StringComparison.Ordinal);

                // helper：是否已存在 R2 assignment / R2 annotation
                bool hasR2Anno = await db.Annotations.AnyAsync(a => a.CaseId == c.Id && a.Round == 2, ct);
                bool hasR2ReadyOrAssigned = await db.CaseAssignments.AnyAsync(a =>
                    a.CaseId == c.Id && a.Round == 2 && (a.Status == StatusReady || a.Status == StatusAssigned), ct);

                // 3) 若不一致：必须进 R2（创建 Ready 的 R2 assignment）
                if (!r1EqualsLlm)
                {
                    if (!hasR2Anno && !hasR2ReadyOrAssigned)
                    {
                        db.CaseAssignments.Add(new CaseAssignment
                        {
                            Id = Guid.NewGuid(),
                            CaseId = c.Id,
                            Round = 2,
                            Status = StatusReady,
                            RaterId = null,
                            Attempt = 0
                        });
                        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
                    }

                    sentToR2++;
                    continue;
                }

                // 4) 一致：10% 抽检进 R2（审计），否则 AutoConfirm 直接产 FGL
                bool audited = req.CreateAuditR2ForMatches && Random.Shared.NextDouble() < auditRate;
                if (audited)
                {
                    if (!hasR2Anno && !hasR2ReadyOrAssigned)
                    {
                        db.CaseAssignments.Add(new CaseAssignment
                        {
                            Id = Guid.NewGuid(),
                            CaseId = c.Id,
                            Round = 2,
                            Status = StatusReady,
                            RaterId = null,
                            Attempt = 0
                        });
                        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
                    }

                    auditedToR2++;
                    continue;
                }

                // AutoConfirm：直接写 FinalGoldLabel = R1
                var updated = await db.Cases
                    .Where(x => x.Id == c.Id && x.FinalGoldLabel == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.FinalGoldLabel, r1n)
                    // 如果你有这些字段，建议一起写上，方便追溯：
                    // .SetProperty(x => x.FinalizedAt, now)
                    // .SetProperty(x => x.FinalizedBy, "AutoConfirm")
                    , ct);

                if (updated == 1) autoConfirmed++;
            }

            // 5) 处理已完成 R2 的 case：R1==R2 -> finalize；R1!=R2 -> needs adjudication
            // 仅扫描未 FinalGoldLabel 的 case
            var toResolve = await db.Cases
                .Where(c => c.FinalGoldLabel == null)
                .Select(c => new { c.Id, c.TaskType })
                .ToListAsync(ct);

            foreach (var c in toResolve)
            {
                var r1 = await db.Annotations.Where(a => a.CaseId == c.Id && a.Round == 1)
                    .OrderByDescending(a => a.SubmittedAt).Select(a => a.AnnotatedValue).FirstOrDefaultAsync(ct);
                var r2 = await db.Annotations.Where(a => a.CaseId == c.Id && a.Round == 2)
                    .OrderByDescending(a => a.SubmittedAt).Select(a => a.AnnotatedValue).FirstOrDefaultAsync(ct);

                if (r1 is null || r2 is null) continue;

                var r1n = NormalizeForCompare(r1);
                var r2n = NormalizeForCompare(r2);

                if (string.Equals(r1n, r2n, StringComparison.Ordinal))
                {
                    var updated = await db.Cases
                        .Where(x => x.Id == c.Id && x.FinalGoldLabel == null)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.FinalGoldLabel, r1n)
                        // .SetProperty(x => x.FinalizedAt, now)
                        // .SetProperty(x => x.FinalizedBy, "R1R2Agree")
                        , ct);

                    if (updated == 1) finalizedAgree++;
                }
                else
                {
                    // 标记需要仲裁：你如果没字段，就先不写；可以单独做一张表记录待仲裁
                    // await db.Cases.Where(x => x.Id==c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NeedsAdjudication, true), ct);
                    needsAdj++;
                }
            }

            return new QcProgressResult(
                ScannedCases: scanned,
                AutoConfirmed: autoConfirmed,
                SentToR2: sentToR2,
                AuditedToR2: auditedToR2,
                FinalizedByR1R2Agree: finalizedAgree,
                MarkedNeedsAdjudication: needsAdj,
                SkippedBecauseMissingData: skippedMissing
            );
        }

        /// <summary>
        /// AdjudicateAsync: 仲裁（R3）
        /// </summary>
        /// <param name="adjudicatorLoginName"></param>
        /// <param name="req"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<AdjudicationResult> AdjudicateAsync(
            string adjudicatorLoginName,
            AdjudicationRequest req,
            CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            // 你可以在这里做权限校验（比如 IsAdmin）
            var adjudicator = await db.Raters.SingleOrDefaultAsync(r => r.LoginName == adjudicatorLoginName, ct);
            if (adjudicator is null)
                return new AdjudicationResult(false, "Adjudicator not found.");

            var normalized = NormalizeForCompare(req.FinalGoldLabel);

            // 写入 FinalGoldLabel（并可记录 FinalizedBy）
            var updated = await db.Cases
                .Where(c => c.Id == req.CaseId && c.TaskType == req.TaskType)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.FinalGoldLabel, normalized)
                // .SetProperty(c => c.FinalizedAt, now)
                // .SetProperty(c => c.FinalizedBy, $"Adjudicator:{adjudicatorLoginName}")
                , ct);

            if (updated != 1)
                return new AdjudicationResult(false, "Case not found or task type mismatch.");

            // 可选：关闭仍处于 Ready/Assigned 的 R2/R3 任务，避免继续发放
            await db.CaseAssignments
                .Where(a => a.CaseId == req.CaseId
                         && (a.Status == StatusReady || a.Status == StatusAssigned))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusSkipped)
                    .SetProperty(a => a.CompletedAt, now)
                    .SetProperty(a => a.RaterId, (Guid?)null)
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null), ct);

            return new AdjudicationResult(true, "Adjudication saved.");
        }

        private static string NormalizeForCompare(string? value)
        {
            var v = (value ?? string.Empty).Trim();

            // 统一换行/多空格（可选）
            v = v.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // true/false 规范化：把各种形式统一为 "true"/"false"
            var lower = v.ToLowerInvariant();
            if (lower is "true" or "t" or "yes" or "y" or "是") return "true";
            if (lower is "false" or "f" or "no" or "n" or "否") return "false";

            // 数值：你也可以在这里加 LVEF/IMT 的格式统一（比如 IMT 保留两位小数）
            // 但注意：一旦你格式化，就要确保与 LLM / 人工都一致，否则会“假不一致”。

            return lower; // 默认：trim + lower
        }

        private static bool LooksLikeRefusal(string normalizedLower)
        {
            // 你提到偶尔会有安全审查/拒答文本，按需补
            if (string.IsNullOrWhiteSpace(normalizedLower)) return true;
            if (normalizedLower.Contains("对不起") && normalizedLower.Contains("不能")) return true;
            if (normalizedLower.Contains("i can't") || normalizedLower.Contains("cannot comply")) return true;
            return false;
        }


        private static string NormalizeAnnotatedValue(string? value)
            => (value ?? string.Empty).Trim();

        private static async Task<Rater> EnsureRaterAsync(MedEvalDbContext db, string loginName, CancellationToken ct)
        {
            var rater = await db.Raters.SingleOrDefaultAsync(x => x.LoginName == loginName, ct);
            if (rater is not null) return rater;

            rater = new Rater
            {
                Id = Guid.NewGuid(),
                LoginName = loginName,
                Name = loginName,
                IsAdmin = false
            };
            db.Raters.Add(rater);
            await db.SaveChangesAsync(ct);
            return rater;
        }

        private static async Task RecycleExpiredAssignmentsAsync(MedEvalDbContext db, DateTime now, CancellationToken ct)
        {
            await db.CaseAssignments
                .Where(a => a.Status == StatusAssigned
                         && a.ExpiresAt != null
                         && a.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusReady)
                    .SetProperty(a => a.RaterId, (Guid?)null)
                    .SetProperty(a => a.AssignedAt, (DateTime?)null)
                    .SetProperty(a => a.ExpiresAt, (DateTime?)null)
                    .SetProperty(a => a.LastSeenAt, (DateTime?)null)
                    .SetProperty(a => a.CompletedAt, now)
                    .SetProperty(a => a.AnnotationId, (Guid?)null)
                    .SetProperty(a => a.Attempt, a => a.Attempt + 1), ct);
        }

        private static async Task<AcquireCaseResponse> BuildAcquireResponseAsync(MedEvalDbContext db, Guid assignmentId, DateTime now, CancellationToken ct)
        {
            var a = await db.CaseAssignments
                .Where(x => x.Id == assignmentId)
                .Select(x => new
                {
                    x.Id,
                    x.CaseId,
                    x.Round,
                    x.AssignedAt,
                    x.ExpiresAt
                })
                .SingleAsync(ct);

            var c = await db.Cases
                .Where(x => x.Id == a.CaseId)
                .Select(x => new { x.Id, x.TaskType, x.RawText, x.MetaInfo })
                .SingleAsync(ct);

            // AssignedAt/ExpiresAt 可能为 null（如果你把它们设为 nullable），这里兜底一下
            var assignedAt = a.AssignedAt ?? now;
            var expiresAt = a.ExpiresAt ?? now.Add(LeaseDuration);

            return new AcquireCaseResponse(
                AssignmentId: a.Id,
                CaseId: c.Id,
                TaskType: c.TaskType,
                RawText: c.RawText,
                MetaInfo: c.MetaInfo,
                Round: a.Round,
                AssignedAtUtc: assignedAt,
                ExpiresAtUtc: expiresAt
            );
        }

        private static async Task<AcquireCaseResponse?> TryClaimReadyAssignmentAsync(
            MedEvalDbContext db,
            Guid raterId,
            int round,
            DateTime now,
            Func<IQueryable<CaseAssignment>, IQueryable<CaseAssignment>> extraFilter,
            CancellationToken ct)
        {
            IQueryable<CaseAssignment> baseQuery = db.CaseAssignments
                .Where(a => a.Status == StatusReady
                         && a.Round == round
                         && a.RaterId == null)
                .Where(a => db.Cases.Any(c => c.Id == a.CaseId && c.FinalGoldLabel == null));

            baseQuery = extraFilter(baseQuery);

            // 选一个候选（稳定排序）
            var candidateId = await baseQuery
                .OrderBy(a => a.Attempt)
                .ThenBy(a => a.Id)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (candidateId == Guid.Empty) return null;

            // 原子 claim
            var updated = await db.CaseAssignments
                .Where(a => a.Id == candidateId
                         && a.Status == StatusReady
                         && a.RaterId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusAssigned)
                    .SetProperty(a => a.RaterId, (Guid?)raterId)
                    .SetProperty(a => a.AssignedAt, now)
                    .SetProperty(a => a.LastSeenAt, now)
                    .SetProperty(a => a.ExpiresAt, now.Add(LeaseDuration)), ct);

            if (updated != 1) return null; // 被抢走

            return await BuildAcquireResponseAsync(db, candidateId, now, ct);
        }
    }
}
