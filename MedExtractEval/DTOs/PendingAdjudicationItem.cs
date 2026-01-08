namespace MedExtractEval.DTOs
{
    public sealed record PendingAdjudicationItem(
    Guid CaseId,
    string TaskType,
    string RawText,
    string? MetaInfo,
    string? LlmValue,
    string R1Value,
    string R2Value,
    DateTime? R1SubmittedAtUtc,
    DateTime? R2SubmittedAtUtc);
}
