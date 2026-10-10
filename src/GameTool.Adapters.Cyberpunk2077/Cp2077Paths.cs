using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077;

/// <summary>
/// 存档目录、游戏目录的校验（D020）。全部只读：只检查目录和文件是否存在，不打开、不创建任何东西。
/// </summary>
public static class Cp2077Paths
{
    public const string SlotPattern = "*Save-*";
    public const string ExeRelative = @"bin\x64\Cyberpunk2077.exe";
    public const string ContentRelative = @"archive\pc\content";
    public const string TweaksRelative = @"tools\redmod\tweaks";
    public const string ExpansionArchiveRelative = @"archive\pc\ep1";
    public const string ExpansionTweakDbRelative = @"r6\cache\tweakdb_ep1.bin";

    /// <summary>PathCheck.Expansions 里往日之影的键。</summary>
    public const string PhantomLibertyKey = "phantomLiberty";

    /// <summary>设置面板上游戏目录的检测结果（D023）。</summary>
    public const string DetectedWithExpansion = "已检测到：本体 + 往日之影";
    public const string DetectedBaseOnly = "已检测到：仅本体（往日之影物品将不显示）";

    /// <summary>用户选到游戏目录下面的子目录（如 bin\x64）时，最多往上找这么多层。</summary>
    private const int MaxWalkUp = 4;

    public static string DefaultSaveRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Saved Games",
        "CD Projekt Red",
        "Cyberpunk 2077");

    public const string GameDirectoryHint =
        @"例如 …\steamapps\common\Cyberpunk 2077（Steam），或 GOG / Epic 的安装目录（里面有 bin、archive 文件夹）";

    public static PathCheck CheckSaveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(false, null, "请选择存档目录。");
        if (!TryNormalize(path, out var full))
            return new PathCheck(false, null, "路径无效：" + path);
        try
        {
            if (!Directory.Exists(full))
                return new PathCheck(false, null, "目录不存在：" + full);

            // 选到了某一个存档自己的文件夹（里面直接有 sav.dat）：改用上一级。
            if (File.Exists(Path.Combine(full, "sav.dat")))
            {
                var parent = Path.GetDirectoryName(full);
                var siblings = parent is null ? 0 : CountSlots(parent);
                if (parent is not null && siblings > 0)
                    return new PathCheck(true, parent, "你选的是单个存档的文件夹，已改用上一级目录（共 " + siblings + " 个存档）：" + parent);
                return new PathCheck(false, null, "你选的是单个存档的文件夹，但上一级目录不像存档目录。请选择包含 ManualSave-… / AutoSave-… 等文件夹的那一层。");
            }

            var count = CountSlots(full);
            if (count == 0)
                return new PathCheck(false, null, "这个目录里没有找到存档。存档目录里应有 ManualSave-…、AutoSave-…、QuickSave-… 等文件夹，每个里面有 sav.dat。");
            return new PathCheck(true, full, "找到 " + count + " 个存档。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PathCheck(false, null, "无法读取这个目录：" + ex.Message);
        }
    }

    public static PathCheck CheckGameDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(false, null, "请选择游戏目录。");
        if (!TryNormalize(path, out var full))
            return new PathCheck(false, null, "路径无效：" + path);
        try
        {
            if (!Directory.Exists(full))
                return new PathCheck(false, null, "目录不存在：" + full);

            var root = FindGameRoot(full);
            if (root is null)
                return new PathCheck(false, null, "没有找到 " + ExeRelative + "。请选择游戏的安装目录（" + GameDirectoryHint + "）。");
            if (!Directory.Exists(Path.Combine(root, ContentRelative)))
                return new PathCheck(false, null, "游戏文件不完整：缺少 " + ContentRelative + "。请确认选的是完整的游戏安装目录。");

            var message = string.Equals(root, full, StringComparison.OrdinalIgnoreCase)
                ? "游戏目录可用。"
                : "已改用游戏根目录：" + root;
            var warning = Directory.Exists(Path.Combine(root, TweaksRelative))
                ? null
                : "没有找到 REDmod（" + TweaksRelative + "）。物品库需要它才能列出物品：" + ItemNameCatalog.RedmodUnavailable;
            var expansion = HasPhantomLiberty(root);
            return new PathCheck(true, root, message, warning)
            {
                Detected = expansion ? DetectedWithExpansion : DetectedBaseOnly,
                Expansions = new Dictionary<string, bool>(StringComparer.Ordinal) { [PhantomLibertyKey] = expansion }
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PathCheck(false, null, "无法读取这个目录：" + ex.Message);
        }
    }

    /// <summary>
    /// 游戏根目录里装了往日之影（D023），只读检查：archive\pc\ep1 里至少有一个非空的内容归档（不算 lang_* 文本 / 语音包和 audio_*），
    /// 并且有 r6\cache\tweakdb_ep1.bin。只有空文件夹、只有语言包或缺 tweakdb_ep1.bin 都算没装。
    /// 读不了目录时按没装处理（宁可少列物品，也不生成游戏不认识的物品）。
    /// </summary>
    public static bool HasPhantomLiberty(string gameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        try
        {
            if (!File.Exists(Path.Combine(gameRoot, ExpansionTweakDbRelative)))
                return false;
            var archives = Path.Combine(gameRoot, ExpansionArchiveRelative);
            if (!Directory.Exists(archives))
                return false;
            return Directory.EnumerateFiles(archives, "*.archive").Any(path =>
            {
                var name = Path.GetFileName(path);
                return !name.StartsWith("lang_", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("audio_", StringComparison.OrdinalIgnoreCase)
                    && new FileInfo(path).Length > 0;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>自己或往上几层里第一个有 bin\x64\Cyberpunk2077.exe 的目录；也接受选到上一层（如 steamapps\common）。</summary>
    private static string? FindGameRoot(string full)
    {
        var current = full;
        for (var level = 0; level <= MaxWalkUp && current is not null; level++)
        {
            if (File.Exists(Path.Combine(current, ExeRelative)))
                return current;
            current = Path.GetDirectoryName(current);
        }

        var child = Path.Combine(full, Localization.SteamGameLocator.GameFolderName);
        return File.Exists(Path.Combine(child, ExeRelative)) ? child : null;
    }

    private static int CountSlots(string directory)
    {
        var count = 0;
        foreach (var slot in Directory.EnumerateDirectories(directory, SlotPattern))
        {
            if (File.Exists(Path.Combine(slot, "sav.dat")))
                count++;
        }

        return count;
    }

    private static bool TryNormalize(string path, out string full)
    {
        try
        {
            full = "";
            var trimmed = path.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(trimmed))
                return false;
            full = Path.GetFullPath(trimmed);
            var root = Path.GetPathRoot(full);
            if (!string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = "";
            return false;
        }
    }
}
