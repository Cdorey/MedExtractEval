namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents an experiment that includes multiple cases and configurations.
    /// </summary>
    public class Experiment
    {
        /// <summary>
        /// Gets or sets the unique identifier for the experiment.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the experiment.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the IDs of the cases included in the experiment.
        /// </summary>
        public Guid[] IncludedCaseIds { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the experiment was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the description of the experiment.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
