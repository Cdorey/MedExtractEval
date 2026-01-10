namespace MedExtractEval.DTOs
{
    public sealed record DataAnalysisReport(
        DateTime GeneratedAtUtc,
        string ReportText
    );
}
