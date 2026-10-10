namespace GameTool.Adapters.Cyberpunk2077.Textures;

/// <summary>
/// BC7 块解码（按 BPTC 公开规范编写）：16 字节 → 4×4 RGBA8。8 种模式；保留模式（首字节为 0）解成全透明黑色。
/// </summary>
public static class Bc7
{
    private readonly record struct Mode(int Subsets, int PartitionBits, int RotationBits, int IndexSelectionBits, int ColorBits, int AlphaBits, int EndpointPBits, int SharedPBits, int IndexBits, int SecondaryIndexBits);

    private static readonly Mode[] Modes =
    [
        new(3, 4, 0, 0, 4, 0, 1, 0, 3, 0),
        new(2, 6, 0, 0, 6, 0, 0, 1, 3, 0),
        new(3, 6, 0, 0, 5, 0, 0, 0, 2, 0),
        new(2, 6, 0, 0, 7, 0, 1, 0, 2, 0),
        new(1, 0, 2, 1, 5, 6, 0, 0, 2, 3),
        new(1, 0, 2, 0, 7, 8, 0, 0, 2, 2),
        new(1, 0, 0, 0, 7, 7, 1, 0, 4, 0),
        new(2, 6, 0, 0, 5, 5, 1, 0, 2, 0)
    ];

    private static readonly int[] Weights2 = [0, 21, 43, 64];
    private static readonly int[] Weights3 = [0, 9, 18, 27, 37, 46, 55, 64];
    private static readonly int[] Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    /// <summary>两子集分区：第 i 位是像素 i 所属的子集。</summary>
    public static readonly ushort[] Partitions2 =
    [
        0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
        0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
        0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A, 0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
        0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C, 0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22
    ];

    /// <summary>三子集分区：每个分区 16 个像素的子集号。</summary>
    public static readonly byte[][] Partitions3 =
    [
        [0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 1, 2, 2, 2, 2], [0, 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1],
        [0, 0, 0, 0, 2, 0, 0, 1, 2, 2, 1, 1, 2, 2, 1, 1], [0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 1, 0, 1, 1, 1],
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2], [0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 2, 2],
        [0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1], [0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1],
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2], [0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2],
        [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2], [0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2],
        [0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2], [0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2],
        [0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2, 1, 2, 2, 2], [0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0],
        [0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2], [0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0],
        [0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2], [0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1],
        [0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2, 0, 2, 2, 2], [0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 1, 2, 2, 2, 1],
        [0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2], [0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 1, 0, 2, 2, 1, 0],
        [0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0], [0, 0, 1, 2, 0, 0, 1, 2, 1, 1, 2, 2, 2, 2, 2, 2],
        [0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1, 0, 1, 1, 0], [0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1],
        [0, 0, 2, 2, 1, 1, 0, 2, 1, 1, 0, 2, 0, 0, 2, 2], [0, 1, 1, 0, 0, 1, 1, 0, 2, 0, 0, 2, 2, 2, 2, 2],
        [0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1], [0, 0, 0, 0, 2, 0, 0, 0, 2, 2, 1, 1, 2, 2, 2, 1],
        [0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 2, 2, 2], [0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 0, 0, 1, 1],
        [0, 0, 1, 1, 0, 0, 1, 2, 0, 0, 2, 2, 0, 2, 2, 2], [0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0],
        [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0], [0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0],
        [0, 1, 2, 0, 2, 0, 1, 2, 1, 2, 0, 1, 0, 1, 2, 0], [0, 0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 2, 0, 0, 1, 1],
        [0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 1, 1], [0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2],
        [0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1], [0, 0, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2, 1, 1, 2, 2],
        [0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1], [0, 2, 2, 0, 1, 2, 2, 1, 0, 2, 2, 0, 1, 2, 2, 1],
        [0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1], [0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1],
        [0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2], [0, 2, 2, 2, 0, 1, 1, 1, 0, 2, 2, 2, 0, 1, 1, 1],
        [0, 0, 0, 2, 1, 1, 1, 2, 0, 0, 0, 2, 1, 1, 1, 2], [0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2],
        [0, 2, 2, 2, 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2], [0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1, 2, 0, 0, 0, 2],
        [0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2], [0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2],
        [0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2, 2, 2, 2, 2], [0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2],
        [0, 0, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2],
        [0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1], [0, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2, 1, 2, 2, 2],
        [0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2], [0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0]
    ];

    /// <summary>两子集时子集 1 的锚点像素。</summary>
    public static readonly byte[] Anchor2 =
    [
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15
    ];

    /// <summary>三子集时子集 1 的锚点像素。</summary>
    public static readonly byte[] Anchor3A =
    [
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3,
        3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15,
        8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15,
        3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3
    ];

    /// <summary>三子集时子集 2 的锚点像素。</summary>
    public static readonly byte[] Anchor3B =
    [
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8,
        15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8,
        15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8
    ];

    /// <summary>解一个 16 字节块，按行写入 destination（每行 stride 字节，RGBA8）。</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> destination, int stride)
    {
        if (block.Length < 16)
            throw new ArgumentException("BC7 块必须是 16 字节。", nameof(block));
        var bits = new BitReader(block);
        var modeIndex = 0;
        while (modeIndex < 8 && bits.Read(1) == 0)
            modeIndex++;
        if (modeIndex == 8)
        {
            for (var y = 0; y < 4; y++)
                destination.Slice(y * stride, 16).Clear();
            return;
        }

        var mode = Modes[modeIndex];
        var partition = bits.Read(mode.PartitionBits);
        var rotation = bits.Read(mode.RotationBits);
        var indexSelection = bits.Read(mode.IndexSelectionBits);
        var endpoints = mode.Subsets * 2;
        Span<int> color = stackalloc int[6 * 4];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var e = 0; e < endpoints; e++)
                color[e * 4 + channel] = bits.Read(mode.ColorBits);
        }

        for (var e = 0; e < endpoints; e++)
            color[e * 4 + 3] = mode.AlphaBits > 0 ? bits.Read(mode.AlphaBits) : 255;

        Span<int> pbits = stackalloc int[6];
        if (mode.EndpointPBits > 0)
        {
            for (var e = 0; e < endpoints; e++)
                pbits[e] = bits.Read(1);
        }
        else if (mode.SharedPBits > 0)
        {
            for (var s = 0; s < mode.Subsets; s++)
            {
                var p = bits.Read(1);
                pbits[s * 2] = p;
                pbits[s * 2 + 1] = p;
            }
        }

        var hasP = mode.EndpointPBits > 0 || mode.SharedPBits > 0;
        for (var e = 0; e < endpoints; e++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var precision = channel < 3 ? mode.ColorBits : mode.AlphaBits;
                if (precision == 0)
                    continue;
                var value = color[e * 4 + channel];
                if (hasP)
                {
                    value = (value << 1) | pbits[e];
                    precision++;
                }

                color[e * 4 + channel] = Expand(value, precision);
            }
        }

        Span<int> subsetOf = stackalloc int[16];
        for (var i = 0; i < 16; i++)
            subsetOf[i] = mode.Subsets switch
            {
                2 => (Partitions2[partition] >> i) & 1,
                3 => Partitions3[partition][i],
                _ => 0
            };

        Span<int> primary = stackalloc int[16];
        Span<int> secondary = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            var anchor = IsAnchor(i, mode.Subsets, partition);
            primary[i] = bits.Read(anchor ? mode.IndexBits - 1 : mode.IndexBits);
        }

        if (mode.SecondaryIndexBits > 0)
        {
            for (var i = 0; i < 16; i++)
                secondary[i] = bits.Read(i == 0 ? mode.SecondaryIndexBits - 1 : mode.SecondaryIndexBits);
        }

        for (var i = 0; i < 16; i++)
        {
            var e0 = subsetOf[i] * 2 * 4;
            var e1 = e0 + 4;
            int r, g, b, a;
            if (mode.SecondaryIndexBits == 0)
            {
                var w = Weights(mode.IndexBits)[primary[i]];
                r = Interpolate(color[e0], color[e1], w);
                g = Interpolate(color[e0 + 1], color[e1 + 1], w);
                b = Interpolate(color[e0 + 2], color[e1 + 2], w);
                a = Interpolate(color[e0 + 3], color[e1 + 3], w);
            }
            else
            {
                // 模式 4/5：颜色和 alpha 各用一组索引；模式 4 的 indexSelection = 1 时两组对调。
                var colorIndex = indexSelection == 0 ? primary[i] : secondary[i];
                var alphaIndex = indexSelection == 0 ? secondary[i] : primary[i];
                var colorBits = indexSelection == 0 ? mode.IndexBits : mode.SecondaryIndexBits;
                var alphaBits = indexSelection == 0 ? mode.SecondaryIndexBits : mode.IndexBits;
                var wc = Weights(colorBits)[colorIndex];
                var wa = Weights(alphaBits)[alphaIndex];
                r = Interpolate(color[e0], color[e1], wc);
                g = Interpolate(color[e0 + 1], color[e1 + 1], wc);
                b = Interpolate(color[e0 + 2], color[e1 + 2], wc);
                a = Interpolate(color[e0 + 3], color[e1 + 3], wa);
            }

            switch (rotation)
            {
                case 1:
                    (r, a) = (a, r);
                    break;
                case 2:
                    (g, a) = (a, g);
                    break;
                case 3:
                    (b, a) = (a, b);
                    break;
            }

            var at = (i >> 2) * stride + (i & 3) * 4;
            destination[at] = (byte)r;
            destination[at + 1] = (byte)g;
            destination[at + 2] = (byte)b;
            destination[at + 3] = (byte)a;
        }
    }

    private static bool IsAnchor(int pixel, int subsets, int partition)
    {
        if (pixel == 0)
            return true;
        return subsets switch
        {
            2 => pixel == Anchor2[partition],
            3 => pixel == Anchor3A[partition] || pixel == Anchor3B[partition],
            _ => false
        };
    }

    private static int[] Weights(int bits) => bits switch
    {
        2 => Weights2,
        3 => Weights3,
        _ => Weights4
    };

    private static int Expand(int value, int precision)
    {
        value <<= 8 - precision;
        return value | (value >> precision);
    }

    private static int Interpolate(int e0, int e1, int weight) => ((64 - weight) * e0 + weight * e1 + 32) >> 6;

    private ref struct BitReader
    {
        private readonly ulong _low;
        private readonly ulong _high;
        private int _position;

        public BitReader(ReadOnlySpan<byte> block)
        {
            _low = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(block);
            _high = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
            _position = 0;
        }

        public int Read(int count)
        {
            if (count == 0)
                return 0;
            int value;
            if (_position >= 64)
                value = (int)((_high >> (_position - 64)) & ((1UL << count) - 1));
            else if (_position + count <= 64)
                value = (int)((_low >> _position) & ((1UL << count) - 1));
            else
            {
                var lowCount = 64 - _position;
                var lowPart = _low >> _position;
                var highPart = _high & ((1UL << (count - lowCount)) - 1);
                value = (int)(lowPart | (highPart << lowCount));
            }

            _position += count;
            return value;
        }
    }
}
