namespace MedExtractEval.DTOs
{
    public sealed record DataAnalysisRequest(
        string? TaskType = null,
        int DaysBack = 30,
        int TakeTopErrors = 10
    );
}
