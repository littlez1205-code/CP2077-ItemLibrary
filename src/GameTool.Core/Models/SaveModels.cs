namespace GameTool.Core;

public sealed record SaveVersionInfo(uint SaveVersion, uint GameVersion);

public sealed record SaveSlotInfo(
    string SlotDirectory,
    string SaveFilePath,
    string SlotName,
    DateTime LastWriteTimeUtc,
    string Group = "");

/// <summary>
/// 唯一的监听模式：存档根目录 + 相对通配。防抖毫秒的安全范围是 0..60_000；2077 使用 150。
/// </summary>
public sealed record SaveWatchSpec(
    string SaveRoot,
    IReadOnlyList<string> IncludeGlobs,
    int DebounceMilliseconds);
