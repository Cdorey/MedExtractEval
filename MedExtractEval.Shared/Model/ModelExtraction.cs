namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents a model prediction for a specific case in an experiment.
    /// </summary>
    public class ModelExtraction
    {
        /// <summary>
        /// Gets or sets the unique identifier for the prediction.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the ID of the associated experiment.
        /// </summary>
        public Guid ExperimentId { get; set; }

        /// <summary>
        /// Gets or sets the associated experiment.
        /// </summary>
        public Experiment? Experiment { get; set; }

        /// <summary>
        /// Gets or sets the ID of the associated case.
        /// </summary>
        public Guid CaseId { get; set; }

        /// <summary>
        /// Gets or sets the associated case item.
        /// </summary>
        public CaseItem? Case { get; set; }

        /// <summary>
        /// Gets or sets the ID of the model configuration used for the prediction.
        /// </summary>
        public Guid ModelConfigId { get; set; }

        /// <summary>
        /// Gets or sets the associated model configuration.S
        /// </summary>
        public ModelConfig? ModelConfig { get; set; }

        /// <summary>
        /// Gets or sets the raw response text from the model.
        /// </summary>
        public string RawResponse { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parsed value extracted from the raw response.
        /// </summary>
        public string ParsedValue { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the parsing was successful.
        /// </summary>
        public bool ParsedSuccessfully { get; set; }

        /// <summary>
        /// Gets or sets the number of tokens used in the prompt.
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// Gets or sets the number of tokens used in the completion.
        /// </summary>
        public int CompletionTokens { get; set; }

        /// <summary>
        /// Gets or sets the latency of the model call.
        /// </summary>
        public TimeSpan Latency { get; set; }

        /// <summary>
        /// Gets or sets the error code, if any, for the prediction.
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the date and time when the prediction was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
