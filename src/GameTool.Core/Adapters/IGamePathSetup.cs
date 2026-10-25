namespace GameTool.Core;

/// <summary>
/// 一次路径校验的结果。Ok 为 true 时 Path 是规范化后实际采用的目录（可能与用户选的不同，比如改用上一级），
/// Message 说明结果或失败原因；Warning 是可以继续但要提醒用户的事（比如缺少可选组件）。
/// </summary>
public sealed record PathCheck(bool Ok, string? Path, string Message, string? Warning = null)
{
    /// <summary>游戏目录里检测到的内容（如「已检测到：本体 + 往日之影」），给界面原样显示；不适用时为 null。</summary>
    public string? Detected { get; init; }

    /// <summary>检测到的资料片：键由适配器定义（如 phantomLiberty），值为是否安装。不适用时为 null。</summary>
    public IReadOnlyDictionary<string, bool>? Expansions { get; init; }
}

/// <summary>
/// 适配器可选：存档目录和游戏目录由用户手动设置（D020）。适配器只负责校验和接收游戏目录，
/// 设置的保存由应用层负责；桌面端不再自动探测路径。
/// </summary>
public interface IGamePathSetup
{
    /// <summary>存档目录的常见位置（只作提示，不自动填入）。</summary>
    string SaveRootHint { get; }

    /// <summary>游戏目录的常见位置（只作提示，不自动填入）。</summary>
    string GameDirectoryHint { get; }

    /// <summary>校验存档目录：只读检查，不创建、不修改任何东西。</summary>
    PathCheck CheckSaveRoot(string? path);

    /// <summary>校验游戏目录：只读检查，不创建、不修改任何东西。</summary>
    PathCheck CheckGameDirectory(string? path);

    /// <summary>当前使用的游戏目录；null 表示还没有设置，依赖游戏文件的功能（名字、图标）不可用。</summary>
    string? GameDirectory { get; }

    /// <summary>改用新的游戏目录（null 表示清除）。之后打开的存档、名字表和图标都基于新目录。</summary>
    void UseGameDirectory(string? gameDirectory);

    /// <summary>当前使用的显示/词条语言（如 zh-cn / zh-tw / en-us）。</summary>
    string Language => "zh-cn";

    /// <summary>切换词条语言。</summary>
    void UseLanguage(string? language) { }
}
