using GameTool.Core;
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
                UiText.T($"块解压长度 {plain.Length} 超出安全范围 1..{Cp2077Format.MaxDecompressedChunkLength}。", $"塊解壓長度 {plain.Length} 超出安全範圍 1..{Cp2077Format.MaxDecompressedChunkLength}。", $"Decompressed chunk length {plain.Length} is outside the safe range 1..{Cp2077Format.MaxDecompressedChunkLength}."));
        }

        var bound = LZ4Codec.MaximumOutputSize(plain.Length);
        var target = new byte[bound];
        var written = LZ4Codec.Encode(plain, target);
        if (written <= 0)
            throw new InvalidDataException(UiText.T("LZ4 压缩失败。", "LZ4 壓縮失敗。", "LZ4 compression failed."));

        var image = new byte[Cp2077Format.ChunkHeaderSize + written];
        BinaryPrimitives.WriteUInt32LittleEndian(image, Cp2077Format.ChunkMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4), (uint)plain.Length);
        target.AsSpan(0, written).CopyTo(image.AsSpan(Cp2077Format.ChunkHeaderSize));
        return image;
    }

    public static byte[] Decode(ReadOnlySpan<byte> image, int decompressedLength)
    {
        Validate(image, decompressedLength);
        var plain = new byte[decompressedLength];
        DecodeValidated(image, plain);
        return plain;
    }

    /// <summary>
    /// 与 <see cref="Decode"/> 相同的校验和报错，但直接解压进 destination（长度就是分块表记录的解压长度），
    /// 读存档时各块直接写进连续载荷，不再每块单独分配再拷贝一次。
    /// </summary>
    public static void DecodeInto(ReadOnlySpan<byte> image, Span<byte> destination)
    {
        Validate(image, destination.Length);
        DecodeValidated(image, destination);
    }

    private static void Validate(ReadOnlySpan<byte> image, int decompressedLength)
    {
        if (image.Length < Cp2077Format.ChunkHeaderSize)
            throw new InvalidDataException(UiText.T("压缩块短于 8 字节块头。", "壓縮塊短於 8 位元組塊頭。", "A compressed chunk is shorter than the 8-byte chunk header."));
        if (decompressedLength is < 1 or > Cp2077Format.MaxDecompressedChunkLength)
        {
            throw new InvalidDataException(
                UiText.T($"块解压长度 {decompressedLength} 超出安全范围 1..{Cp2077Format.MaxDecompressedChunkLength}。", $"塊解壓長度 {decompressedLength} 超出安全範圍 1..{Cp2077Format.MaxDecompressedChunkLength}。", $"Decompressed chunk length {decompressedLength} is outside the safe range 1..{Cp2077Format.MaxDecompressedChunkLength}."));
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(image);
        if (magic != Cp2077Format.ChunkMagic)
            throw new InvalidDataException(UiText.T($"块魔数 0x{magic:X8} 不是 0x{Cp2077Format.ChunkMagic:X8}。", $"塊魔數 0x{magic:X8} 不是 0x{Cp2077Format.ChunkMagic:X8}。", $"Chunk magic 0x{magic:X8} is not 0x{Cp2077Format.ChunkMagic:X8}."));

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(4));
        if (declared != (uint)decompressedLength)
            throw new InvalidDataException(UiText.T("块内解压长度与分块表不一致。", "塊內解壓長度與分塊表不一致。", "The decompressed length inside the chunk does not match the chunk table."));
    }

    private static void DecodeValidated(ReadOnlySpan<byte> image, Span<byte> plain)
    {
        var written = LZ4Codec.Decode(image.Slice(Cp2077Format.ChunkHeaderSize), plain);
        if (written != plain.Length)
            throw new InvalidDataException(UiText.T($"LZ4 解压得到 {written} 字节，分块表记录 {plain.Length} 字节。", $"LZ4 解壓得到 {written} 位元組，分塊表記錄 {plain.Length} 位元組。", $"LZ4 decompression produced {written} bytes, but the chunk table says {plain.Length}."));
    }
}
