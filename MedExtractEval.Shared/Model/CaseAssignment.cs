namespace MedExtractEval.Shared.Model
{
    public class CaseAssignment
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public CaseItem? Case { get; set; }
        public Guid? RaterId { get; set; }
        public Rater? Rater { get; set; }
        public Guid? AnnotationId { get; set; }
        public Annotation? Annotation { get; set; }
        public int Round { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = "Assigned"; // Assigned / Submitted / Skipped / Expired
        public int Attempt { get; set; }
    }
}