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

var defaultModelName = "ministral-3:14b";
var defaultVersionTage = "4760c35aeb9d";
var defaultTemperature = 0.15;
var defaultTopP = 0.90;
var defaultProvider = "Ollama";

var store = new ExtractionStore(ConnectionStringResolvers.ConsolePrompt());

var experiments = await store.ListExperimentsAsync();

foreach (var exp in experiments)
{
    if (sysPromtDict.Keys.Where(k => exp.Name.Contains(k)).FirstOrDefault() is string key)
    {
        var sysPrompt = sysPromtDict[key];
        var modelConfig = await store.CreateOrGetModelConfigAsync(
            modelName: defaultModelName,
            versionTag: defaultVersionTage,
            temperature: defaultTemperature,
            topP: defaultTopP,
            provider: defaultProvider,
            promptTemplate: sysPrompt,
            isDeterministic: false);

        var cases = await store.ListPendingCaseItemsAsync(exp, modelConfig);

        foreach (var caseItem in cases)
        {
            var ollamaTask = new OllamaTask(ollama, modelConfig.ModelName, modelConfig.PromptTemplate, caseItem.RawText);
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
