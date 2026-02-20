using MedExtractEval.Shared.Model;

namespace MedExtract.OllamaExtractor
{
    internal class OllamaExtraction : ModelExtraction
    {
        public OllamaExtraction(Experiment experiment, CaseItem caseItem, ModelConfig modelConfig)
        {
            ExperimentId = experiment.Id;
            Experiment = experiment;
            CaseId = caseItem.Id;
            Case = caseItem;
            ModelConfigId = modelConfig.Id;
            ModelConfig = modelConfig;
        }

        /// <summary>
        /// 转换 OllamaChatRunResult 到 ModelExtraction 的字段
        /// </summary>
        /// <param name="runResult"></param>
        /// <param name="cancellationToken"></param>
        public void FromChatRunResultAsync(OllamaChatRunResult runResult, CancellationToken cancellationToken = default)
        {
            //把整个res json化保存在RawRespones里
            RawResponse = System.Text.Json.JsonSerializer.Serialize(runResult);
            ParsedValue = runResult.ContentText.Trim(); // 这里假设我们关心的是content文本，实际可以根据需求调整
            ParsedSuccessfully = runResult.Completed && !string.IsNullOrEmpty(ParsedValue);
#warning 这里调用有问题，不应该强转long to int
            PromptTokens = (int)(runResult.Usage.PromptEvalCount ?? 0);
            CompletionTokens = (int)(runResult.Usage.EvalCount ?? 0);
            Latency = runResult.Metrics.EndToEnd;
            ErrorCode = string.Empty; // 这里假设没有错误码，如果有错误信息可以从runResult或异常中提取
        }
    }
}