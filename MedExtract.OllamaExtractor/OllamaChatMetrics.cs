namespace MedExtract.OllamaExtractor
{
    public sealed record OllamaChatMetrics(
        TimeSpan? TimeToFirstToken,
        TimeSpan EndToEnd,
        double? PrefillTokensPerSecond,
        double? DecodeTokensPerSecond
    );
}
