namespace MedExtractEval.DTOs
{
    /// <summary>
    /// 心跳续租返回
    /// </summary>
    /// <param name="Ok"></param>
    /// <param name="ExpiresAtUtc"></param>
    /// <param name="Message"></param>
    public sealed record HeartbeatResponse(
    bool Ok,
    DateTime? ExpiresAtUtc,
    string? Message = null
);

}
