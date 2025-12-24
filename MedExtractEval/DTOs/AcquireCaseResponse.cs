namespace MedExtractEval.DTOs
{
    /// <summary>
    /// 分配/领取一个任务的返回
    /// </summary>
    /// <param name="AssignmentId"></param>
    /// <param name="CaseId"></param>
    /// <param name="TaskType"></param>
    /// <param name="RawText"></param>
    /// <param name="MetaInfo"></param>
    /// <param name="Round"></param>
    /// <param name="AssignedAtUtc"></param>
    /// <param name="ExpiresAtUtc"></param>
    public sealed record AcquireCaseResponse(
        Guid AssignmentId,
        Guid CaseId,
        string TaskType,
        string RawText,
        string? MetaInfo,
        int Round,
        DateTime AssignedAtUtc,
        DateTime ExpiresAtUtc
    );
}
