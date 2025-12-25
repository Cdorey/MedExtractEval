namespace MedExtractEval.DTOs
{
    public sealed record SubmitAnnotationRequest(
        Guid AssignmentId,
        Guid CaseId,
        string TaskType,
        string AnnotatedValue,
        string? Note,
        DateTime StartedAtUtc,
        DateTime SubmittedAtUtc,
        int? DifficultyScore,
        int? ConfidenceScore);
}