namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents a case item with associated metadata and final gold label information.
    /// </summary>
    public class CaseItem
    {
        /// <summary>
        /// Gets or sets the unique identifier for the case.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the raw text of the medical examination report.
        /// </summary>
        public string RawText { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the task type (e.g., "IMT", "LVEF", "STENOSIS").
        /// </summary>
        public string TaskType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets optional metadata such as disease type, date, or hospital.
        /// </summary>
        public string? MetaInfo { get; set; }

        /// <summary>
        /// Gets or sets the final gold standard label.
        /// </summary>
        public string? FinalGoldLabel { get; set; }

        /// <summary>
        /// Gets or sets the ID of the rater who finalized the case.
        /// </summary>
        public Guid? FinalGoldRaterId { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the case was finalized.
        /// </summary>
        public DateTime? FinalizedAt { get; set; }
    }

    public class CaseAssignment
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid RaterId { get; set; }
        public int Round { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Status { get; set; } = "Assigned"; // Assigned / Submitted / Skipped / Expired
    }
}