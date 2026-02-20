using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using System.Diagnostics;
using System.Text;

namespace MedExtract.OllamaExtractor
{
    internal class OllamaTask(OllamaApiClient ollama, string model, string sysMsg, string usrMsg)
    {
        private readonly OllamaApiClient _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        private readonly string _model = model ?? throw new ArgumentNullException(nameof(model));
        private readonly string _sysMsg = sysMsg ?? string.Empty;
        private readonly string _usrMsg = usrMsg ?? string.Empty;

        /// <summary>
        /// 可选：是否捕获完整 thinking（有些模型 think 会非常长；捕获会占内存）
        /// </summary>
        public bool CaptureThinking { get; init; } = true;

        /// <summary>
        /// 可选：是否捕获完整 content
        /// </summary>
        public bool CaptureContent { get; init; } = true;

        /// <summary>
        /// 可选：是否启用 think
        /// </summary>
        public bool ThinkEnabled { get; init; } = true;

        /// <summary>
        /// 可选：限制输出 token
        /// </summary>
        public int? NumPredict { get; init; } = null;

        // 可选：流式增量回调（如果你想实时显示输出，而不是等结果返回）
        public event Action<string>? OnThinkingDelta;
        public event Action<string>? OnContentDelta;

        // ---------- 进度（低开销，可轮询） ----------
        private long _chunks;
        private long _thinkingChars;
        private long _contentChars;
        private volatile OllamaTaskState _state = OllamaTaskState.NotStarted;

        private DateTimeOffset? _startedAt;
        private DateTimeOffset? _firstTokenAt;
        private DateTimeOffset? _lastChunkAt;

        /// <summary>
        /// 进度快照（线程安全，便于 UI/监控系统轮询）
        /// </summary>
        public OllamaTaskProgressSnapshot Progress =>
            new(
                State: _state,
                StartedAt: _startedAt,
                FirstTokenAt: _firstTokenAt,
                LastChunkAt: _lastChunkAt,
                ChunksReceived: Interlocked.Read(ref _chunks),
                ThinkingCharsReceived: Interlocked.Read(ref _thinkingChars),
                ContentCharsReceived: Interlocked.Read(ref _contentChars)
            );

        public async Task<OllamaChatRunResult> RunAsync(CancellationToken cancellationToken = default)
        {
            if (_state != OllamaTaskState.NotStarted)
                throw new InvalidOperationException("This task instance has already been started.");

            _state = OllamaTaskState.Streaming;
            _startedAt = DateTimeOffset.UtcNow;

            StringBuilder? thinkingSb = CaptureThinking ? new StringBuilder(capacity: 1024) : null;
            StringBuilder? contentSb = CaptureContent ? new StringBuilder(capacity: 1024) : null;

            var sw = Stopwatch.StartNew();
            TimeSpan? ttft = null;

            ChatDoneResponseStream? done = null;

            try
            {
                ChatRequest req = BuildRequest();

                await foreach (ChatResponseStream? chunk in _ollama.ChatAsync(req).WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (chunk?.Message == null) continue;

                    Interlocked.Increment(ref _chunks);
                    _lastChunkAt = DateTimeOffset.UtcNow;

                    // 记录 TTFT（第一次收到 thinking 或 content 的增量）
                    var thinkingDelta = chunk.Message.Thinking ?? string.Empty;
                    var contentDelta = chunk.Message.Content ?? string.Empty;

                    if (ttft == null && (thinkingDelta.Length > 0 || contentDelta.Length > 0))
                    {
                        ttft = sw.Elapsed;
                        _firstTokenAt = DateTimeOffset.UtcNow;
                    }

                    // 更新计数（原子加，便于低开销轮询）
                    if (thinkingDelta.Length > 0) Interlocked.Add(ref _thinkingChars, thinkingDelta.Length);
                    if (contentDelta.Length > 0) Interlocked.Add(ref _contentChars, contentDelta.Length);

                    // 增量回调（给 UI/日志系统用）
                    if (thinkingDelta.Length > 0) OnThinkingDelta?.Invoke(thinkingDelta);
                    if (contentDelta.Length > 0) OnContentDelta?.Invoke(contentDelta);

                    // 捕获文本（可选，避免内存暴涨）
                    if (thinkingSb != null && thinkingDelta.Length > 0) thinkingSb.Append(thinkingDelta);
                    if (contentSb != null && contentDelta.Length > 0) contentSb.Append(contentDelta);

                    // done chunk
                    if (chunk is ChatDoneResponseStream last)
                    {
                        done = last;
                        break;
                    }
                }

                sw.Stop();

                if (done is null)
                    throw new InvalidOperationException("Ollama stream ended without a done chunk.");

                OllamaChatRunResult result = BuildResult(done, ttft, sw.Elapsed,
                    thinkingSb?.ToString() ?? string.Empty,
                    contentSb?.ToString() ?? string.Empty);

                _state = OllamaTaskState.Completed;
                return result;
            }
            catch (OperationCanceledException)
            {
                _state = OllamaTaskState.Canceled;
                sw.Stop();
                throw;
            }
            catch
            {
                _state = OllamaTaskState.Faulted;
                sw.Stop();
                throw;
            }
        }

        private ChatRequest BuildRequest()
        {
            var req = new ChatRequest
            {
                Model = _model,
                Stream = true,
                Think = ThinkEnabled,
                Messages =
                [
                    new Message(ChatRole.System, _sysMsg),
                new Message(ChatRole.User, _usrMsg)
                ]
            };

            // options：按需限制输出
            if (NumPredict.HasValue)
            {
                req.Options ??= new RequestOptions();
                req.Options.NumPredict = NumPredict.Value;
            }

            return req;
        }

        private static OllamaChatRunResult BuildResult(ChatDoneResponseStream done, TimeSpan? ttft, TimeSpan e2e, string thinkingText, string contentText)
        {
            // usage 字段可能为 null，duration 也可能为 0，需要防御性计算
            var promptTok = done.PromptEvalCount;
            var promptNs = done.PromptEvalDuration;
            var outTok = done.EvalCount;
            var outNs = done.EvalDuration;

            var prefillTps = CalcTokensPerSecond(promptTok, promptNs);
            var decodeTps = CalcTokensPerSecond(outTok, outNs);

            var usage = new OllamaUsage(
                PromptEvalCount: promptTok,
                PromptEvalDurationNs: promptNs,
                EvalCount: outTok,
                EvalDurationNs: outNs,
                LoadDurationNs: done.LoadDuration,
                TotalDurationNs: done.TotalDuration
            );

            var metrics = new OllamaChatMetrics(
                TimeToFirstToken: ttft,
                EndToEnd: e2e,
                PrefillTokensPerSecond: prefillTps,
                DecodeTokensPerSecond: decodeTps
            );

            return new OllamaChatRunResult(
                Model: done.Model ?? string.Empty,         // done 里通常会带最终实际模型名
                SystemMessage: "",                         // 可按需在上层补充
                UserMessage: "",
                ThinkEnabled: true,
                Completed: true,
                ThinkingText: thinkingText,
                ContentText: contentText,
                Usage: usage,
                Metrics: metrics
            );
        }

        private static double? CalcTokensPerSecond(long? tokens, long? durationNs)
        {
            if (!tokens.HasValue || !durationNs.HasValue) return null;
            if (tokens.Value < 0) return null;
            if (durationNs.Value <= 0) return null;

            var seconds = durationNs.Value / 1e9;
            return seconds <= 0 ? null : tokens.Value / seconds;
        }
    }
}
