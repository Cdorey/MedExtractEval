using OllamaSharp;

var http = new HttpClient
{
    BaseAddress = new Uri("http://CdoreyMacMini:11434"),
    Timeout = TimeSpan.FromMinutes(10)
};

var ollama = new OllamaApiClient(http);
