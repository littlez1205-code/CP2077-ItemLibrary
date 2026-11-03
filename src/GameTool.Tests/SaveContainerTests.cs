using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Core;

namespace GameTool.Tests;

public class SaveContainerTests
{
    [Fact]
    public void RoundTrip_CleanWrite_IsByteIdentical()
    {
        var container = Sample();
        var bytes = container.ToArray();
        var loaded = SaveContainer.Read(bytes);

        Assert.Equal(bytes, loaded.ToArray());
        Assert.Equal(Cp2077Format.SupportedSaveVersion, loaded.SaveVersion);
        Assert.Equal(Cp2077Format.SupportedGameVersion, loaded.GameVersion);
        Assert.Equal(container.CopyNodeTable(), loaded.CopyNodeTable());
        Assert.Equal(container.GetDecompressed(0), loaded.GetDecompressed(0));
        Assert.Equal(container.GetDecompressed(2), loaded.GetDecompressed(2));
    }

    [Fact]
    public void RoundTrip_Version250_IsSupportedAndByteIdentical()
    {
        var container = SaveContainer.Create(
            saveVersion: Cp2077Format.SaveVersion250,
            gameVersion: Cp2077Format.GameVersion250,
            timestamp: 0x1122_3344_5566_7788,
            archiveVersion: 11,
            decompressedChunks: [Pattern(64, 1), Pattern(64, 2), Pattern(64, 3)],
            nodeTable: [.. Cp2077Format.Node.ToArray(), 1, 2, 3, 4]);
        var bytes = container.ToArray();
        var loaded = SaveContainer.Read(bytes);

        Assert.Equal(bytes, loaded.ToArray());
        Assert.Equal(Cp2077Format.SaveVersion250, loaded.SaveVersion);
        Assert.Equal(Cp2077Format.GameVersion250, loaded.GameVersion);
    }

    [Fact]
    public void Rejects_VersionOutsideWhitelist()
    {
        var bytes = SaveContainer.Create(
            saveVersion: Cp2077Format.SupportedSaveVersion - 1,
            gameVersion: Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [Pattern(32, 1), Pattern(32, 2)],
            nodeTable: [9, 9]).ToArray();

        var ex = Assert.Throws<UnsupportedSaveException>(() => SaveContainer.Read(bytes));
        Assert.Equal(Cp2077Format.SupportedSaveVersion - 1, ex.SaveVersion);
    }

    [Fact]
    public void Rejects_BadMagic()
    {
        var bytes = Sample().ToArray();
        bytes[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SaveContainer.Read(bytes));

        var chunk = Sample().ToArray();
        chunk[Cp2077Format.FirstChunkOffset] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SaveContainer.Read(chunk));
    }

    [Fact]
    public void DirtyRecompress_DecompressedBytesMatch()
    {
        var container = Sample();
        var expected = new[]
        {
            container.GetDecompressed(0),
            container.GetDecompressed(1),
            container.GetDecompressed(2)
        };
        var nodeTable = container.CopyNodeTable();

        container.MarkAllDirty();
        var loaded = SaveContainer.Read(container.ToArray());

        Assert.Equal(expected[0], loaded.GetDecompressed(0));
        Assert.Equal(expected[1], loaded.GetDecompressed(1));
        Assert.Equal(expected[2], loaded.GetDecompressed(2));
        Assert.Equal(nodeTable, loaded.CopyNodeTable());
    }

    [Fact]
    public void UnusedSlotResidue_SurvivesIdentityAndDirtyRecompress()
    {
        var bytes = Sample().ToArray();
        var slot = Cp2077Format.ChunkTableOffset + (3 * Cp2077Format.ChunkEntrySize);
        bytes[slot] = 0xAB;
        bytes[slot + 5] = 0xCD;

        var loaded = SaveContainer.Read(bytes);
        Assert.Equal(bytes, loaded.ToArray());

        loaded.MarkAllDirty();
        var rewritten = loaded.ToArray();
        Assert.Equal(0xAB, rewritten[slot]);
        Assert.Equal(0xCD, rewritten[slot + 5]);
    }

    [Fact]
    public void ReplacePayload_KeepsResidueAndSplitsOnChunkBoundary()
    {
        var original = new byte[Cp2077Format.DecompressedChunkSize];
        original[0] = 7;
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [original],
            nodeTable: [1, 2, 3, 4, 5]);
        var bytes = container.ToArray();
        var residue = Cp2077Format.ChunkTableOffset + (2 * Cp2077Format.ChunkEntrySize);
        bytes[residue] = 0xAB;
        bytes[residue + 5] = 0xCD;
        var loaded = SaveContainer.Read(bytes);

        var grown = new byte[Cp2077Format.DecompressedChunkSize + 10];
        original.CopyTo(grown, 0);
        grown[^1] = 9;
        loaded.ReplacePayload(grown);
        var rewritten = loaded.ToArray();
        var again = SaveContainer.Read(rewritten);
        Assert.Equal(2, again.ChunkCount);
        Assert.Equal(grown, again.Payload.ToArray());
        Assert.Equal(0xAB, rewritten[residue]);
        Assert.Equal(0xCD, rewritten[residue + 5]);

        var sameCount = new byte[32];
        again.ReplacePayload(sameCount);
        var stable = again.ToArray();
        Assert.Equal(1, SaveContainer.Read(stable).ChunkCount);
        Assert.Equal(0xAB, stable[residue]);
    }

    [Fact]
    public void Read_FromPathAndMemory_MatchesTheFile_AndKeepsCleanChunksVerbatim()
    {
        var bytes = Sample().ToArray();
        var path = Path.Combine(Path.GetTempPath(), "GameTool-container-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            File.WriteAllBytes(path, bytes);
            var fromPath = SaveContainer.Read(path);
            var fromMemory = SaveContainer.Read((ReadOnlyMemory<byte>)bytes);
            Assert.Equal(bytes, fromPath.ToArray());
            Assert.Equal(fromPath.Payload.ToArray(), fromMemory.Payload.ToArray());

            // 容器直接引用读入的文件字节：之后改动调用方的数组不能影响已经解析好的容器。
            var original = fromMemory.ToArray();
            Array.Fill(bytes, (byte)0xEE);
            Assert.Equal(original, fromMemory.ToArray());

            // 改一个字节只重压缩那一块，其他块仍原样写回，重新读取后载荷一致。
            fromPath.WritePayload(130, [0xAB]);
            Assert.False(fromPath.IsChunkDirty(0));
            Assert.True(fromPath.IsChunkDirty(1));
            var edited = SaveContainer.Read((ReadOnlyMemory<byte>)fromPath.ToArray());
            Assert.Equal(fromPath.Payload.ToArray(), edited.Payload.ToArray());
            Assert.Equal(0xAB, edited.Payload[130]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ChunkTableWithOutOfRangeLength_ReportsTheSameErrorAsBefore()
    {
        // 第 2 块的解压长度写成 0：不能预分配，逐块校验时在第 2 块报解压长度越界（第 1 块照常通过）。
        var bytes = Sample().ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Cp2077Format.ChunkTableOffset + Cp2077Format.ChunkEntrySize + 8), 0);
        var error = Assert.Throws<InvalidDataException>(() => SaveContainer.Read((ReadOnlyMemory<byte>)bytes));
        Assert.Equal($"块解压长度 0 超出安全范围 1..{Cp2077Format.MaxDecompressedChunkLength}。", error.Message);

        // 解压长度与块内声明不一致（在安全范围内）：同样在那一块报错。
        bytes = Sample().ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Cp2077Format.ChunkTableOffset + Cp2077Format.ChunkEntrySize + 8), 299);
        error = Assert.Throws<InvalidDataException>(() => SaveContainer.Read((ReadOnlyMemory<byte>)bytes));
        Assert.Equal("块内解压长度与分块表不一致。", error.Message);
    }

    private static SaveContainer Sample() => SaveContainer.Create(
        Cp2077Format.SupportedSaveVersion,
        Cp2077Format.SupportedGameVersion,
        timestamp: 0x1122_3344_5566_7788,
        archiveVersion: 195,
        decompressedChunks: [Pattern(128, 3), Pattern(300, 9), Pattern(64, 21)],
        nodeTable: [1, 2, 3, 4, 5, 6, 7, 8]);

    private static byte[] Pattern(int length, byte seed)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(seed + i);
        return data;
    }
}
