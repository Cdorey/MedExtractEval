namespace MedExtractEval.DTOs
{
    // ========== Adjudication ==========
    public sealed record AdjudicationRequest(
        Guid CaseId,
        string TaskType,
        string FinalGoldLabel,
        string? Note = null
    );
}
