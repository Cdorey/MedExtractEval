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

    public sealed record SeedAssignmentsRequest(
    int PerTaskTypeN,
    string[] TaskTypes,
    int Round = 1,
    bool ExcludeAlreadySeeded = true
);

    public sealed record SeedAssignmentsResult(
        int RequestedTotal,
        int CreatedTotal,
        IReadOnlyList<SeedPerTaskTypeResult> ByTaskType
    );

    public sealed record SeedPerTaskTypeResult(
        string TaskType,
        int Requested,
        int Created
    );

    // ========== QC ==========
    public sealed record QcProgressRequest(
        string[]? TaskTypes = null,
        int? MaxCases = null,
        bool CreateAuditR2ForMatches = true,
        double AuditRate = 0.10
    );

    public sealed record QcProgressResult(
        int ScannedCases,
        int AutoConfirmed,
        int SentToR2,
        int AuditedToR2,
        int FinalizedByR1R2Agree,
        int MarkedNeedsAdjudication,
        int SkippedBecauseMissingData
    );

    // ========== Adjudication ==========
    public sealed record AdjudicationRequest(
        Guid CaseId,
        string TaskType,
        string FinalGoldLabel,
        string? Note = null
    );

    public sealed record AdjudicationResult(
        bool Ok,
        string Message
    );
}
