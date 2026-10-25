using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameTool.Adapters.Cyberpunk2077.Localization;

public sealed record LocText(IReadOnlyList<LocEntry> Entries, int BaseCount, int Ep1Count, int Overridden);

public static partial class LocalizationCatalog
{
    public static string ArchivePath(string gameDirectory, string lang, string pack) =>
        Path.Combine(gameDirectory, "archive", "pc", pack, $"lang_{lang}_text.archive");

    public static string OodlePath(string gameDirectory)
    {
        var primary = Path.Combine(gameDirectory, "bin", "x64", "oo2ext_7_win64.dll");
        if (File.Exists(primary))
            return primary;
        try
        {
            var binDir = Path.Combine(gameDirectory, "bin", "x64");
            if (Directory.Exists(binDir))
            {
                var alt = Directory.EnumerateFiles(binDir, "oo2ext_*_win64.dll").FirstOrDefault();
                if (alt is not null)
                    return alt;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return primary;
    }

    public static IEnumerable<string> OnscreensPaths(string lang)
    {
        RequireLang(lang);
        yield return $@"base\localization\{lang}\onscreens\onscreens.json";
        yield return $@"ep1\localization\{lang}\onscreens\onscreens.json";
        for (var n = 0; n <= 32; n++)
        {
            yield return $@"base\localization\{lang}\onscreens\onscreens_{n}.json";
            yield return $@"ep1\localization\{lang}\onscreens\onscreens_{n}.json";
        }
    }

    public static LocText Load(string gameDirectory, string lang, IOodleDecompressor? oodle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        RequireLang(lang);
        var contentPath = ArchivePath(gameDirectory, lang, "content");
        var expansionPath = ArchivePath(gameDirectory, lang, "ep1");
        if (!File.Exists(contentPath))
            throw new InvalidDataException($"找不到本地化归档：{contentPath}");
        oodle ??= OodleNativeDecompressor.Load(OodlePath(gameDirectory));
        var baseEntries = ReadOnscreens(RdarArchive.Open(contentPath), lang, oodle);
        // 往日之影的文本包可选（D023）：没买资料片的玩家没有 archive\pc\ep1，只用本体文本，Ep1Count 为 0。
        IReadOnlyList<LocEntry> expansion = File.Exists(expansionPath)
            ? ReadOnscreens(RdarArchive.Open(expansionPath), lang, oodle)
            : [];
        var (merged, overridden) = Merge(baseEntries, expansion);
        return new LocText(merged, baseEntries.Count, expansion.Count, overridden);
    }

    public static (IReadOnlyList<LocEntry> Entries, int Overridden) Merge(IReadOnlyList<LocEntry> baseEntries, IReadOnlyList<LocEntry> expansion)
    {
        ArgumentNullException.ThrowIfNull(baseEntries);
        ArgumentNullException.ThrowIfNull(expansion);
        var merged = new List<LocEntry>(baseEntries);
        var index = new Dictionary<ulong, int>();
        for (var i = 0; i < merged.Count; i++)
            index[merged[i].PrimaryKey] = i;
        var replaced = new HashSet<ulong>();
        foreach (var entry in expansion)
        {
            if (index.TryGetValue(entry.PrimaryKey, out var at))
            {
                merged[at] = entry;
                replaced.Add(entry.PrimaryKey);
                continue;
            }

            index[entry.PrimaryKey] = merged.Count;
            merged.Add(entry);
        }

        return (merged, replaced.Count);
    }

    public static IReadOnlyList<LocEntry> ReadOnscreens(RdarArchive archive, string lang, IOodleDecompressor oodle)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(oodle);
        RequireLang(lang);
        var entries = new List<LocEntry>();
        foreach (var path in OnscreensPaths(lang))
        {
            var file = archive.Find(Fnv1a64.HashPath(path));
            if (file is null)
                continue;
            entries.AddRange(OnscreensReader.Read(archive.Read(file, oodle)));
        }

        if (entries.Count == 0)
            throw new InvalidDataException($"归档里没有 {lang} 的 onscreens。");
        return entries;
    }

    public static IReadOnlyList<LocEntry> Find(IReadOnlyList<LocEntry> entries, string key)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.StartsWith("LocKey#", StringComparison.Ordinal))
        {
            if (!ulong.TryParse(key["LocKey#".Length..], out var id))
                throw new InvalidDataException($"无法解析 {key}。");
            return entries.Where(entry => entry.PrimaryKey == id).ToArray();
        }

        return entries.Where(entry => entry.SecondaryKey == key).ToArray();
    }

    public static void RequireExportDirectory(string outputDirectory, string gameDirectory, string saveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var full = Path.GetFullPath(outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (File.Exists(full) || Directory.Exists(full))
            throw new InvalidDataException("输出目录已经存在。");
        RejectInside(full, gameDirectory, "输出目录位于游戏目录内。");
        RejectInside(full, saveRoot, "输出目录位于存档根目录内。");
    }

    public static string Export(LocText text, string outputDirectory, string lang)
    {
        ArgumentNullException.ThrowIfNull(text);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "onscreens.json");
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        writer.WriteString("lang", lang);
        writer.WriteNumber("baseCount", text.BaseCount);
        writer.WriteNumber("ep1Count", text.Ep1Count);
        writer.WriteNumber("overridden", text.Overridden);
        writer.WriteNumber("count", text.Entries.Count);
        writer.WriteStartArray("entries");
        foreach (var entry in text.Entries)
        {
            writer.WriteStartObject();
            writer.WriteNumber("primaryKey", entry.PrimaryKey);
            writer.WriteString("secondaryKey", entry.SecondaryKey);
            writer.WriteString("femaleVariant", entry.FemaleVariant);
            writer.WriteString("maleVariant", entry.MaleVariant);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return path;
    }

    private static void RejectInside(string full, string root, string message)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;
        var normalized = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var prefix = normalized + Path.DirectorySeparatorChar;
        if (full.Equals(normalized, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static void RequireLang(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || !LangPattern().IsMatch(lang))
            throw new InvalidDataException("语言代码只允许小写字母、数字和连字符。");
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LangPattern();
}
