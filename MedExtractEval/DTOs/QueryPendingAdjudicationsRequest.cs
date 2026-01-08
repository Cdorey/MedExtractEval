namespace MedExtractEval.DTOs
{
    public sealed record QueryPendingAdjudicationsRequest(
    string? TaskType = null,
    int Take = 50,
    int Skip = 0);
}
