using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace GameTool.Adapters.Cyberpunk2077.Container;

internal static class Lz4Chunks
{
    public static byte[] Encode(ReadOnlySpan<byte> plain)
    {
        if (plain.Length is < 1 or > Cp2077Format.MaxDecompressedChunkLength)
        {
            throw new InvalidDataException(
                $"块解压长度 {plain.Length} 超出安全范围 1..{Cp2077Format.MaxDecompressedChunkLength}。");
        }

        var bound = LZ4Codec.MaximumOutputSize(plain.Length);
        var target = new byte[bound];
        var written = LZ4Codec.Encode(plain, target);
        if (written <= 0)
            throw new InvalidDataException("LZ4 压缩失败。");

        var image = new byte[Cp2077Format.ChunkHeaderSize + written];
        BinaryPrimitives.WriteUInt32LittleEndian(image, Cp2077Format.ChunkMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), (uint)plain.Length);
        target.AsSpan(0, written).CopyTo(image.AsSpan(Cp2077Format.ChunkHeaderSize));
        return image;
    }

    public static byte[] Decode(ReadOnlySpan<byte> image, int decompressedLength)
    {
        if (image.Length < Cp2077Format.ChunkHeaderSize)
            throw new InvalidDataException("压缩块短于 8 字节块头。");
        if (decompressedLength is < 1 or > Cp2077Format.MaxDecompressedChunkLength)
        {
            throw new InvalidDataException(
                $"块解压长度 {decompressedLength} 超出安全范围 1..{Cp2077Format.MaxDecompressedChunkLength}。");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(image);
        if (magic != Cp2077Format.ChunkMagic)
            throw new InvalidDataException($"块魔数 0x{magic:X8} 不是 0x{Cp2077Format.ChunkMagic:X8}。");

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(4));
        if (declared != (uint)decompressedLength)
            throw new InvalidDataException("块内解压长度与分块表不一致。");

        var plain = new byte[decompressedLength];
        var written = LZ4Codec.Decode(image.Slice(Cp2077Format.ChunkHeaderSize), plain);
        if (written != decompressedLength)
            throw new InvalidDataException($"LZ4 解压得到 {written} 字节，分块表记录 {decompressedLength} 字节。");
        return plain;
    }
}
