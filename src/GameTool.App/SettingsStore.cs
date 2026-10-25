using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GameTool.App;

/// <summary>一个游戏的两项路径设置（D020）。未设置为 null。</summary>
public sealed record GamePaths(string? SaveRoot, string? GameDir)
{
    public static GamePaths Empty { get; } = new(null, null);
}

/// <summary>
/// 用户设置（D020）：默认在 %LOCALAPPDATA%\GameTool\settings.json，按游戏 id 分别保存存档目录和游戏目录：
/// <c>{ "games": { "cp2077": { "saveRoot": "…", "gameDir": "…" } }, "helpShown": true }</c>。
/// <c>helpShown</c> 是程序级的标记（D022）：「说明」已经自动弹出过一次，以后不再自动弹出；旧文件没有这一项，当作未弹出。
/// 读取时容错：文件不存在、损坏或字段类型不对，一律当作未设置（games 和 helpShown 各自独立判断）；
/// 写入先写临时文件再整体替换，不会留下半个文件，并保留另一部分设置。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Lock _gate = new();

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath();
    }

    public string FilePath { get; }

    public static string DefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameTool",
        "settings.json");

    public GamePaths Get(string gameId)
    {
        RequireGameId(gameId);
        lock (_gate)
        {
            var file = ReadFile();
            return file.Games.TryGetValue(gameId, out var entry) && entry is not null
                ? new GamePaths(Clean(entry.SaveRoot), Clean(entry.GameDir))
                : GamePaths.Empty;
        }
    }

    public void Set(string gameId, GamePaths paths)
    {
        RequireGameId(gameId);
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            // 其他游戏的设置原样保留；文件损坏时从空白重新开始。
            var file = ReadFile();
            file.Games[gameId] = new GameEntry { SaveRoot = Clean(paths.SaveRoot), GameDir = Clean(paths.GameDir) };
            WriteFile(file);
        }
    }

    /// <summary>「说明」是否已经自动弹出过（D022）。没有这一项或类型不对都当作 false。</summary>
    public bool GetHelpShown()
    {
        lock (_gate)
            return ReadFile().HelpShown == true;
    }

    public void SetHelpShown(bool shown)
    {
        lock (_gate)
        {
            // 各游戏的路径设置原样保留。
            var file = ReadFile();
            file.HelpShown = shown ? true : null;
            WriteFile(file);
        }
    }

    public string GetLanguage()
    {
        lock (_gate)
            return NormalizeLanguage(ReadFile().Language);
    }

    public void SetLanguage(string? language)
    {
        lock (_gate)
        {
            var file = ReadFile();
            file.Language = string.IsNullOrWhiteSpace(language) ? null : NormalizeLanguage(language);
            WriteFile(file);
        }
    }

    public static string NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return "zh-cn";
        var lower = lang.Trim().ToLowerInvariant();
        if (lower.StartsWith("zh-tw", StringComparison.Ordinal) || lower.StartsWith("zh-hk", StringComparison.Ordinal))
            return "zh-tw";
        if (lower.StartsWith("en", StringComparison.Ordinal))
            return "en-us";
        return "zh-cn";
    }

    private SettingsFile ReadFile()
    {
        JsonObject? root;
        try
        {
            if (!File.Exists(FilePath))
                return new SettingsFile();
            root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new SettingsFile();
        }

        if (root is null)
            return new SettingsFile();
        var file = new SettingsFile
        {
            HelpShown = root["helpShown"] is JsonValue flag && flag.GetValueKind() == JsonValueKind.True ? true : null,
            Language = root["language"] is JsonValue langVal && langVal.GetValueKind() == JsonValueKind.String
                ? Clean(langVal.GetValue<string>())
                : null
        };
        try
        {
            var games = root["games"]?.Deserialize<Dictionary<string, GameEntry?>>(JsonOptions);
            if (games is not null)
            {
                foreach (var (key, value) in games)
                {
                    if (!string.IsNullOrWhiteSpace(key) && value is not null)
                        file.Games[key] = value;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            file.Games.Clear();
        }

        return file;
    }

    private void WriteFile(SettingsFile file)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
            if (File.Exists(FilePath))
                File.Replace(temporary, FilePath, destinationBackupFileName: null);
            else
                File.Move(temporary, FilePath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RequireGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("缺少游戏 id。", nameof(gameId));
    }

    private sealed class SettingsFile
    {
        public Dictionary<string, GameEntry> Games { get; set; } = new(StringComparer.Ordinal);

        /// <summary>只在为 true 时写出；false 时省略，文件和旧版写出的一样。</summary>
        public bool? HelpShown { get; set; }

        /// <summary>界面与词条语言（zh-cn / zh-tw / en-us）；未设置时省略。</summary>
        public string? Language { get; set; }
    }

    private sealed class GameEntry
    {
        public string? SaveRoot { get; set; }

        public string? GameDir { get; set; }
    }
}
