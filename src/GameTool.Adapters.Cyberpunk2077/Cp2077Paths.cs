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

            // 1. 优先检查当前目录本身是否就是包含存档子文件夹的根目录（即便根目录底下不小心散落了单独的 sav.dat 也不误判）。
            var count = CountSlots(full);
            if (count > 0)
                return new PathCheck(true, full, "找到 " + count + " 个存档。");

            // 2. 选到了某一个存档自己的文件夹（里面直接有 sav.dat，或选到了存档文件夹内部的子目录）：改用上一级。
            var current = full;
            for (var up = 0; up < 2 && current is not null; up++)
            {
                if (File.Exists(Path.Combine(current, "sav.dat")))
                {
                    var parent = Path.GetDirectoryName(current);
                    var siblings = parent is null ? 0 : CountSlots(parent);
                    if (siblings == 0 && parent is not null)
                        siblings = CountVascSlots(parent);
                    if (parent is not null && siblings > 0)
                        return new PathCheck(true, parent, "你选的是单个存档的文件夹，已改用上一级目录（共 " + siblings + " 个存档）：" + parent);
                    return new PathCheck(false, null, "你选的是单个存档的文件夹，但上一级目录不像存档目录。请选择包含 ManualSave-… / AutoSave-… 等文件夹的那一层。");
                }
                current = Path.GetDirectoryName(current);
            }

            // 3. 选到了外层父目录（如 Saved Games 或 CD Projekt Red）：自动向下定位 Cyberpunk 2077 存档目录。
            var subSaveRoot = FindSaveRootDownward(full);
            if (subSaveRoot is not null)
            {
                var subCount = CountSlots(subSaveRoot);
                if (subCount == 0)
                    subCount = CountVascSlots(subSaveRoot);
                if (subCount > 0)
                    return new PathCheck(true, subSaveRoot, "已自动定位到存档目录（共 " + subCount + " 个存档）：" + subSaveRoot);
            }

            // 4. 兼容被第三方存档管理器改过文件夹名（不含 Save- 字样，但子文件夹里有真实 VASC 存档头 sav.dat）的情况。
            var vascCount = CountVascSlots(full);
            if (vascCount > 0)
                return new PathCheck(true, full, "找到 " + vascCount + " 个存档。");

            // 5. 误把游戏安装目录选成了存档目录：给出明确纠错提示。
            if (FindGameRoot(full) is not null)
                return new PathCheck(false, null, "这个目录里没有找到存档（你选的是游戏安装目录，不是存档目录！请选择包含 ManualSave-…、AutoSave-… 的存档目录，常见位置：" + DefaultSaveRoot + "）。");

            return new PathCheck(false, null, "这个目录里没有找到存档。存档目录里应有 ManualSave-…、AutoSave-…、QuickSave-… 等文件夹，每个里面有 sav.dat。");
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
            {
                if (CountSlots(full) > 0 || File.Exists(Path.Combine(full, "sav.dat")))
                    return new PathCheck(false, null, "没有找到 " + ExeRelative + "（你选的是存档目录，不是游戏安装目录！请选择装有 bin、archive 文件夹的游戏目录，如 " + GameDirectoryHint + "）。");
                return new PathCheck(false, null, "没有找到 " + ExeRelative + "。请选择游戏的安装目录（" + GameDirectoryHint + "）。");
            }
            if (!Directory.Exists(Path.Combine(root, ContentRelative)))
                return new PathCheck(false, null, "游戏文件不完整：缺少 " + ContentRelative + "。请确认选的是完整的游戏安装目录。");

            var message = string.Equals(root, full, StringComparison.OrdinalIgnoreCase)
                ? "游戏目录可用。"
                : "已改用游戏根目录：" + root;
            var tweaksDir = Path.Combine(root, TweaksRelative);
            string? warning = null;
            if (!Directory.Exists(tweaksDir))
            {
                warning = "没有找到 REDmod（" + TweaksRelative + "）。物品库需要它才能列出物品：" + ItemNameCatalog.RedmodUnavailable;
            }
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

    /// <summary>自己或往上几层里第一个有 bin\x64\Cyberpunk2077.exe 的目录；也接受选到外层（如 steam、steamapps、steamapps\common、GOG Games、Epic Games）。</summary>
    private static string? FindGameRoot(string full)
    {
        var current = full;
        for (var level = 0; level <= MaxWalkUp && current is not null; level++)
        {
            if (File.Exists(Path.Combine(current, ExeRelative)))
                return current;
            current = Path.GetDirectoryName(current);
        }

        string[] relativeCandidates =
        [
            Localization.SteamGameLocator.GameFolderName,
            "Cyberpunk2077",
            Path.Combine("common", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("steamapps", "common", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("Steam", "steamapps", "common", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("SteamLibrary", "steamapps", "common", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("Games", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("GOG Galaxy", "Games", Localization.SteamGameLocator.GameFolderName),
            Path.Combine("Epic Games", "Cyberpunk2077"),
            Path.Combine("Epic Games", Localization.SteamGameLocator.GameFolderName)
        ];

        foreach (var rel in relativeCandidates)
        {
            var candidate = Path.Combine(full, rel);
            if (File.Exists(Path.Combine(candidate, ExeRelative)))
                return candidate;
        }

        return null;
    }

    /// <summary>当用户选到 Saved Games 或 CD Projekt Red 等外层目录时，自动向下探查标准存档目录。</summary>
    private static string? FindSaveRootDownward(string full)
    {
        string[] relativeCandidates =
        [
            "Cyberpunk 2077",
            Path.Combine("CD Projekt Red", "Cyberpunk 2077"),
            Path.Combine("Saved Games", "CD Projekt Red", "Cyberpunk 2077"),
            Path.Combine("OneDrive", "Saved Games", "CD Projekt Red", "Cyberpunk 2077")
        ];

        foreach (var rel in relativeCandidates)
        {
            var candidate = Path.Combine(full, rel);
            if (Directory.Exists(candidate) && (CountSlots(candidate) > 0 || CountVascSlots(candidate) > 0))
                return candidate;
        }

        return null;
    }

    private static int CountSlots(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;
        var count = 0;
        foreach (var slot in Directory.EnumerateDirectories(directory, SlotPattern))
        {
            if (File.Exists(Path.Combine(slot, "sav.dat")))
                count++;
        }

        return count;
    }

    /// <summary>兼容存档文件夹被改名（不含 Save-），但子文件夹内确实有以 VASC 魔数开头的 2077 存档文件。</summary>
    private static int CountVascSlots(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;
        var count = 0;
        Span<byte> magic = stackalloc byte[4];
        foreach (var slot in Directory.EnumerateDirectories(directory))
        {
            var sav = Path.Combine(slot, "sav.dat");
            if (!File.Exists(sav))
                continue;
            try
            {
                using var fs = new FileStream(sav, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length >= 12 && fs.Read(magic) == 4 && magic.SequenceEqual(Cp2077Format.Vasc))
                    count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
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
