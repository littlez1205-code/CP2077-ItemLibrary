using System.Buffers.Binary;
using System.IO.Compression;

namespace GameTool.Adapters.Cyberpunk2077.Textures;

/// <summary>最小的 PNG 编码器：8 位 RGBA、不隔行，每行用 Sub 过滤，zlib 用 System.IO.Compression。</summary>
public static class PngEncoder
{
    public static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] EncodeRgba(ReadOnlySpan<byte> rgba, int width, int height, CompressionLevel level = CompressionLevel.Optimal)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("图片尺寸必须是正数。");
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("像素数据长度和尺寸不一致。", nameof(rgba));

        var stride = width * 4;
        var filtered = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * stride, stride);
            var output = filtered.AsSpan(y * (stride + 1), stride + 1);
            // Sub 过滤：每个字节减去左边同通道的字节。透明背景和平滑渐变下通常比 None 小。
            output[0] = 1;
            for (var i = 0; i < stride; i++)
                output[i + 1] = (byte)(row[i] - (i >= 4 ? row[i - 4] : 0));
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, level, leaveOpen: true))
                zlib.Write(filtered);
            compressed = buffer.ToArray();
        }

        using var png = new MemoryStream(compressed.Length + 64);
        png.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed);
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        stream.Write(tail);
    }

    /// <summary>PNG 用的 CRC-32（多项式 0xEDB88320）。</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data) => Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }
    }
}
