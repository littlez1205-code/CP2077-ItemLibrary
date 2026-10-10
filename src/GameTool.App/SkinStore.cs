using System.Text.Json;
using GameTool.Core;

namespace GameTool.App;

public sealed class SkinRejectedException : Exception
{
    public SkinRejectedException(string message) : base(message)
    {
    }
}

public sealed record SkinState(bool HasImage, double OverlayOpacity, string? ImageVersion);

public sealed record SkinImage(byte[] Bytes, string ContentType);

public sealed class SkinStore
{
    public const long MaxBytes = 20L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ImageNames = ["background.png", "background.jpg", "background.webp"];

    private readonly AdapterRegistry _registry;

    public SkinStore(AdapterRegistry registry, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTool",
            "skins");
    }

    public string Root { get; }

    public ThemeColors Theme(string gameId) => ThemeCatalog.Load(RequireGame(gameId));

    public SkinState Get(string gameId)
    {
        var theme = Theme(gameId);
        var directory = DirectoryFor(gameId);
        var image = FindImage(directory);
        return new SkinState(image is not null, ReadOverlay(directory) ?? theme.DefaultOverlayOpacity, VersionOf(image));
    }

    public SkinState ImportImage(string gameId, string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ConfineFileName(fileName);
        if (data.LongLength > MaxBytes)
            throw new SkinRejectedException("图片超过 20 MB。");
        var format = Detect(data, fileName);
        var directory = DirectoryFor(gameId);
        Directory.CreateDirectory(directory);
        foreach (var name in ImageNames)
        {
            var existing = Path.Combine(directory, name);
            if (File.Exists(existing))
                File.Delete(existing);
        }

        var destination = Path.Combine(directory, "background" + format.Extension);
        File.WriteAllBytes(destination, data);
        return Get(gameId);
    }

    public SkinState SetOverlay(string gameId, double value)
    {
        if (value < 0 || value > 0.8)
            throw new SkinRejectedException("遮罩透明度超出 0 到 0.8。");
        var directory = DirectoryFor(gameId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "skin.json"), JsonSerializer.Serialize(new SkinFile(value), JsonOptions));
        return Get(gameId);
    }

    public SkinState ClearImage(string gameId)
    {
        var directory = DirectoryFor(gameId);
        if (Directory.Exists(directory))
        {
            foreach (var name in ImageNames)
            {
                var existing = Path.Combine(directory, name);
                if (File.Exists(existing))
                    File.Delete(existing);
            }
        }

        return Get(gameId);
    }

    public SkinImage? TryOpenImage(string gameId)
    {
        try
        {
            var image = FindImage(DirectoryFor(gameId));
            if (image is null)
                return null;
            var format = Detect(File.ReadAllBytes(image), Path.GetFileName(image));
            return new SkinImage(File.ReadAllBytes(image), format.ContentType);
        }
        catch (Exception ex) when (ex is SkinRejectedException or KeyNotFoundException)
        {
            return null;
        }
    }

    private IGameAdapter RequireGame(string gameId)
    {
        try
        {
            return _registry.GetRequired(gameId);
        }
        catch (KeyNotFoundException)
        {
            throw new SkinRejectedException("未知游戏。");
        }
    }

    private string DirectoryFor(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || gameId.Contains("..", StringComparison.Ordinal)
            || gameId.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new SkinRejectedException("皮肤路径超出皮肤目录。");
        RequireGame(gameId);
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string directory;
        try
        {
            directory = Path.GetFullPath(Path.Combine(Root, gameId));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SkinRejectedException("皮肤路径超出皮肤目录。");
        }

        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new SkinRejectedException("皮肤路径超出皮肤目录。");
        return directory;
    }

    private static void ConfineFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(fileName)
            || fileName.IndexOfAny(['/', '\\']) >= 0)
            throw new SkinRejectedException("图片路径超出皮肤目录。");
    }

    private static string? FindImage(string directory)
    {
        if (!Directory.Exists(directory))
            return null;
        foreach (var name in ImageNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static double? ReadOverlay(string directory)
    {
        var path = Path.Combine(directory, "skin.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var file = JsonSerializer.Deserialize<SkinFile>(File.ReadAllText(path), JsonOptions);
            return file?.OverlayOpacity;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? VersionOf(string? path) =>
        path is null ? null : File.GetLastWriteTimeUtc(path).Ticks + "-" + new FileInfo(path).Length;

    private static ImageFormat Detect(byte[] data, string fileName)
    {
        var format = Magic(data) ?? throw new SkinRejectedException("不支持的图片格式。");
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && format.Extension != ".png"
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && format.Extension != ".jpg"
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) && format.Extension != ".jpg"
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) && format.Extension != ".webp")
            throw new SkinRejectedException("文件头与扩展名不符。");
        return format;
    }

    private static ImageFormat? Magic(byte[] data)
    {
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return new ImageFormat(".png", "image/png");
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return new ImageFormat(".jpg", "image/jpeg");
        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return new ImageFormat(".webp", "image/webp");
        return null;
    }

    private readonly record struct ImageFormat(string Extension, string ContentType);

    private sealed record SkinFile(double? OverlayOpacity);
}
