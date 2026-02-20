namespace MedExtract.OllamaExtractor
{
    /// <summary>
    /// 低开销进度快照：主要用于 UI/轮询判断是否“在动”、是否首字已出、以及输出量级。
    /// 不保证“百分比进度”（因为服务端不会在流式过程中持续给 token 统计）。
    /// </summary>
    public sealed record OllamaTaskProgressSnapshot(
        OllamaTaskState State,
        DateTimeOffset? StartedAt,
        DateTimeOffset? FirstTokenAt,
        DateTimeOffset? LastChunkAt,
        long ChunksReceived,
        long ThinkingCharsReceived,
        long ContentCharsReceived
    );
}
