namespace MedExtractEval.DTOs
{
    public sealed record SeedAssignmentsResult(
        int RequestedTotal,
        int CreatedTotal,
        IReadOnlyList<SeedPerTaskTypeResult> ByTaskType
    );
}
