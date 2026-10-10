using System.Globalization;
using System.Text.Json;
using GameTool.Core;

namespace GameTool.App;

public sealed record ThemeColors(string Accent, string AccentText, double DefaultOverlayOpacity);

public static class ThemeCatalog
{
    public static ThemeColors Default { get; } = new("#3ee0ff", "#041018", 0.42);

    public static ThemeColors Load(IGameAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter is not IThemeProvider provider)
            return Default;

        try
        {
            using var stream = provider.OpenTheme();
            return stream is null ? Default : Parse(stream);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            return Default;
        }
    }

    public static ThemeColors Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("主题不是对象。");
        var accent = ReadColor(root, "accent");
        var accentText = ReadColor(root, "accentText");
        if (!root.TryGetProperty("defaultOverlayOpacity", out var opacity) || opacity.ValueKind != JsonValueKind.Number || !opacity.TryGetDouble(out var value))
            throw new InvalidDataException("主题缺少 defaultOverlayOpacity。");
        if (value < 0 || value > 0.8)
            throw new InvalidDataException("主题的默认遮罩超出范围。");
        return new ThemeColors(accent, accentText, value);
    }

    private static string ReadColor(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"主题缺少 {name}。");
        var text = value.GetString() ?? "";
        if (text.Length != 7 || text[0] != '#')
            throw new InvalidDataException($"主题的 {name} 必须是 #RRGGBB。");
        for (var i = 1; i < text.Length; i++)
        {
            if (!Uri.IsHexDigit(text[i]))
                throw new InvalidDataException($"主题的 {name} 必须是 #RRGGBB。");
        }

        return "#" + text[1..].ToLower(CultureInfo.InvariantCulture);
    }
}
