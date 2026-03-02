using MedExtract.Base;
using MedExtract.OllamaExtractor;
using MedExtractEval.Shared.Model;
using OllamaSharp;

var http = new HttpClient
{
    BaseAddress = new Uri("http://CdoreyMacMini:11434"),
    Timeout = TimeSpan.FromMinutes(10)
};

var ollama = new OllamaApiClient(http);
var sysPromtDict = new Dictionary<string, string>
{
    { "ExamEjectionFraction","对心脏超声的报告提取信息，寻找射血分数的值，如果没有回复null，你只需要回复数字或者null，无视其他的混杂信息：" },
    { "ExamCarotidIMTPlaque","对颈动脉超声的报告提取信息，寻找报告的颈动脉IMT，如果有多个值，回复最高IMT，如果没有回复null，你只需要回复数字或者null，无视其他的混杂信息：" },
    { "ExamCoronaryCTA","对冠脉CTA报告的诊断结论进行分类，你只回复一个True/False，存在狭窄或斑块为True，无该问题为False，如果是支架术后，认为是发生过狭窄，其他类型的异常不需要关注：" },
};

var store = new ExtractionStore(ConnectionStringResolvers.ConsolePrompt());
var experiments = await store.ListExperimentsAsync();

ModelRunConfig[] modelConfigs = [
    //new ModelRunConfig("qwen3-vl:30b","eda0be100877",0,0.95,"Ollama",false),
    //new ModelRunConfig("qwen3-next:80b","b2ebb986e4e9",0,0.95,"Ollama",false),
    new ModelRunConfig("qwen3:32b","030ee887880f",0,0.95,"Ollama",false),
    new ModelRunConfig("qwen3:32b","030ee887880f",0.6f,0.95,"Ollama",false),
    //new ModelRunConfig("qwen3:30b","ad815644918f",0,0.95,"Ollama",false),
    new ModelRunConfig("qwen3:0.6b","7df6b6e09427",0,0.95,"Ollama",false),
    new ModelRunConfig("qwen3:0.6b","7df6b6e09427",0.6f,0.95,"Ollama",false),
    //new ModelRunConfig("ministral-3:3b","f04aa1c738f6",0,0.90,"Ollama",false),
    //new ModelRunConfig("ministral-3:14b","4760c35aeb9d",0,0.90,"Ollama",false),
    //new ModelRunConfig("gemma3:4b","a2af6cc3eb7f",0,0.95,"Ollama",false),
    //new ModelRunConfig("gemma3:27b","a418f5838eaf",0,0.95,"Ollama",false),
    ];

foreach (var config in modelConfigs)
{
    await MainTreatmentLoop(config);
}


async Task MainTreatmentLoop(ModelRunConfig runConfig)
{
    foreach (var exp in experiments)
    {
        if (sysPromtDict.Keys.Where(k => exp.Name.Contains(k)).FirstOrDefault() is string key)
        {
            var sysPrompt = sysPromtDict[key];
            var modelConfig = await store.CreateOrGetModelConfigAsync(
                modelName: runConfig.ModelName,
                versionTag: runConfig.VersionTag,
                temperature: runConfig.Temperature,
                topP: runConfig.TopP,
                provider: runConfig.Provider,
                promptTemplate: sysPrompt,
                isDeterministic: false,
                isThinking: runConfig.ThinkEnable);

            var cases = await store.ListPendingCaseItemsAsync(exp, modelConfig);

            foreach (var caseItem in cases)
            {
                var ollamaTask = new OllamaTask(ollama, modelConfig.ModelName, modelConfig.PromptTemplate, caseItem.RawText, runConfig.Temperature, modelConfig.IsThinking);
                var x = await ollamaTask.RunAsync();
                var extraction = new OllamaExtraction(exp, caseItem, modelConfig);
                extraction.FromChatRunResult(x);

                await store.AddModelExtractionAsync(extraction);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(x.ThinkingText);
                Console.ResetColor();
                Console.WriteLine(x.ContentText);
                Console.WriteLine($"PromptTokens: {extraction.PromptTokens}, CompletionTokens: {extraction.CompletionTokens}, Latency: {extraction.Latency.TotalSeconds}s");
            }
        }
    }
}
