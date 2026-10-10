using System.Collections.Concurrent;
using System.Globalization;
using GameTool.Adapters.Cyberpunk2077.Archives;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Textures;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>
/// 从本机游戏取物品图标（T020）：记录 → UIIcon（tweak 源）→ inkatlas + xbm（归档）→ PNG。
/// 第一次请求才打开归档索引并加载 Oodle（就地从 bin\x64 加载，不复制）；游戏目录只读。
/// 内存里留最近解码的贴图（<see cref="IconExtractor"/>）和最近编码的 PNG；同一个键并发请求只解一次。
/// 任何缺失或读取错误都返回 null，界面显示占位图。
/// </summary>
public sealed class Cp2077ItemIcons : IItemIconProvider
{
    /// <summary>提取规则版本：解码或裁切逻辑变化时加一，让 WebView 的长缓存失效。</summary>
    public const int ExtractorRevision = 1;

    public const int PngCapacity = 512;

    private const string VersionArchive = "basegame_4_gamedata.archive";

    private readonly Func<ItemIconResolver?> _resolver;
    private readonly string? _gameDirectory;
    private readonly Func<GameArchiveSet>? _openArchives;
    private readonly Lazy<IconExtractor?> _extractor;
    private readonly Lazy<string?> _version;
    private readonly LruCache<string, byte[]?> _png = new(PngCapacity, StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> _pending = new(StringComparer.Ordinal);

    public Cp2077ItemIcons(Func<ItemIconResolver?> resolver, string? gameDirectory, Func<GameArchiveSet>? openArchives = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _gameDirectory = string.IsNullOrWhiteSpace(gameDirectory) ? null : gameDirectory;
        _openArchives = openArchives;
        _extractor = new Lazy<IconExtractor?>(CreateExtractor, LazyThreadSafetyMode.ExecutionAndPublication);
        _version = new Lazy<string?>(ComputeVersion, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>最近一次打开归档或提取失败的原因（诊断用）。</summary>
    public string? LastError { get; private set; }

    public string? IconVersion => _version.Value;

    public byte[]? TryGetIconPng(string recordName, IconGender gender)
    {
        if (string.IsNullOrWhiteSpace(recordName))
            return null;
        var key = (gender == IconGender.Male ? "m:" : "f:") + recordName;
        if (_png.TryGet(key, out var cached))
            return cached;
        var pending = _pending.GetOrAdd(key, _ => new Lazy<byte[]?>(() => Produce(recordName, gender), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var png = pending.Value;
            _png.Set(key, png);
            return png;
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, Lazy<byte[]?>>(key, pending));
        }
    }

    /// <summary>不编码，只取裁好的 RGBA（测试和诊断用）。</summary>
    public IconImage? TryGetIconImage(string recordName, IconGender gender)
    {
        var resolver = _resolver();
        var resolved = resolver?.Resolve(recordName, gender);
        if (resolved is null)
            return null;
        var extractor = _extractor.Value;
        return extractor?.Extract(resolved.Icon.AtlasPath, resolved.Icon.PartName);
    }

    private byte[]? Produce(string recordName, IconGender gender)
    {
        try
        {
            var image = TryGetIconImage(recordName, gender);
            return image is null ? null : PngEncoder.EncodeRgba(image.Rgba, image.Width, image.Height);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or EndOfStreamException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or ArgumentException)
        {
            LastError = recordName + ": " + ex.Message;
            return null;
        }
    }

    private IconExtractor? CreateExtractor()
    {
        try
        {
            if (_openArchives is not null)
                return new IconExtractor(_openArchives());
            if (_gameDirectory is null)
                return null;
            var oodle = OodleNativeDecompressor.Load(LocalizationCatalog.OodlePath(_gameDirectory));
            return new IconExtractor(GameArchiveSet.Open(_gameDirectory, oodle));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>"r{提取规则版本}-{basegame_4_gamedata 的长度和修改时间}"；没有游戏或名字表时为 null。</summary>
    private string? ComputeVersion()
    {
        if (_gameDirectory is null || _resolver() is null)
            return null;
        try
        {
            var archive = new FileInfo(Path.Combine(_gameDirectory, "archive", "pc", "content", VersionArchive));
            if (!archive.Exists)
                return null;
            var stamp = archive.Length ^ archive.LastWriteTimeUtc.Ticks;
            return "r" + ExtractorRevision.ToString(CultureInfo.InvariantCulture) + "-" + stamp.ToString("x", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
