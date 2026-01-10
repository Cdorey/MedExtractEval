using System.Text.RegularExpressions;

namespace MedExtractEval.Data.Analytics
{
    public enum LabelSource { Llm, R1, R2, Gold }

    public static class LabelNormalizer
    {
        // Canonical label for "not mentioned / refused / null-ish"
        public const string NotMentioned = "notmentioned";

        public static string? Normalize(string taskType, string? raw, LabelSource source)
        {
            if (raw is null) return null;

            var s = raw.Trim();

            // Treat empty as NotMentioned (you can choose to keep empty as null instead)
            if (s.Length == 0) return NotMentioned;

            // Common null-ish strings (LLM often outputs these)
            var lower = s.ToLowerInvariant();
            if (lower is "null" or "none" or "n/a" or "na" or "undefined")
                return NotMentioned;

            // Common "not mentioned" variants from humans / gold
            if (Regex.IsMatch(lower, @"^(not\s*mentioned|notmentioned|not_mentioned|unmentioned|unspecified)$"))
                return NotMentioned;

            // Task-specific rules
            // If CTA is boolean-like, normalize booleans:
            if (IsBooleanTask(taskType))
            {
                // Accept multiple boolean spellings
                if (lower is "true" or "t" or "yes" or "y" or "1") return "true";
                if (lower is "false" or "f" or "no" or "n" or "0") return "false";

                // Some datasets store as "positive/negative"
                if (lower is "positive" or "pos") return "true";
                if (lower is "negative" or "neg") return "false";
            }

            // Default: case-insensitive canonicalization for nominal labels
            // Keep original meaning but normalize casing:
            // - for labels that are alphabetic: lower-case
            // - for mixed numeric categories: keep trimmed original
            // Here we do: lower-case everything to avoid case mismatch
            return lower;
        }

        private static bool IsBooleanTask(string taskType)
        {
            // You can tighten this to exactly your boolean tasks
            // e.g. CTA presence/absence
            return taskType.Equals("EXAMCORONARYCTA", StringComparison.OrdinalIgnoreCase)
                   || taskType.Contains("CTA", StringComparison.OrdinalIgnoreCase); // optional
        }

        /// <summary>
        /// Optional: compare after normalization.
        /// </summary>
        public static bool EqualsNormalized(string taskType, string? a, string? b, LabelSource sa, LabelSource sb)
        {
            var na = Normalize(taskType, a, sa);
            var nb = Normalize(taskType, b, sb);
            return string.Equals(na, nb, StringComparison.Ordinal);
        }
    }
}
