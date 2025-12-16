namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents a rater who performs annotations.
    /// </summary>
    public class Rater
    {
        /// <summary>
        /// Gets or sets the unique identifier for the rater.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the rater.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the login name of the rater (e.g., AD account or internal account).
        /// </summary>
        public string LoginName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the rater has administrative privileges.
        /// </summary>
        public bool IsAdmin { get; set; }
    }
}
