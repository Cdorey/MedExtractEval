namespace MedExtractEval.DTOs
{
    public sealed record QcProgressResult(
        int ScannedCases,
        int AutoConfirmed,
        int SentToR2,
        int AuditedToR2,
        int FinalizedByR1R2Agree,
        int MarkedNeedsAdjudication,
        int SkippedBecauseMissingData
    );
}
