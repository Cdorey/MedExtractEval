namespace MedExtractEval.DTOs
{
    public sealed record SeedPerTaskTypeResult(
        string TaskType,
        int Requested,
        int Created
    );
}
