namespace MedExtractEval.Shared.Model
{
    /// <summary>
    /// Represents the configuration of a model used for predictions.
    /// </summary>
    public class ModelConfig
    {
        /// <summary>
        /// Gets or sets the unique identifier for the model configuration.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the provider of the model (e.g., OpenAI, DeepSeek, local Qwen).
        /// </summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the model.
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the version tag for the model.
        /// </summary>
        public string VersionTag { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the prompt template used for the model.
        /// </summary>
        public string PromptTemplate { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the temperature parameter for the model.
        /// </summary>
        public double Temperature { get; set; }

        /// <summary>
        /// Gets or sets the top-p parameter for the model.
        /// </summary>
        public double TopP { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the model is deterministic.
        /// </summary>
        public bool IsDeterministic { get; set; }
    }
}
