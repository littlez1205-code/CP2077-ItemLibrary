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
        byte[] file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            file = ReadToEnd(stream);
        return Parse(file);
    }

    /// <summary>调用方的内存可能之后被改动，先复制一份；容器会直接引用其中的压缩块。</summary>
    public static SaveContainer Read(ReadOnlyMemory<byte> file) => Parse(file.ToArray());

    /// <summary>
    /// 按打开时的长度一次读进一个数组（原来经 MemoryStream 扩容再 ToArray，整个文件要复制两三遍）。
    /// 与原来一样读到实际的文件末尾：读的过程中文件变短就截短，变长就继续读完。
    /// </summary>
    private static byte[] ReadToEnd(FileStream stream)
    {
        var expected = stream.Length;
        if (expected > Array.MaxLength)
            throw new InvalidDataException(UiText.T("存档文件过大。", "存檔檔案過大。", "The save file is too large."));
        var buffer = new byte[expected];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0)
                return buffer.AsSpan(0, read).ToArray();
            read += count;
        }

        var next = stream.ReadByte();
        if (next < 0)
            return buffer;
        using var rest = new MemoryStream();
        rest.Write(buffer);
        rest.WriteByte((byte)next);
        stream.CopyTo(rest);
        return rest.ToArray();
    }

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
                UiText.T($"块数必须在 1..{Cp2077Format.ChunkSlotCount}。", $"塊數必須在 1..{Cp2077Format.ChunkSlotCount}。", $"The chunk count must be within 1..{Cp2077Format.ChunkSlotCount}."));
        }

        var total = 0;
        foreach (var plain in decompressedChunks)
        {
            if (plain is null)
                throw new ArgumentException(UiText.T("块不能为 null。", "塊不能為 null。", "A chunk cannot be null."), nameof(decompressedChunks));
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
            throw new ArgumentOutOfRangeException(nameof(payload), UiText.T("解压数据不能为空。", "解壓資料不能為空。", "The decompressed data cannot be empty."));
        var size = Cp2077Format.DecompressedChunkSize;
        var count = (payload.Length + size - 1) / size;
        if (count > Cp2077Format.ChunkSlotCount)
            throw new InvalidDataException(UiText.T($"块数 {count} 超出安全范围 1..{Cp2077Format.ChunkSlotCount}。", $"塊數 {count} 超出安全範圍 1..{Cp2077Format.ChunkSlotCount}。", $"Chunk count {count} is outside the safe range 1..{Cp2077Format.ChunkSlotCount}."));

        var chunks = new Chunk[count];
        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            var length = Math.Min(size, payload.Length - offset);
            var chunk = new Chunk(offset, length, ReadOnlyMemory<byte>.Empty);
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
            throw new ArgumentException(UiText.T("节点表过短。", "節點表過短。", "The node table is too short."), nameof(nodeTable));
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
            throw new ArgumentOutOfRangeException(nameof(offset), UiText.T("写入超出解压数据范围。", "寫入超出解壓資料範圍。", "The write goes beyond the decompressed data."));
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
        var images = new ReadOnlyMemory<byte>[_chunks.Length];
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
            image.Span.CopyTo(span.Slice(cursor));
            cursor += image.Length;
        }

        table.CopyTo(span.Slice(Cp2077Format.ChunkTableOffset));

        _nodeTable.CopyTo(span.Slice(cursor));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(file.Length - 8), (uint)cursor);
        Cp2077Format.Done.CopyTo(span.Slice(file.Length - 4));
        return file;
    }

    /// <summary>
    /// 解析整个文件。file 归容器所有：未改动的块写回时原样复制，直接引用 file 里的压缩字节，不再逐块复制。
    /// </summary>
    private static SaveContainer Parse(byte[] fileBytes)
    {
        ReadOnlySpan<byte> file = fileBytes;
        if (file.Length < Cp2077Format.FirstChunkOffset + 8)
            throw new InvalidDataException(UiText.T("存档短于容器头。", "存檔短於容器頭。", "The save is shorter than the container header."));
        if (!file.StartsWith(Cp2077Format.Vasc))
            throw new InvalidDataException(UiText.T("缺少 VASC 魔数。", "缺少 VASC 魔數。", "The VASC magic number is missing."));
        if (file[12] != 0)
            throw new InvalidDataException(UiText.T("元数据字符串不是实测的空串（单字节 00）。", "後設資料字串不是實測的空串（單位元組 00）。", "The metadata string is not the expected empty string (a single 00 byte)."));
        if (!file.Slice(Cp2077Format.FzlcOffset, 4).SequenceEqual(Cp2077Format.Fzlc))
            throw new InvalidDataException(UiText.T("缺少 FZLC 魔数。", "缺少 FZLC 魔數。", "The FZLC magic number is missing."));

        var saveVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(4));
        var gameVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(8));
        var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(file.Slice(13));
        var archiveVersion = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(21));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(Cp2077Format.ChunkCountOffset));
        if (count is < 1 or > Cp2077Format.ChunkSlotCount)
        {
            throw new InvalidDataException(
                UiText.T($"块数 {count} 超出安全范围 1..{Cp2077Format.ChunkSlotCount}。", $"塊數 {count} 超出安全範圍 1..{Cp2077Format.ChunkSlotCount}。", $"Chunk count {count} is outside the safe range 1..{Cp2077Format.ChunkSlotCount}."));
        }

        if (!Cp2077Format.IsSupported(saveVersion, gameVersion))
        {
            throw new UnsupportedSaveException(
                saveVersion,
                gameVersion,
                UiText.T($"存档版本 {saveVersion}、游戏版本 {gameVersion} 不在白名单内。支持范围：v2.31（存档 269，游戏 2310）或 v2.5（存档 277，游戏 2500）。", $"存檔版本 {saveVersion}、遊戲版本 {gameVersion} 不在白名單內。支援範圍：v2.31（存檔 269，遊戲 2310）或 v2.5（存檔 277，遊戲 2500）。", $"Save version {saveVersion} / game version {gameVersion} is not supported. Supported: v2.31 (save 269, game 2310) or v2.5 (save 277, game 2500)."));
        }

        // 先按分块表算出解压后的总长，一次分配载荷，各块直接解压进去（原来每块单独分配，最后再整体拷贝一遍）。
        // 分块表里的解压长度有越界、或总长超出 512 个标准块时不预分配，退回原来的做法：逐块解压、最后拼接，
        // 校验顺序和报错与原来完全相同。
        var payload = TryAllocatePayload(file, (int)count);
        var plains = payload is null ? new byte[count][] : null;
        var chunks = new Chunk[count];
        var cursor = Cp2077Format.FirstChunkOffset;
        var payloadCursor = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = Cp2077Format.ChunkTableOffset + (i * Cp2077Format.ChunkEntrySize);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry));
            var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry + 4));
            var decompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry + 8));
            if (offset != cursor)
                throw new InvalidDataException(UiText.T($"第 {i} 块偏移 {offset} 与连续布局 {cursor} 不一致。", $"第 {i} 塊偏移 {offset} 與連續佈局 {cursor} 不一致。", $"Chunk {i} starts at offset {offset} instead of the contiguous position {cursor}."));
            if (compressedLength < Cp2077Format.ChunkHeaderSize)
                throw new InvalidDataException(UiText.T($"第 {i} 块压缩长度 {compressedLength} 短于块头。", $"第 {i} 塊壓縮長度 {compressedLength} 短於塊頭。", $"Chunk {i} has a compressed length of {compressedLength}, shorter than its header."));
            if (cursor > file.Length - 8 || compressedLength > file.Length - 8 - cursor)
                throw new InvalidDataException(UiText.T($"第 {i} 块超出文件范围。", $"第 {i} 塊超出檔案範圍。", $"Chunk {i} extends beyond the end of the file."));

            var image = file.Slice(cursor, (int)compressedLength);
            int plainLength;
            if (payload is not null)
            {
                Lz4Chunks.DecodeInto(image, payload.AsSpan(payloadCursor, (int)decompressedLength));
                plainLength = (int)decompressedLength;
            }
            else
            {
                plains![i] = Lz4Chunks.Decode(image, (int)decompressedLength);
                plainLength = plains[i].Length;
            }

            chunks[i] = new Chunk(payloadCursor, plainLength, fileBytes.AsMemory(cursor, (int)compressedLength));
            payloadCursor = checked(payloadCursor + plainLength);
            cursor += (int)compressedLength;
        }

        var chunkTable = file.Slice(Cp2077Format.ChunkTableOffset, Cp2077Format.ChunkSlotCount * Cp2077Format.ChunkEntrySize).ToArray();

        if (file.Length < cursor + 8)
            throw new InvalidDataException(UiText.T("文件在节点表之前被截断。", "檔案在節點表之前被截斷。", "The file is truncated before the node table."));
        if (!file.Slice(file.Length - 4, 4).SequenceEqual(Cp2077Format.Done))
            throw new InvalidDataException(UiText.T("缺少 DONE 魔数。", "缺少 DONE 魔數。", "The DONE magic number is missing."));

        var nodeOffset = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(file.Length - 8, 4));
        if (nodeOffset != cursor)
            throw new InvalidDataException(UiText.T($"节点表偏移 {nodeOffset} 与最后一块末尾 {cursor} 不一致。", $"節點表偏移 {nodeOffset} 與最後一塊末尾 {cursor} 不一致。", $"The node table offset {nodeOffset} does not match the end of the last chunk {cursor}."));

        if (payload is null)
        {
            payload = new byte[payloadCursor];
            var at = 0;
            foreach (var plain in plains!)
            {
                plain.CopyTo(payload.AsSpan(at));
                at += plain.Length;
            }
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

    /// <summary>
    /// 分块表里每块的解压长度都在 1..上限内、且总长不超过 512 个标准块时，按总长分配载荷；否则返回 null，
    /// 由逐块校验报出与原来相同的错误。
    /// </summary>
    private static byte[]? TryAllocatePayload(ReadOnlySpan<byte> file, int count)
    {
        const long limit = (long)Cp2077Format.ChunkSlotCount * Cp2077Format.DecompressedChunkSize;
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = Cp2077Format.ChunkTableOffset + (i * Cp2077Format.ChunkEntrySize);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(entry + 8));
            if (length is < 1 or > Cp2077Format.MaxDecompressedChunkLength)
                return null;
            total += length;
        }

        return total <= limit ? new byte[total] : null;
    }

    private sealed class Chunk
    {
        public Chunk(int payloadOffset, int length, ReadOnlyMemory<byte> image)
        {
            PayloadOffset = payloadOffset;
            Length = length;
            Image = image;
        }

        public int PayloadOffset { get; }
        public int Length { get; }
        /// <summary>原样写回用的压缩字节（含块头）；脏块写出时重新压缩，不用它。</summary>
        public ReadOnlyMemory<byte> Image { get; }
        public bool Dirty { get; set; }
    }
}
