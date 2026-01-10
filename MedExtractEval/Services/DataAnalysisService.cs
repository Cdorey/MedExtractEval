using MedExtractEval.Data;
using MedExtractEval.Data.Analytics;
using MedExtractEval.DTOs;
using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace MedExtractEval.Services
{
    public interface IDataAnalysisService
    {
        /// <summary>
        /// 生成一份纯文本的数据分析报告（给 Razor 直接展示）
        /// </summary>
        Task<DataAnalysisReport> GenerateReportAsync(
            DataAnalysisRequest req,
            IProgress<string>? progress = null,
            CancellationToken ct = default);
    }
    public sealed class DataAnalysisService(IDbContextFactory<MedEvalDbContext> dbFactory) : IDataAnalysisService
    {
        public async Task<DataAnalysisReport> GenerateReportAsync(DataAnalysisRequest req,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // 1) for each case, find latest CreatedAt within the experiment
            var latestPerCase = db.ModelExtractions
                .AsNoTracking()
                .GroupBy(x => x.CaseId)
                .Select(g => new
                {
                    CaseId = g.Key,
                    MaxCreatedAt = g.Max(x => x.CreatedAt)
                });

            // join back to get the row (if ties exist, take one deterministically)
            var latestModelByCase = await (
                from m in db.ModelExtractions.AsNoTracking()
                join lp in latestPerCase
                    on new { m.CaseId, m.CreatedAt } equals new { lp.CaseId, CreatedAt = lp.MaxCreatedAt }
                select new
                {
                    m.CaseId,
                    LlmValue = m.ParsedValue,
                    m.ParsedSuccessfully,
                    m.ErrorCode,
                    m.CreatedAt
                }
            )
            .ToDictionaryAsync(x => x.CaseId, x => x, ct);

            // 2) Pull R1/R2 annotations per case (Round 1/2)
            var latestAnnKey = db.Annotations
                .AsNoTracking()
                .Where(a => a.Round == 1 || a.Round == 2)
                .GroupBy(a => new { a.CaseId, a.Round })
                .Select(g => new
                {
                    g.Key.CaseId,
                    g.Key.Round,
                    MaxSubmittedAt = g.Max(x => x.SubmittedAt)
                });

            // 2) join back to get the annotation row
            var annByCaseRound = await (
                from a in db.Annotations.AsNoTracking()
                join k in latestAnnKey
                    on new { a.CaseId, a.Round, a.SubmittedAt }
                    equals new { k.CaseId, k.Round, SubmittedAt = k.MaxSubmittedAt }
                where a.Round == 1 || a.Round == 2
                select new
                {
                    a.CaseId,
                    a.Round,
                    Value = a.AnnotatedValue,
                    a.RaterId,
                    a.SubmittedAt,
                    a.Id // tie-break helper if needed
                }
            ).ToListAsync(ct);

            var r1ByCase = annByCaseRound
                .Where(x => x.Round == 1)
                .ToDictionary(x => x.CaseId, x => x);

            var r2ByCase = annByCaseRound
                .Where(x => x.Round == 2)
                .ToDictionary(x => x.CaseId, x => x);

            // 3) Pull cases involved in the experiment (join via ModelExtractions)
            // and fetch task type + gold label
            var caseIds = latestModelByCase.Keys.ToArray();

            var cases = await db.Cases
                .AsNoTracking()
                .Where(c => caseIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    TaskType = c.TaskType.ToUpper(),
                    Gold = c.FinalGoldLabel,
                    c.FinalizedAt
                })
                .ToListAsync(ct);

            // 4) Build unified rows
            var rows = new List<Row>(cases.Count);
            foreach (var c in cases)
            {
                latestModelByCase.TryGetValue(c.Id, out var model);
                r1ByCase.TryGetValue(c.Id, out var r1);
                r2ByCase.TryGetValue(c.Id, out var r2);

                rows.Add(new Row(CaseId: c.Id,
                                 TaskType: c.TaskType,
                                 Llm: LabelNormalizer.Normalize(c.TaskType, model?.LlmValue, LabelSource.Llm),
                                 R1: LabelNormalizer.Normalize(c.TaskType, r1?.Value, LabelSource.R1),
                                 R2: LabelNormalizer.Normalize(c.TaskType, r2?.Value, LabelSource.R2),
                                 Gold: LabelNormalizer.Normalize(c.TaskType, c.Gold, LabelSource.Gold),
                                 HasR2: r2 is not null,
                                 ParsedOk: model?.ParsedSuccessfully ?? false,
                                 ModelError: model?.ErrorCode));
            }

            // 5) Group by task and compute metrics
            var sb = new StringBuilder();
            sb.AppendLine("LLM Extraction Evaluation Report");
            sb.AppendLine($"GeneratedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            var tasks = rows.GroupBy(r => r.TaskType).OrderBy(g => g.Key).ToList();
            sb.AppendLine($"Tasks: {tasks.Count}  |  Total cases analyzed: {rows.Count}");
            sb.AppendLine();

            foreach (var tg in tasks)
            {
                var task = tg.Key;
                var data = tg.ToList();

                sb.AppendLine(new string('=', 70));
                sb.AppendLine($"Task: {task}");
                sb.AppendLine($"N: {data.Count}");
                sb.AppendLine();

                // Flow counts
                var flow = ComputeFlow(data);
                sb.AppendLine("Flow (R1/R2/R3-related counts)");
                sb.AppendLine($"- R1 present: {flow.R1Present}/{data.Count}");
                sb.AppendLine($"- R2 present: {flow.R2Present}/{data.Count}");
                sb.AppendLine($"- Gold present (FinalGoldLabel): {flow.GoldPresent}/{data.Count}");
                sb.AppendLine($"- LLM present: {flow.LlmPresent}/{data.Count}");
                sb.AppendLine($"- R1 == LLM: {flow.R1EqLlm}");
                sb.AppendLine($"- R1 != LLM: {flow.R1NeqLlm}");
                sb.AppendLine($"- Entered R2 (proxy): {flow.EnteredR2}  (R2 present)");
                sb.AppendLine($"- R1 != R2: {flow.R1NeqR2}  (proxy for entering R3)");
                sb.AppendLine($"- Gold differs from R1: {flow.GoldNeqR1}");
                sb.AppendLine($"- Gold differs from LLM: {flow.GoldNeqLlm}");
                sb.AppendLine();

                // Agreement LLM vs R1 (where both present)
                var agreePairs = data.Where(x => x.R1 is not null && x.Llm is not null).ToList();
                if (agreePairs.Count > 0)
                {
                    var pctAgree = agreePairs.Count(x => x.R1 == x.Llm) / (double)agreePairs.Count;
                    sb.AppendLine("Agreement (LLM vs R1)");
                    sb.AppendLine($"- % agreement: {pctAgree:P2}  (n={agreePairs.Count})");

                    // Kappa works for nominal labels; skip if only 1 label appears
                    var kappa = TryCohensKappa(agreePairs.Select(x => (x.Llm!, x.R1!)));
                    if (kappa is not null)
                        sb.AppendLine($"- Cohen's kappa: {kappa.Value:F4}");
                    else
                        sb.AppendLine($"- Cohen's kappa: N/A (insufficient label diversity)");
                    sb.AppendLine();
                }

                // Accuracy vs Gold
                sb.AppendLine("Accuracy vs Gold (FinalGoldLabel)");
                var llmAcc = AccuracyWithWilson(data.Where(x => x.Gold is not null && x.Llm is not null)
                                                   .Select(x => (Pred: x.Llm!, Gold: x.Gold!)));
                var r1Acc = AccuracyWithWilson(data.Where(x => x.Gold is not null && x.R1 is not null)
                                                   .Select(x => (Pred: x.R1!, Gold: x.Gold!)));

                if (llmAcc is not null)
                    sb.AppendLine($"- LLM: accuracy={llmAcc.Value.Accuracy:P2}  95%CI[{llmAcc.Value.CI_L:P2}, {llmAcc.Value.CI_U:P2}]  (n={llmAcc.Value.N})");
                else
                    sb.AppendLine($"- LLM: N/A (no paired Gold & LLM)");

                if (r1Acc is not null)
                    sb.AppendLine($"- R1 : accuracy={r1Acc.Value.Accuracy:P2}  95%CI[{r1Acc.Value.CI_L:P2}, {r1Acc.Value.CI_U:P2}]  (n={r1Acc.Value.N})");
                else
                    sb.AppendLine($"- R1 : N/A (no paired Gold & R1)");

                sb.AppendLine();

                // Paired comparison LLM vs R1 relative to Gold (McNemar; only for binary labels)
                var pairedForMcnemar = data.Where(x => x.Gold is not null && x.Llm is not null && x.R1 is not null).ToList();
                if (pairedForMcnemar.Count >= 10)
                {
                    var distinctGold = pairedForMcnemar.Select(x => x.Gold!).Distinct().Take(3).ToList();
                    if (distinctGold.Count == 2) // binary
                    {
                        var mc = McNemar(pairedForMcnemar.Select(x =>
                        {
                            var llmCorrect = x.Llm == x.Gold;
                            var r1Correct = x.R1 == x.Gold;
                            return (llmCorrect, r1Correct);
                        }));
                        sb.AppendLine("Paired comparison (LLM vs R1) vs Gold");
                        sb.AppendLine($"- McNemar (chi-square, no continuity correction): X2={mc.ChiSquare:F4}, p={mc.PValue:G4}");
                        sb.AppendLine($"- Discordant pairs: (LLM wrong, R1 correct)={mc.B}, (LLM correct, R1 wrong)={mc.C}  (n={pairedForMcnemar.Count})");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine("Paired comparison (LLM vs R1) vs Gold");
                        sb.AppendLine($"- McNemar skipped (Gold is not binary; labels={distinctGold.Count}+). Consider Stuart–Maxwell for multi-class.");
                        sb.AppendLine();
                    }
                }
            }

            // Optional: overall micro summary (only for accuracy vs gold)
            sb.AppendLine(new string('=', 70));
            sb.AppendLine("Overall (all tasks combined) accuracy vs Gold");
            var overall = rows.Where(x => x.Gold is not null).ToList();
            var overallLlm = AccuracyWithWilson(overall.Where(x => x.Llm is not null).Select(x => (x.Llm!, x.Gold!)));
            var overallR1 = AccuracyWithWilson(overall.Where(x => x.R1 is not null).Select(x => (x.R1!, x.Gold!)));

            if (overallLlm is not null)
                sb.AppendLine($"- LLM: accuracy={overallLlm.Value.Accuracy:P2}  95%CI[{overallLlm.Value.CI_L:P2}, {overallLlm.Value.CI_U:P2}]  (n={overallLlm.Value.N})");
            if (overallR1 is not null)
                sb.AppendLine($"- R1 : accuracy={overallR1.Value.Accuracy:P2}  95%CI[{overallR1.Value.CI_L:P2}, {overallR1.Value.CI_U:P2}]  (n={overallR1.Value.N})");

            sb.AppendLine();
            sb.AppendLine("Notes:");
            sb.AppendLine("- Gold is read from CaseItem.FinalGoldLabel. Ensure it is populated after R3 adjudication.");
            sb.AppendLine("- For multiple annotations per (CaseId, Round), this report uses the latest SubmittedAt.");
            sb.AppendLine("- LLM value uses latest ModelExtraction per CaseId within the experiment (CreatedAt max).");

            return new DataAnalysisReport(DateTime.UtcNow.ToLocalTime(), sb.ToString());
        }

        // ------------------------------ helpers ------------------------------

        private static string? Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Trim();
        }

        private static FlowCounts ComputeFlow(List<Row> data)
        {
            var r1Present = data.Count(x => x.R1 is not null);
            var r2Present = data.Count(x => x.R2 is not null);
            var goldPresent = data.Count(x => x.Gold is not null);
            var llmPresent = data.Count(x => x.Llm is not null);

            var r1llmPairs = data.Where(x => x.R1 is not null && x.Llm is not null).ToList();
            var r1EqLlm = r1llmPairs.Count(x => x.R1 == x.Llm);
            var r1NeqLlm = r1llmPairs.Count - r1EqLlm;

            var r1r2Pairs = data.Where(x => x.R1 is not null && x.R2 is not null).ToList();
            var r1NeqR2 = r1r2Pairs.Count(x => x.R1 != x.R2);

            var goldR1Pairs = data.Where(x => x.Gold is not null && x.R1 is not null).ToList();
            var goldNeqR1 = goldR1Pairs.Count(x => x.Gold != x.R1);

            var goldLlmPairs = data.Where(x => x.Gold is not null && x.Llm is not null).ToList();
            var goldNeqLlm = goldLlmPairs.Count(x => x.Gold != x.Llm);

            return new FlowCounts(
                R1Present: r1Present,
                R2Present: r2Present,
                GoldPresent: goldPresent,
                LlmPresent: llmPresent,
                R1EqLlm: r1EqLlm,
                R1NeqLlm: r1NeqLlm,
                EnteredR2: r2Present,
                R1NeqR2: r1NeqR2,
                GoldNeqR1: goldNeqR1,
                GoldNeqLlm: goldNeqLlm
            );
        }

        private static double? TryCohensKappa(IEnumerable<(string A, string B)> pairs)
        {
            var list = pairs.ToList();
            if (list.Count == 0) return null;

            var labels = list.SelectMany(x => new[] { x.A, x.B }).Distinct().ToList();
            if (labels.Count < 2) return null;

            var n = list.Count;
            var agree = list.Count(x => x.A == x.B);
            var po = agree / (double)n;

            // expected agreement
            var pA = labels.ToDictionary(l => l, l => list.Count(x => x.A == l) / (double)n);
            var pB = labels.ToDictionary(l => l, l => list.Count(x => x.B == l) / (double)n);
            var pe = labels.Sum(l => pA[l] * pB[l]);

            if (Math.Abs(1 - pe) < 1e-12) return null;
            return (po - pe) / (1 - pe);
        }

        private static AccResult? AccuracyWithWilson(IEnumerable<(string Pred, string Gold)> pairs)
        {
            var list = pairs.ToList();
            if (list.Count == 0) return null;

            var n = list.Count;
            var k = list.Count(x => x.Pred == x.Gold);
            var acc = k / (double)n;

            var (l, u) = WilsonCI(k, n, 0.95);
            return new AccResult(n, k, acc, l, u);
        }

        // Wilson score interval for proportion
        private static (double L, double U) WilsonCI(int successes, int n, double level)
        {
            // For 95% CI, z=1.959963984540054; general could be computed via inverse normal.
            // Keep it simple & stable:
            var z = level switch
            {
                0.90 => 1.6448536269514722,
                0.95 => 1.959963984540054,
                0.99 => 2.5758293035489004,
                _ => 1.959963984540054
            };

            if (n == 0) return (double.NaN, double.NaN);
            var p = successes / (double)n;

            var z2 = z * z;
            var denom = 1 + z2 / n;
            var center = (p + z2 / (2 * n)) / denom;
            var half = (z * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n)) / denom;

            var l = Math.Max(0, center - half);
            var u = Math.Min(1, center + half);
            return (l, u);
        }

        // McNemar test without continuity correction:
        // b = (LLM wrong, R1 correct), c = (LLM correct, R1 wrong)
        private static McNemarResult McNemar(IEnumerable<(bool llmCorrect, bool r1Correct)> paired)
        {
            int b = 0, c = 0;
            foreach (var (llm, r1) in paired)
            {
                if (!llm && r1) b++;
                else if (llm && !r1) c++;
            }

            // If no discordant pairs -> p=1
            if (b + c == 0)
                return new McNemarResult(b, c, 0, 1);

            var chi2 = (b - c) * (b - c) / (double)(b + c);

            // p-value from Chi-square(df=1): p = 1 - CDF(chi2)
            // We implement using survival function approximation via regularized gamma for df=1
            // For df=1, CDF = erf(sqrt(x/2))
            var pValue = 1.0 - Erf(Math.Sqrt(chi2 / 2.0));

            return new McNemarResult(b, c, chi2, pValue);
        }

        // Error function approximation
        private static double Erf(double x)
        {
            // Abramowitz and Stegun approximation
            var sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            var t = 1.0 / (1.0 + p * x);
            var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        private readonly record struct Row(
            Guid CaseId,
            string TaskType,
            string? Llm,
            string? R1,
            string? R2,
            string? Gold,
            bool HasR2,
            bool ParsedOk,
            string? ModelError
        );

        private readonly record struct FlowCounts(
            int R1Present,
            int R2Present,
            int GoldPresent,
            int LlmPresent,
            int R1EqLlm,
            int R1NeqLlm,
            int EnteredR2,
            int R1NeqR2,
            int GoldNeqR1,
            int GoldNeqLlm
        );

        private readonly record struct AccResult(
            int N,
            int Correct,
            double Accuracy,
            double CI_L,
            double CI_U
        );

        private readonly record struct McNemarResult(
            int B,
            int C,
            double ChiSquare,
            double PValue
        );
    }
}
