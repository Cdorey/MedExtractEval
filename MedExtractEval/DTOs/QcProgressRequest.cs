namespace MedExtractEval.DTOs
{
    // ========== QC ==========
    public sealed record QcProgressRequest(
        string[]? TaskTypes = null,
        int? MaxCases = null,
        bool CreateAuditR2ForMatches = true,
        double AuditRate = 0.10
    );
}
