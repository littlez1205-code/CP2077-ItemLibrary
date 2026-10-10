namespace GameTool.Core;

/// <summary>
/// 可选的格式专属校验。内核先做通用校验，文档实现了本接口时再调用它。
/// </summary>
public interface IStrictVerifier
{
    void VerifyOnlyTargetsChanged(ISaveDocument original, IReadOnlyList<ResourceChange> changes);
}
