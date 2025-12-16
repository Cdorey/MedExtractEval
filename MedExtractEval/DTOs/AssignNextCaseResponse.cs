namespace MedExtractEval.DTOs
{
    public sealed record AssignNextCaseResponse(
        Guid AssignmentId,
        Guid CaseId,
        string TaskType,
        string RawText,
        string? MetaInfo,
        int Round);
}
