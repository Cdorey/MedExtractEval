namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents an annotation made by rater for a specific case.
    /// </summary>
    public class Annotation
    {
        /// <summary>
        /// Gets or sets the unique identifier for the annotation.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the associated case.
        /// </summary>
        public Guid CaseId { get; set; }

        /// <summary>
        /// Gets or sets the associated case item.
        /// </summary>
        public CaseItem? Case { get; set; }

        /// <summary>
        /// Gets or sets the task type.
        /// </summary>
        public string TaskType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the value annotated by the rater.
        /// </summary>
        public string AnnotatedValue { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the uncertainty description, if any.
        /// </summary>
        public string? Uncertainty { get; set; }

        /// <summary>
        /// Gets or sets the ID of the rater who made the annotation.
        /// </summary>
        public Guid RaterId { get; set; }

        /// <summary>
        /// Gets or sets the associated rater.
        /// </summary>
        public Rater? Rater { get; set; }

        /// <summary>
        /// Gets or sets the round of annotation.
        /// </summary>
        public int Round { get; set; } // 1=初评, 2=复核

        /// <summary>
        /// Gets or sets the date and time when the annotation was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the annotation was started.
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the annotation was submitted.
        /// </summary>
        public DateTime SubmittedAt { get; set; }

        /// <summary>
        /// Gets or sets the difficulty score of the case (1-5).
        /// </summary>
        public int? DifficultyScore { get; set; }

        /// <summary>
        /// Gets or sets the confidence score of the annotation (1-5).
        /// </summary>
        public int? ConfidenceScore { get; set; }
    }
}
