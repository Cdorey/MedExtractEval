namespace MedExtract.OllamaExtractor
{
    public sealed record OllamaUsage(
    long? PromptEvalCount,
    long? PromptEvalDurationNs,
    long? EvalCount,
    long? EvalDurationNs,
    long? LoadDurationNs,
    long? TotalDurationNs
);
}
