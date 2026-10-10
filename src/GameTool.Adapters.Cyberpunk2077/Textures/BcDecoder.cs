namespace GameTool.Adapters.Cyberpunk2077.Textures;

/// <summary>贴图的像素格式（只列图标可能用到的）。</summary>
public enum TextureFormat
{
    Rgba8,
    Bc1,
    Bc3,
    Bc4,
    Bc5,
    Bc7
}

/// <summary>
/// 块压缩贴图解码（BC1/3/4/5/7）和按区域裁切。纯函数，不碰文件。
/// </summary>
public static class BcDecoder
{
    public static bool IsBlockCompressed(TextureFormat format) => format != TextureFormat.Rgba8;

    public static int BlockSize(TextureFormat format) => format switch
    {
        TextureFormat.Bc1 or TextureFormat.Bc4 => 8,
        TextureFormat.Rgba8 => 4,
        _ => 16
    };

    /// <summary>mip 0 一行块（或像素）的字节数。</summary>
    public static int RowPitch(TextureFormat format, int width) =>
        IsBlockCompressed(format) ? ((width + 3) / 4) * BlockSize(format) : width * 4;

    /// <summary>
    /// 从 mip 0 的数据里解出矩形 [x, x+width) × [y, y+height)（按存储的行序），输出 RGBA8，每行 width*4 字节。
    /// 只解码覆盖到的 4×4 块。区域越界时抛 InvalidDataException。
    /// </summary>
    public static byte[] DecodeRegion(ReadOnlySpan<byte> data, TextureFormat format, int textureWidth, int textureHeight, int rowPitch, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > textureWidth || y + height > textureHeight)
            throw new InvalidDataException("裁切区域超出贴图范围。");
        var output = new byte[width * height * 4];
        if (!IsBlockCompressed(format))
        {
            for (var row = 0; row < height; row++)
            {
                var source = (long)(y + row) * rowPitch + x * 4;
                if (source + width * 4 > data.Length)
                    throw new InvalidDataException("贴图数据比声明的短。");
                data.Slice((int)source, width * 4).CopyTo(output.AsSpan(row * width * 4));
            }

            return output;
        }

        var blockSize = BlockSize(format);
        Span<byte> pixels = stackalloc byte[4 * 4 * 4];
        var firstBlockX = x / 4;
        var lastBlockX = (x + width - 1) / 4;
        var firstBlockY = y / 4;
        var lastBlockY = (y + height - 1) / 4;
        for (var by = firstBlockY; by <= lastBlockY; by++)
        {
            for (var bx = firstBlockX; bx <= lastBlockX; bx++)
            {
                var offset = (long)by * rowPitch + (long)bx * blockSize;
                if (offset + blockSize > data.Length)
                    throw new InvalidDataException("贴图数据比声明的短。");
                DecodeBlock(format, data.Slice((int)offset, blockSize), pixels, 16);
                for (var py = 0; py < 4; py++)
                {
                    var outY = by * 4 + py - y;
                    if (outY < 0 || outY >= height)
                        continue;
                    for (var px = 0; px < 4; px++)
                    {
                        var outX = bx * 4 + px - x;
                        if (outX < 0 || outX >= width)
                            continue;
                        pixels.Slice(py * 16 + px * 4, 4).CopyTo(output.AsSpan((outY * width + outX) * 4));
                    }
                }
            }
        }

        return output;
    }

    /// <summary>解一个块到 destination（4 行，每行 stride 字节，RGBA8）。</summary>
    public static void DecodeBlock(TextureFormat format, ReadOnlySpan<byte> block, Span<byte> destination, int stride)
    {
        switch (format)
        {
            case TextureFormat.Bc1:
                DecodeColor(block, destination, stride, allowTransparent: true);
                break;
            case TextureFormat.Bc3:
                DecodeColor(block[8..], destination, stride, allowTransparent: false);
                DecodeSingleChannel(block, destination, stride, 3);
                break;
            case TextureFormat.Bc4:
                DecodeSingleChannel(block, destination, stride, 0);
                for (var i = 0; i < 16; i++)
                {
                    var at = (i >> 2) * stride + (i & 3) * 4;
                    destination[at + 1] = destination[at];
                    destination[at + 2] = destination[at];
                    destination[at + 3] = 255;
                }

                break;
            case TextureFormat.Bc5:
                DecodeSingleChannel(block, destination, stride, 0);
                DecodeSingleChannel(block[8..], destination, stride, 1);
                for (var i = 0; i < 16; i++)
                {
                    var at = (i >> 2) * stride + (i & 3) * 4;
                    destination[at + 2] = 0;
                    destination[at + 3] = 255;
                }

                break;
            case TextureFormat.Bc7:
                Bc7.DecodeBlock(block, destination, stride);
                break;
            default:
                throw new InvalidDataException($"不是块压缩格式：{format}");
        }
    }

    /// <summary>BC1 颜色块（BC3 的颜色部分总是四色模式）。</summary>
    private static void DecodeColor(ReadOnlySpan<byte> block, Span<byte> destination, int stride, bool allowTransparent)
    {
        var c0 = block[0] | (block[1] << 8);
        var c1 = block[2] | (block[3] << 8);
        Span<int> palette = stackalloc int[16];
        Unpack565(c0, palette[..4]);
        Unpack565(c1, palette[4..8]);
        for (var channel = 0; channel < 3; channel++)
        {
            var a = palette[channel];
            var b = palette[4 + channel];
            if (c0 > c1 || !allowTransparent)
            {
                palette[8 + channel] = (2 * a + b) / 3;
                palette[12 + channel] = (a + 2 * b) / 3;
            }
            else
            {
                palette[8 + channel] = (a + b) / 2;
                palette[12 + channel] = 0;
            }
        }

        palette[3] = 255;
        palette[7] = 255;
        palette[11] = 255;
        palette[15] = c0 > c1 || !allowTransparent ? 255 : 0;
        var indices = block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24);
        for (var i = 0; i < 16; i++)
        {
            var index = (indices >> (i * 2)) & 3;
            var at = (i >> 2) * stride + (i & 3) * 4;
            destination[at] = (byte)palette[index * 4];
            destination[at + 1] = (byte)palette[index * 4 + 1];
            destination[at + 2] = (byte)palette[index * 4 + 2];
            destination[at + 3] = (byte)palette[index * 4 + 3];
        }
    }

    /// <summary>BC4 风格的单通道块（BC3 的 alpha、BC4、BC5 的两个通道）。</summary>
    private static void DecodeSingleChannel(ReadOnlySpan<byte> block, Span<byte> destination, int stride, int channel)
    {
        int a = block[0];
        int b = block[1];
        Span<int> values = stackalloc int[8];
        values[0] = a;
        values[1] = b;
        if (a > b)
        {
            for (var i = 1; i < 7; i++)
                values[i + 1] = ((7 - i) * a + i * b) / 7;
        }
        else
        {
            for (var i = 1; i < 5; i++)
                values[i + 1] = ((5 - i) * a + i * b) / 5;
            values[6] = 0;
            values[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
        {
            var index = (int)((bits >> (i * 3)) & 7);
            destination[(i >> 2) * stride + (i & 3) * 4 + channel] = (byte)values[index];
        }
    }

    private static void Unpack565(int color, Span<int> rgba)
    {
        var r = (color >> 11) & 31;
        var g = (color >> 5) & 63;
        var b = color & 31;
        rgba[0] = (r << 3) | (r >> 2);
        rgba[1] = (g << 2) | (g >> 4);
        rgba[2] = (b << 3) | (b >> 2);
        rgba[3] = 255;
    }
}
