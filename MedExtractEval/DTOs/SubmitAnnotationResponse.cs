namespace MedExtractEval.DTOs
{
    public sealed record SubmitAnnotationResponse(
        bool Saved,
        string Message,
        bool TriggeredSecondRater);
}
