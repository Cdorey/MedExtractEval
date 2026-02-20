namespace MedExtract.OllamaExtractor
{
    public sealed record OllamaChatRunResult(
        string Model,
        string SystemMessage,
        string UserMessage,
        bool ThinkEnabled,
        bool Completed,
        string ThinkingText,
        string ContentText,
        OllamaUsage Usage,
        OllamaChatMetrics Metrics
    );
}
