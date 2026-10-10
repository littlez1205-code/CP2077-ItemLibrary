using GameTool.Adapters.Cyberpunk2077.Archives;
using GameTool.Adapters.Cyberpunk2077.Resources;
using GameTool.Adapters.Cyberpunk2077.Textures;

namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>裁出的图标：RGBA8，按显示方向（第 0 行在上）。</summary>
public sealed record IconImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// 图集 + 部件名 → RGBA 图块。先用 slots[0]（全分辨率），那一槽缺贴图或缺部件时退到下一槽。
/// xbm 的行是自下而上存的：裁存储行 [(1-Bottom)·H, (1-Top)·H)，解码后整块上下翻转（只在这里翻）。
/// 解码过的贴图放在小 LRU 里（默认 8 张，2048² BC7 一张 4 MB）。
/// </summary>
public sealed class IconExtractor
{
    public const int DefaultTextureCapacity = 8;

    private readonly GameArchiveSet _archives;
    private readonly LruCache<string, InkAtlas?> _atlases = new(64, StringComparer.OrdinalIgnoreCase);
    private readonly LruCache<string, LoadedTexture?> _textures;
    private readonly Lock _loadGate = new();

    public IconExtractor(GameArchiveSet archives, int textureCapacity = DefaultTextureCapacity)
    {
        ArgumentNullException.ThrowIfNull(archives);
        _archives = archives;
        _textures = new LruCache<string, LoadedTexture?>(textureCapacity, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>图集或部件缺失、贴图格式不支持时返回 null。</summary>
    public IconImage? Extract(string atlasPath, string partName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        var atlas = LoadAtlas(atlasPath);
        if (atlas is null)
            return null;
        foreach (var slot in atlas.Slots)
        {
            if (slot.TexturePath is null || !slot.Parts.TryGetValue(partName, out var part))
                continue;
            var texture = LoadTexture(slot.TexturePath);
            if (texture is null)
                continue;
            var image = Crop(texture, part);
            if (image is not null)
                return image;
        }

        return null;
    }

    /// <summary>UV 矩形 → 存储行列上的像素矩形（宽高为正时才有效）。</summary>
    public static (int X, int Y, int Width, int Height) StoredRect(AtlasPart part, int textureWidth, int textureHeight)
    {
        ArgumentNullException.ThrowIfNull(part);
        var x0 = Clamp((int)Math.Round(part.Left * textureWidth), textureWidth);
        var x1 = Clamp((int)Math.Round(part.Right * textureWidth), textureWidth);
        var y0 = Clamp((int)Math.Round((1 - part.Bottom) * textureHeight), textureHeight);
        var y1 = Clamp((int)Math.Round((1 - part.Top) * textureHeight), textureHeight);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>上下翻转一块 RGBA8。</summary>
    public static void FlipRows(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var stride = width * 4;
        var temp = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            var a = rgba.AsSpan(top * stride, stride);
            var b = rgba.AsSpan(bottom * stride, stride);
            a.CopyTo(temp);
            b.CopyTo(a);
            temp.CopyTo(b);
        }
    }

    private static IconImage? Crop(LoadedTexture texture, AtlasPart part)
    {
        var info = texture.Info;
        var (x, y, width, height) = StoredRect(part, info.Width, info.Height);
        if (width <= 0 || height <= 0)
            return null;
        var rgba = BcDecoder.DecodeRegion(texture.Mip0, info.Format, info.Width, info.Height, info.RowPitch, x, y, width, height);
        FlipRows(rgba, width, height);
        return new IconImage(width, height, rgba);
    }

    private InkAtlas? LoadAtlas(string path)
    {
        if (_atlases.TryGet(path, out var cached))
            return cached;
        InkAtlas? atlas = null;
        var file = _archives.Find(path);
        if (file is not null)
            atlas = InkAtlas.Parse(Cr2wFile.Parse(file.ReadBody()));
        _atlases.Set(path, atlas);
        return atlas;
    }

    private LoadedTexture? LoadTexture(string path)
    {
        if (_textures.TryGet(path, out var cached))
            return cached;
        lock (_loadGate)
        {
            if (_textures.TryGet(path, out cached))
                return cached;
            LoadedTexture? texture = null;
            var file = _archives.Find(path);
            if (file is not null)
            {
                var info = XbmTexture.Parse(Cr2wFile.Parse(file.ReadBody()));
                var data = file.ReadSegment(info.BufferIndex)
                    ?? throw new InvalidDataException($"贴图缺少像素段 {info.BufferIndex}：{path}");
                if ((long)info.MipOffset + info.MipSize > data.Length)
                    throw new InvalidDataException($"贴图像素段比 mip 0 短：{path}");
                texture = new LoadedTexture(info, info.MipOffset == 0 && info.MipSize == data.Length ? data : data.AsSpan(info.MipOffset, info.MipSize).ToArray());
            }

            _textures.Set(path, texture);
            return texture;
        }
    }

    private static int Clamp(int value, int max) => Math.Clamp(value, 0, max);

    private sealed record LoadedTexture(XbmTexture Info, byte[] Mip0);
}
