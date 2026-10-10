using System.Buffers.Binary;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Container;

/// <summary>
/// VASC 容器：头、固定 512 槽分块表、LZ4 块、不透明节点表、DONE 尾。
/// 解压后的块拼成一块连续的 <see cref="Payload"/>。压缩长度含 8 字节块头。
/// 干净块原样复制；脏块才重新压缩。
/// </summary>
public sealed class SaveContainer
{
    private byte[] _payload;
    private Chunk[] _chunks;
    private byte[] _nodeTable;
    private readonly byte[] _chunkTable;

    private SaveContainer(
        uint saveVersion,
        uint gameVersion,
        ulong timestamp,
        uint archiveVersion,
        byte[] payload,
        Chunk[] chunks,
        byte[] nodeTable,
        byte[] chunkTable,
        uint nodeTableOffset)
    {
        SaveVersion = saveVersion;
        GameVersion = gameVersion;
        Timestamp = timestamp;
        ArchiveVersion = archiveVersion;
        _payload = payload;
        _chunks = chunks;
        _nodeTable = nodeTable;
        _chunkTable = chunkTable;
        NodeTableOffset = nodeTableOffset;
        DecompressedLength = payload.LongLength;
    }

    public uint SaveVersion { get; }
    public uint GameVersion { get; }
    public ulong Timestamp { get; }
    public uint ArchiveVersion { get; }
    public int ChunkCount => _chunks.Length;
    public long DecompressedLength { get; private set; }
    public uint NodeTableOffset { get; }

    public ReadOnlySpan<byte> Payload => _payload;

    public static SaveContainer Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    public static SaveContainer Read(ReadOnlyMemory<byte> file) => Read(file.Span);

    public static SaveContainer Create(
        uint saveVersion,
        uint gameVersion,
        ulong timestamp,
        uint archiveVersion,
        IReadOnlyList<byte[]> decompressedChunks,
        byte[] nodeTable)
    {
        ArgumentNullException.ThrowIfNull(decompressedChunks);
        ArgumentNullException.ThrowIfNull(nodeTable);
        if (decompressedChunks.Count is < 1 or > Cp2077Format.ChunkSlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decompressedChunks),
                $"块数必须在 1..{Cp2077Format.ChunkSlotCount}。");
        }

        var total = 0;
        foreach (var plain in decompressedChunks)
        {
            if (plain is null)
                throw new ArgumentException("块不能为 null。", nameof(decompressedChunks));
            total = checked(total + plain.Length);
        }

        var payload = new byte[total];
        var chunks = new Chunk[decompressedChunks.Count];
        var fileCursor = Cp2077Format.FirstChunkOffset;
        var payloadCursor = 0;
        for (var i = 0; i < decompressedChunks.Count; i++)
        {
            var plain = decompressedChunks[i];
            plain.CopyTo(payload.AsSpan(payloadCursor));
            var image = Lz4Chunks.Encode(plain);
            chunks[i] = new Chunk(payloadCursor, plain.Length, image);
            payloadCursor += plain.Length;
            fileCursor += image.Length;
        }

        return new SaveContainer(
            saveVersion,
            gameVersion,
            timestamp,
            archiveVersion,
            payload,
            chunks,
            nodeTable.ToArray(),
            new byte[Cp2077Format.ChunkSlotCount * Cp2077Format.ChunkEntrySize],
            (uint)fileCursor);
    }

    public byte[] GetDecompressed(int index)
    {
        var chunk = _chunks[index];
        return _payload.AsSpan(chunk.PayloadOffset, chunk.Length).ToArray();
    }

    public byte[] CopyNodeTable() => (byte[])_nodeTable.Clone();

    /// <summary>
    /// 用新的解压数据替换载荷，按 <see cref="Cp2077Format.DecompressedChunkSize"/> 重新切块。
    /// 全部分块标脏。未用槽位里的残留字节保留；块数增加时，新占用的槽位在写出时被覆盖。
    /// </summary>
    public void ReplacePayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(payload), "解压数据不能为空。");
        var size = Cp2077Format.DecompressedChunkSize;
        var count = (payload.Length + size - 1) / size;
        if (count > Cp2077Format.ChunkSlotCount)
            throw new InvalidDataException($"块数 {count} 超出安全范围 1..{Cp2077Format.ChunkSlotCount}。");

        var chunks = new Chunk[count];
        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            var length = Math.Min(size, payload.Length - offset);
            var chunk = new Chunk(offset, length, []);
            chunk.Dirty = true;
            chunks[i] = chunk;
            offset += length;
        }

        _payload = payload;
        _chunks = chunks;
        DecompressedLength = payload.LongLength;
    }

    public void ReplaceNodeTable(byte[] nodeTable)
    {
        ArgumentNullException.ThrowIfNull(nodeTable);
        if (nodeTable.Length < 5)
            throw new ArgumentException("节点表过短。", nameof(nodeTable));
        _nodeTable = nodeTable.ToArray();
    }

    public void MarkAllDirty()
    {
        foreach (var chunk in _chunks)
            chunk.Dirty = true;
    }

    public bool IsChunkDirty(int index) => _chunks[index].Dirty;

    public void WritePayload(int offset, ReadOnlySpan<byte> data)
    {
        if (offset < 0 || data.Length > _payload.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "写入超出解压数据范围。");
        data.CopyTo(_payload.AsSpan(offset));
        var end = offset + data.Length;
        foreach (var chunk in _chunks)
        {
            var chunkEnd = chunk.PayloadOffset + chunk.Length;
            if (offset < chunkEnd && chunk.PayloadOffset < end)
                chunk.Dirty = true;
        }
    }

    public void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var bytes = ToArray();
        output.Write(bytes);
    }

    public byte[] ToArray()
    {
        var images = new byte[_chunks.Length][];
        var compressed = 0;
        for (var i = 0; i < _chunks.Length; i++)
        {
            var chunk = _chunks[i];
            images[i] = chunk.Dirty
                ? Lz4Chunks.Encode(_payload.AsSpan(chunk.PayloadOffset, chunk.Length))
                : chunk.Image;
            compressed += images[i].Length;
        }

        var nodeOffset = Cp2077Format.FirstChunkOffset + compressed;
        var file = new byte[nodeOffset + _nodeTable.Length + 8];
        var span = file.AsSpan();

        Cp2077Format.Vasc.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), SaveVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), GameVersion);
        span[12] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(13), Timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(21), ArchiveVersion);
        Cp2077Format.Fzlc.CopyTo(span.Slice(Cp2077Format.FzlcOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(Cp2077Format.ChunkCountOffset), (uint)_chunks.Length);
        var table = (byte[])_chunkTable.Clone();
        var cursor = Cp2077Format.FirstChunkOffset;
        for (var i = 0; i < _chunks.Length; i++)
        {
            var image = images[i];
            var entry = i * Cp2077Format.ChunkEntrySize;
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(entry), (uint)cursor);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(entry + 4), (uint)image.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(entry + 8), (uint)_chunks[i].Length);
            image.CopyTo(span.Slice(cursor));
            cursor += image.Length;
        }

        table.CopyTo(span.Slice(Cp2077Format.ChunkTableOffset));

        _nodeTable.CopyTo(span.Slice(cursor));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(file.Length - 8), (uint)cursor);
        Cp2077Format.Done.CopyTo(span.Slice(file.Length - 4));
        return file;
    }

    private static SaveContainer Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < Cp2077Format.FirstChunkOffset + 8)
            throw new InvalidDataException("存档短于容器头。");
        if (!file.StartsWith(Cp2077Format.Vasc))
            throw new InvalidDataException("缺少 VASC 魔数。");
        if (file[12] != 0)
            throw new InvalidDataException("元数据字符串不是实测的空串（单字节 00）。");
        if (!file.Slice(Cp2077Format.FzlcOffset, 4).SequenceEqual(Cp2077Format.Fzlc))
            throw new InvalidDataException("缺少 FZLC 魔数。");

        var saveVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(4));
        var gameVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(8));
        var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(file.Slice(13));
        var archiveVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(21));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(Cp2077Format.ChunkCountOffset));
        if (count is < 1 or > Cp2077Format.ChunkSlotCount)
        {
            throw new InvalidDataException(
                $"块数 {count} 超出安全范围 1..{Cp2077Format.ChunkSlotCount}。");
        }

        if (saveVersion != Cp2077Format.SupportedSaveVersion || gameVersion != Cp2077Format.SupportedGameVersion)
        {
            throw new UnsupportedSaveException(
                saveVersion,
                gameVersion,
                $"存档版本 {saveVersion}、游戏版本 {gameVersion} 不在白名单内。安全范围：存档版本 {Cp2077Format.SupportedSaveVersion}，游戏版本 {Cp2077Format.SupportedGameVersion}。");
        }

        var plains = new byte[count][];
        var images = new byte[count][];
        var cursor = Cp2077Format.FirstChunkOffset;
        var payloadLength = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = Cp2077Format.ChunkTableOffset + (i * Cp2077Format.ChunkEntrySize);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry));
            var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry + 4));
            var decompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry + 8));
            if (offset != cursor)
                throw new InvalidDataException($"第 {i} 块偏移 {offset} 与连续布局 {cursor} 不一致。");
            if (compressedLength < Cp2077Format.ChunkHeaderSize)
                throw new InvalidDataException($"第 {i} 块压缩长度 {compressedLength} 短于块头。");
            if (cursor > file.Length - 8 || compressedLength > file.Length - 8 - cursor)
                throw new InvalidDataException($"第 {i} 块超出文件范围。");

            var image = file.Slice(cursor, (int)compressedLength);
            plains[i] = Lz4Chunks.Decode(image, (int)decompressedLength);
            images[i] = image.ToArray();
            payloadLength = checked(payloadLength + plains[i].Length);
            cursor += (int)compressedLength;
        }

        var chunkTable = file.Slice(Cp2077Format.ChunkTableOffset, Cp2077Format.ChunkSlotCount * Cp2077Format.ChunkEntrySize).ToArray();

        if (file.Length < cursor + 8)
            throw new InvalidDataException("文件在节点表之前被截断。");
        if (!file.Slice(file.Length - 4, 4).SequenceEqual(Cp2077Format.Done))
            throw new InvalidDataException("缺少 DONE 魔数。");

        var nodeOffset = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(file.Length - 8, 4));
        if (nodeOffset != cursor)
            throw new InvalidDataException($"节点表偏移 {nodeOffset} 与最后一块末尾 {cursor} 不一致。");

        var payload = new byte[payloadLength];
        var chunks = new Chunk[count];
        var payloadCursor = 0;
        for (var i = 0; i < count; i++)
        {
            plains[i].CopyTo(payload.AsSpan(payloadCursor));
            chunks[i] = new Chunk(payloadCursor, plains[i].Length, images[i]);
            payloadCursor += plains[i].Length;
        }

        var nodeTable = file.Slice(cursor, file.Length - 8 - cursor).ToArray();
        return new SaveContainer(
            saveVersion,
            gameVersion,
            timestamp,
            archiveVersion,
            payload,
            chunks,
            nodeTable,
            chunkTable,
            nodeOffset);
    }

    private sealed class Chunk
    {
        public Chunk(int payloadOffset, int length, byte[] image)
        {
            PayloadOffset = payloadOffset;
            Length = length;
            Image = image;
        }

        public int PayloadOffset { get; }
        public int Length { get; }
        public byte[] Image { get; }
        public bool Dirty { get; set; }
    }
}
