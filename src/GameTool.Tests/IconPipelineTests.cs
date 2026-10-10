using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GameTool.Adapters.Cyberpunk2077.Archives;
using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Resources;
using GameTool.Adapters.Cyberpunk2077.Textures;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>
/// T020 图标管线：BC7 块、PNG 编码、CR2W / inkatlas / xbm 解析、流式归档的多段文件，以及端到端的裁切和翻转。
/// 全部是手工合成的数据，不含任何游戏资源。
/// </summary>
public sealed class IconPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-icons-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---------- BC7 ----------

    [Fact]
    public void Bc7_Mode6_DecodesKnownPixels()
    {
        // 模式 6：端点 7 位 + 每端点 1 个 P 位，4 位索引（像素 0 是锚点，只有 3 位）。
        // e0 = (127,0,64,127)+P1 → (255,1,129,255)；e1 = (0,127,0,127)+P0 → (0,254,0,254)。
        var indices = new int[16];
        indices[5] = 8;
        indices[15] = 15;
        var block = Mode6Block([127, 0, 64, 127], [0, 127, 0, 127], 1, 0, indices);
        var rgba = new byte[64];
        Bc7.DecodeBlock(block, rgba, 16);
        Assert.Equal(new byte[] { 255, 1, 129, 255 }, rgba[0..4]);
        Assert.Equal(new byte[] { 0, 254, 0, 254 }, rgba[60..64]);
        // 权重 34：((64-34)*e0 + 34*e1 + 32) >> 6
        Assert.Equal(new byte[] { 120, 135, 60, 254 }, rgba[20..24]);
    }

    [Fact]
    public void Bc7_ReservedMode_IsTransparentBlack()
    {
        var rgba = Enumerable.Repeat((byte)7, 64).ToArray();
        Bc7.DecodeBlock(new byte[16], rgba, 16);
        Assert.All(rgba, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Bc7_Mode4_RotationAndIndexSelection()
    {
        // 模式 4：颜色 5 位、alpha 6 位，旋转 1 = 交换 R 和 A；索引选择 0：颜色用 2 位索引，alpha 用 3 位索引。
        var bits = new BitWriter();
        bits.Write(0b10000, 5);
        bits.Write(1, 2);
        bits.Write(0, 1);
        foreach (var (a, b) in new[] { (31, 31), (0, 0), (0, 0) })
        {
            bits.Write(a, 5);
            bits.Write(b, 5);
        }

        bits.Write(0, 6);
        bits.Write(0, 6);
        for (var i = 0; i < 16; i++)
            bits.Write(0, i == 0 ? 1 : 2);
        for (var i = 0; i < 16; i++)
            bits.Write(0, i == 0 ? 2 : 3);
        var rgba = new byte[64];
        Bc7.DecodeBlock(bits.ToArray(), rgba, 16);
        // 解出来是 R=255、A=0，旋转后 R=0、A=255。
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, rgba[0..4]);
    }

    [Fact]
    public void Bc7_Tables_AreConsistent()
    {
        Assert.Equal(64, Bc7.Partitions2.Length);
        Assert.Equal(64, Bc7.Partitions3.Length);
        for (var p = 0; p < 64; p++)
        {
            Assert.Equal(0, Bc7.Partitions2[p] & 1);
            Assert.Equal(1, (Bc7.Partitions2[p] >> Bc7.Anchor2[p]) & 1);
            var three = Bc7.Partitions3[p];
            Assert.Equal(16, three.Length);
            Assert.Equal(0, three[0]);
            Assert.Equal(1, three[Bc7.Anchor3A[p]]);
            Assert.Equal(2, three[Bc7.Anchor3B[p]]);
            // 锚点是该子集里序号最小的像素以外的固定位置，但必须属于该子集，且每个子集都有像素。
            Assert.Contains((byte)1, three);
            Assert.Contains((byte)2, three);
        }
    }

    [Fact]
    public void Bc1_DecodesEndpoints()
    {
        // c0 = 纯红 0xF800，c1 = 纯蓝 0x001F，四色模式，索引全 1 → c1。
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0x55, 0x55, 0x55, 0x55];
        var rgba = new byte[64];
        BcDecoder.DecodeBlock(TextureFormat.Bc1, block, rgba, 16);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[0..4]);
    }

    // ---------- PNG ----------

    [Fact]
    public void Png_RoundTrip_StructureCrcAndPixels()
    {
        const int width = 3;
        const int height = 2;
        byte[] pixels =
        [
            255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0,
            10, 20, 30, 40, 50, 60, 70, 80, 250, 251, 252, 253
        ];
        var png = PngEncoder.EncodeRgba(pixels, width, height);
        Assert.Equal(PngEncoder.Signature, png[..8]);
        var chunks = ReadChunks(png);
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(chunk => chunk.Type));
        var ihdr = chunks[0].Data;
        Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(ihdr));
        Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4)));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, ihdr[8..13]);
        // IEND 的 CRC 是 PNG 规范里的固定值。
        Assert.Equal(0xAE426082u, chunks[2].Crc);
        Assert.Equal(PngEncoder.Crc32.Compute(Encoding.ASCII.GetBytes("IEND")), chunks[2].Crc);
        Assert.Equal(pixels, DecodePng(png, width, height));
    }

    // ---------- CR2W / inkatlas / xbm ----------

    [Fact]
    public void InkAtlas_ParsesSlotsAndParts()
    {
        var atlas = InkAtlas.Parse(Cr2wFile.Parse(BuildAtlas(("item_a", 0.25f, 0, 0.75f, 0.5f), ("item_b", 0.5f, 0, 1, 1))));
        Assert.Equal(2, atlas.Slots.Count);
        Assert.Equal(TexturePath, atlas.Slots[0].TexturePath);
        Assert.Null(atlas.Slots[1].TexturePath);
        var part = atlas.Slots[0].Parts["item_a"];
        Assert.Equal(0.25, part.Left, 5);
        Assert.Equal(0, part.Top, 5);
        Assert.Equal(0.75, part.Right, 5);
        Assert.Equal(0.5, part.Bottom, 5);
    }

    [Fact]
    public void Xbm_ParsesLayout()
    {
        var file = Cr2wFile.Parse(BuildXbm(8, 8));
        var texture = XbmTexture.Parse(file);
        Assert.Equal(8, texture.Width);
        Assert.Equal(8, texture.Height);
        Assert.Equal(TextureFormat.Bc7, texture.Format);
        Assert.Equal(32, texture.RowPitch);
        Assert.Equal(64, texture.MipSize);
        Assert.Equal(1, texture.BufferIndex);
        Assert.Equal("rendRenderTextureBlobPC", file.ExportClass(1));
    }

    [Fact]
    public void StoredRect_MapsUvFromTopLeft_ToBottomUpRows()
    {
        var rect = IconExtractor.StoredRect(new AtlasPart("p", 0.25, 0.125, 0.75, 0.5), 2048, 2048);
        Assert.Equal((512, 1024, 1024, 768), rect);
    }

    [Fact]
    public void Archive_MultiSegmentFile_AndEndToEndIcon()
    {
        // 8×8 BC7 贴图 = 2×2 块，四个块各是一种纯色（模式 6 的 P 位为 1，0 解成 1）；存储行自下而上。
        var red = SolidBlock(255, 0, 0);
        var green = SolidBlock(0, 255, 0);
        var blue = SolidBlock(0, 0, 255);
        var white = SolidBlock(255, 255, 255);
        var pixels = red.Concat(green).Concat(blue).Concat(white).ToArray();
        WriteArchive(
            (AtlasPath, [BuildAtlas(("icon_right", 0.5f, 0, 1, 1))]),
            (TexturePath, [BuildXbm(8, 8), pixels]));

        using var set = GameArchiveSet.Open(_root, new NoOodle());
        Assert.Equal(2, set.Find(TexturePath)!.SegmentCount);
        Assert.Equal(pixels, set.Find(TexturePath.Replace('/', '\\').ToUpperInvariant())!.ReadSegment(1));
        Assert.Null(set.Find("base/missing.xbm"));

        var index = TweakSourceIndex.Parse([("a.tweak", $$"""
            package Items
            Thing
            {
            	iconPath = "icon_right";
            }

            package UIIcon
            icon_right
            {
            	atlasResourcePath = "{{AtlasPath}}";
            	atlasPartName = "icon_right";
            }
            """)]);
        var resolver = ItemIconResolver.Build(index);
        var icons = new Cp2077ItemIcons(() => resolver, null, () => GameArchiveSet.Open(_root, new NoOodle()));
        var png = icons.TryGetIconPng("Items.Thing", IconGender.Female);
        Assert.NotNull(png);
        Assert.Same(png, icons.TryGetIconPng("Items.Thing", IconGender.Female));
        Assert.Null(icons.TryGetIconPng("Items.Missing", IconGender.Female));
        var rgba = DecodePng(png, 4, 8);
        // 右半边：存储的第 0 块行是 green（x 4..7），第 1 块行是 white；翻转后 white 在上、green 在下。
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, rgba[0..4]);
        Assert.Equal(new byte[] { 1, 255, 1, 255 }, rgba[^4..]);
    }

    // ---------- helpers ----------

    private const string AtlasPath = "base/gameplay/gui/common/icons/items/test_icons.inkatlas";
    private const string TexturePath = "base/gameplay/gui/common/icons/items/test_icons.xbm";

    private sealed class NoOodle : IOodleDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> compressed, int uncompressedSize) =>
            throw new InvalidOperationException("合成归档全部是未压缩段。");
    }

    private static byte[] SolidBlock(int r, int g, int b) =>
        Mode6Block([r >> 1, g >> 1, b >> 1, 127], [r >> 1, g >> 1, b >> 1, 127], 1, 1, new int[16]);

    private static byte[] Mode6Block(int[] e0, int[] e1, int p0, int p1, int[] indices)
    {
        var bits = new BitWriter();
        bits.Write(0b1000000, 7);
        for (var channel = 0; channel < 4; channel++)
        {
            bits.Write(e0[channel], 7);
            bits.Write(e1[channel], 7);
        }

        bits.Write(p0, 1);
        bits.Write(p1, 1);
        for (var i = 0; i < 16; i++)
            bits.Write(indices[i], i == 0 ? 3 : 4);
        return bits.ToArray();
    }

    private sealed class BitWriter
    {
        private readonly byte[] _bytes = new byte[16];
        private int _position;

        public void Write(int value, int count)
        {
            for (var i = 0; i < count; i++, _position++)
            {
                if (((value >> i) & 1) != 0)
                    _bytes[_position >> 3] |= (byte)(1 << (_position & 7));
            }
        }

        public byte[] ToArray()
        {
            Assert.Equal(128, _position);
            return _bytes;
        }
    }

    private static List<(string Type, byte[] Data, uint Crc)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png[(at + 8)..(at + 8 + length)];
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(PngEncoder.Crc32.Compute(png.AsSpan(at + 4, 4 + length)), crc);
            chunks.Add((type, data, crc));
            at += 12 + length;
        }

        return chunks;
    }

    /// <summary>只支持本编码器的输出：RGBA8、每行 None 或 Sub 过滤。</summary>
    private static byte[] DecodePng(byte[] png, int width, int height)
    {
        var idat = ReadChunks(png).Single(chunk => chunk.Type == "IDAT").Data;
        using var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var filtered = raw.ToArray();
        var stride = width * 4;
        Assert.Equal((stride + 1) * height, filtered.Length);
        var output = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (stride + 1)];
            Assert.True(filter is 0 or 1);
            for (var i = 0; i < stride; i++)
            {
                var value = filtered[y * (stride + 1) + 1 + i];
                if (filter == 1 && i >= 4)
                    value = (byte)(value + output[y * stride + i - 4]);
                output[y * stride + i] = value;
            }
        }

        return output;
    }

    private void WriteArchive(params (string Path, byte[][] Segments)[] files)
    {
        var folder = Path.Combine(_root, "archive", "pc", "content");
        Directory.CreateDirectory(folder);
        using var data = new MemoryStream();
        data.Write(new byte[40]);
        var segments = new List<(long Offset, int Length)>();
        var entries = new List<(ulong Hash, int Start, int End)>();
        foreach (var (path, parts) in files)
        {
            var start = segments.Count;
            foreach (var part in parts)
            {
                segments.Add((data.Position, part.Length));
                data.Write(part);
            }

            entries.Add((GameArchiveSet.HashDepotPath(path), start, segments.Count));
        }

        var index = new MemoryStream();
        var writer = new BinaryWriter(index);
        writer.Write(8u);
        writer.Write(0u);
        writer.Write(0UL);
        writer.Write((uint)entries.Count);
        writer.Write((uint)segments.Count);
        writer.Write(0u);
        foreach (var (hash, start, end) in entries)
        {
            writer.Write(hash);
            writer.Write(0L);
            writer.Write(0u);
            writer.Write((uint)start);
            writer.Write((uint)end);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(new byte[20]);
        }

        foreach (var (offset, length) in segments)
        {
            writer.Write(offset);
            writer.Write((uint)length);
            writer.Write((uint)length);
        }

        writer.Flush();
        var indexOffset = data.Position;
        data.Write(index.ToArray());
        var bytes = data.ToArray();
        "RDAR"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 12);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), (ulong)indexOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)index.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), (ulong)bytes.Length);
        File.WriteAllBytes(Path.Combine(folder, "basegame_test.archive"), bytes);
        File.WriteAllBytes(Path.Combine(folder, "lang_skipped.archive"), [1, 2, 3]);
    }

    private static byte[] BuildAtlas(params (string Name, float Left, float Top, float Right, float Bottom)[] parts)
    {
        var cr2w = new Cr2wBuilder();
        cr2w.Import(TexturePath);
        var slots = new List<byte[]>
        {
            cr2w.Struct(
                cr2w.Property("texture", "raRef:CBitmapTexture", U16(1)),
                cr2w.Property("parts", "array:inkTextureAtlasMapper", cr2w.Array(parts.Select(part => cr2w.Struct(
                    cr2w.Property("partName", "CName", cr2w.Name(part.Name)),
                    cr2w.Property("clippingRectInUVCoords", "RectF", cr2w.Struct(
                        cr2w.Property("Left", "Float", F32(part.Left)),
                        cr2w.Property("Top", "Float", F32(part.Top)),
                        cr2w.Property("Right", "Float", F32(part.Right)),
                        cr2w.Property("Bottom", "Float", F32(part.Bottom)))))).ToArray()))),
            cr2w.Struct()
        };
        cr2w.Export("inkTextureAtlas", cr2w.Struct(
            cr2w.Property("cookingPlatform", "ECookingPlatform", cr2w.Name("PLATFORM_PC")),
            cr2w.Property("slots", "[3]inkTextureSlot", cr2w.Array(slots.ToArray()))));
        return cr2w.Build();
    }

    private static byte[] BuildXbm(int width, int height)
    {
        var cr2w = new Cr2wBuilder();
        var rowPitch = (width / 4) * 16;
        cr2w.Export("CBitmapTexture", cr2w.Struct(
            cr2w.Property("width", "Uint32", U32(width)),
            cr2w.Property("height", "Uint32", U32(height)),
            cr2w.Property("setup", "STextureGroupSetup", cr2w.Struct(
                cr2w.Property("compression", "ETextureCompression", cr2w.Name("TCM_QualityColor")),
                cr2w.Property("isGamma", "Bool", [1]))),
            cr2w.Property("renderTextureResource", "rendRenderTextureResource", cr2w.Struct(
                cr2w.Property("renderResourceBlobPC", "handle:IRenderResourceBlob", U32(2))))));
        cr2w.Export("rendRenderTextureBlobPC", cr2w.Struct(
            cr2w.Property("header", "rendRenderTextureBlobHeader", cr2w.Struct(
                cr2w.Property("version", "Uint32", U32(2)),
                cr2w.Property("sizeInfo", "rendRenderTextureBlobSizeInfo", cr2w.Struct(
                    cr2w.Property("width", "Uint16", U16(width)),
                    cr2w.Property("height", "Uint16", U16(height)))),
                cr2w.Property("mipMapInfo", "array:rendRenderTextureBlobMipMapInfo", cr2w.Array(cr2w.Struct(
                    cr2w.Property("layout", "rendRenderTextureBlobMemoryLayout", cr2w.Struct(
                        cr2w.Property("rowPitch", "Uint32", U32(rowPitch)))),
                    cr2w.Property("placement", "rendRenderTextureBlobPlacement", cr2w.Struct(
                        cr2w.Property("size", "Uint32", U32(rowPitch * height / 4))))))))),
            cr2w.Property("textureData", "serializationDeferredDataBuffer", U16(1))));
        cr2w.Buffer(rowPitch * height / 4);
        return cr2w.Build();
    }

    private static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] U32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        return bytes;
    }

    private static byte[] F32(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>按 CR2W 布局拼一个最小文件：字符串池、名字表、导入表、导出表、缓冲表，再接导出数据。</summary>
    private sealed class Cr2wBuilder
    {
        private readonly List<string> _names = [""];
        private readonly List<string> _imports = [];
        private readonly List<(string Class, byte[] Data)> _exports = [];
        private readonly List<int> _buffers = [];

        public byte[] Name(string name)
        {
            var index = _names.IndexOf(name);
            if (index < 0)
            {
                index = _names.Count;
                _names.Add(name);
            }

            return U16(index);
        }

        public void Import(string path) => _imports.Add(path);

        public void Export(string className, byte[] data)
        {
            Name(className);
            _exports.Add((className, data));
        }

        public void Buffer(int size) => _buffers.Add(size);

        public byte[] Property(string name, string type, byte[] value)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Name(name));
            bytes.AddRange(Name(type));
            bytes.AddRange(U32(value.Length + 4));
            bytes.AddRange(value);
            return bytes.ToArray();
        }

        public byte[] Struct(params byte[][] properties) =>
            [0, .. properties.SelectMany(property => property), 0, 0];

        public byte[] Array(params byte[][] items) =>
            [.. U32(items.Length), .. items.SelectMany(item => item)];

        public byte[] Build()
        {
            var pool = new MemoryStream();
            var nameOffsets = new List<int>();
            foreach (var name in _names)
            {
                nameOffsets.Add((int)pool.Position);
                pool.Write(Encoding.UTF8.GetBytes(name));
                pool.WriteByte(0);
            }

            var importOffsets = new List<int>();
            foreach (var path in _imports)
            {
                importOffsets.Add((int)pool.Position);
                pool.Write(Encoding.UTF8.GetBytes(path));
                pool.WriteByte(0);
            }

            const int tablesStart = 40 + 10 * 12;
            var poolOffset = tablesStart;
            var namesOffset = poolOffset + (int)pool.Length;
            var importsOffset = namesOffset + _names.Count * 8;
            var exportsOffset = importsOffset + _imports.Count * 8;
            var buffersOffset = exportsOffset + _exports.Count * 24;
            var dataOffset = buffersOffset + _buffers.Count * 24;

            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write("CR2W"u8.ToArray());
            writer.Write(195u);
            writer.Write(0u);
            writer.Write(0UL);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            (int Offset, int Count)[] tables =
            [
                (poolOffset, (int)pool.Length), (namesOffset, _names.Count), (importsOffset, _imports.Count), (0, 0),
                (exportsOffset, _exports.Count), (buffersOffset, _buffers.Count), (0, 0), (0, 0), (0, 0), (0, 0)
            ];
            foreach (var (offset, count) in tables)
            {
                writer.Write((uint)offset);
                writer.Write((uint)count);
                writer.Write(0u);
            }

            writer.Write(pool.ToArray());
            for (var i = 0; i < _names.Count; i++)
            {
                writer.Write((uint)nameOffsets[i]);
                writer.Write(0u);
            }

            foreach (var offset in importOffsets)
            {
                writer.Write((uint)offset);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
            }

            var cursor = dataOffset;
            foreach (var (className, data) in _exports)
            {
                writer.Write((ushort)_names.IndexOf(className));
                writer.Write((ushort)0);
                writer.Write(0u);
                writer.Write((uint)data.Length);
                writer.Write((uint)cursor);
                writer.Write(0u);
                writer.Write(0u);
                cursor += data.Length;
            }

            for (var i = 0; i < _buffers.Count; i++)
            {
                writer.Write(0u);
                writer.Write((uint)(i + 1));
                writer.Write(0u);
                writer.Write((uint)_buffers[i]);
                writer.Write((uint)_buffers[i]);
                writer.Write(0u);
            }

            foreach (var (_, data) in _exports)
                writer.Write(data);
            writer.Flush();
            return output.ToArray();
        }
    }
}
