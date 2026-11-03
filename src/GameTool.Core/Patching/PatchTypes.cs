namespace GameTool.Core;

public static class ChangeOutcome
{
    public const string Changed = "changed";
    public const string Unchanged = "unchanged";
    public const string NotHeld = "not_held";
    public const string Inserted = "inserted";
    public const string Ambiguous = "ambiguous";
}

public static class PatchOutcome
{
    public const string Patched = "patched";
    public const string NoChange = "no_change";
}

public sealed class AmbiguousResourceException : Exception
{
    public AmbiguousResourceException(ResourceId id)
        : base(UiText.T($"资源 {id.Value} 存在多个堆叠，拒绝把它当成未持有。", $"資源 {id.Value} 存在多個堆疊，拒絕把它當成未持有。", $"Resource {id.Value} has multiple stacks; refusing to treat it as not held."))
    {
        ResourceId = id;
    }

    public ResourceId ResourceId { get; }
}

public class PatchRejectedException : Exception
{
    public PatchRejectedException(string message) : base(message)
    {
    }
}

/// <summary>补丁写回前发现存档已被改写。调用方可以重试。</summary>
public sealed class SaveChangedDuringPatchException : PatchRejectedException
{
    public SaveChangedDuringPatchException() : base(UiText.T("存档在补丁过程中被改写", "存檔在補丁過程中被改寫", "The save was modified while it was being patched"))
    {
    }
}

/// <summary>
/// 备份路径。null 表示写入时不生成 .bak。
/// </summary>
public sealed record PatchOptions(string? BackupPath);

public sealed record PatchReport(
    string Outcome,
    IReadOnlyList<ResourceChangeResult> Results,
    string? BackupPath,
    long ElapsedMilliseconds,
    DateTime? WrittenLastWriteTimeUtc = null)
{
    /// <summary>
    /// 写出的存档字节数（与 <see cref="WrittenLastWriteTimeUtc"/> 一起识别「这次文件变化是自己写的」：
    /// 文件时间的精度不够区分紧挨着的两次写入）。没有写文件时为 null。
    /// </summary>
    public long? WrittenLength { get; init; }
}

public sealed record RestoreReport(string SaveFilePath, long ElapsedMilliseconds);
