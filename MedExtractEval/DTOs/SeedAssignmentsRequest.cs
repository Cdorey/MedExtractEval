namespace MedExtractEval.DTOs
{
    public sealed record SeedAssignmentsRequest(
    int PerTaskTypeN,
    string[] TaskTypes,
    int Round = 1,
    bool ExcludeAlreadySeeded = true
);
}
