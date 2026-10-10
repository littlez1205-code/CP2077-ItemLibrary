namespace GameTool.Adapters.Cyberpunk2077.Localization;

public sealed class RdarFile
{
    internal RdarFile(ulong pathHash, int firstSegment, int segmentCount)
    {
        PathHash = pathHash;
        FirstSegment = firstSegment;
        SegmentCount = segmentCount;
    }

    public ulong PathHash { get; }
    public int FirstSegment { get; }
    public int SegmentCount { get; }
}

/// <summary>
/// RED 归档（RDAR，本机实测 version 12）。
/// 索引：8 字节头 + N 条 56 字节文件记录 + 4 字节校验 + 16 字节段表头 + N 条 16 字节段。
/// N = (indexSize - 28) / 72。段数据以 KARK 开头，其后是 Oodle 流。
/// </summary>
public sealed class RdarArchive
{
    private readonly byte[] _bytes;
    private readonly Segment[] _segments;
    private readonly Dictionary<ulong, RdarFile> _files;

    private RdarArchive(byte[] bytes, Segment[] segments, Dictionary<ulong, RdarFile> files)
    {
        _bytes = bytes;
        _segments = segments;
        _files = files;
    }

    public static RdarArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    public static RdarArchive Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 40 || bytes[0] != (byte)'R' || bytes[1] != (byte)'D' || bytes[2] != (byte)'A' || bytes[3] != (byte)'R')
            throw new InvalidDataException("不是 RDAR 归档。");
        var version = BitConverter.ToUInt32(bytes, 4);
        if (version != 12)
            throw new InvalidDataException($"不支持的归档版本 {version}。");
        var indexOffset = checked((int)BitConverter.ToUInt64(bytes, 8));
        var indexSize = checked((int)BitConverter.ToUInt32(bytes, 16));
        var fileSize = BitConverter.ToUInt64(bytes, 32);
        if (fileSize != (ulong)bytes.Length)
            throw new InvalidDataException("归档声明的长度和文件实际长度不一致。");
        if (indexOffset < 40 || indexSize < 28 || indexOffset > bytes.Length - indexSize)
            throw new InvalidDataException("归档索引越界。");
        if ((indexSize - 28) % 72 != 0)
            throw new InvalidDataException("无法识别的归档索引。");
        var count = (indexSize - 28) / 72;
        var records = indexOffset + 8;
        var segmentsAt = records + count * 56 + 4 + 16;
        var segments = new Segment[count];
        for (var i = 0; i < count; i++)
        {
            var at = segmentsAt + i * 16;
            var offset = checked((int)BitConverter.ToUInt64(bytes, at));
            var compressed = checked((int)BitConverter.ToUInt32(bytes, at + 8));
            var uncompressed = checked((int)BitConverter.ToUInt32(bytes, at + 12));
            if (offset < 0 || compressed < 8 || offset > bytes.Length - compressed)
                throw new InvalidDataException($"归档段 {i} 越界。");
            segments[i] = new Segment(offset, compressed, uncompressed);
        }

        var files = new Dictionary<ulong, RdarFile>();
        for (var i = 0; i < count; i++)
        {
            var at = records + i * 56;
            var hash = BitConverter.ToUInt64(bytes, at + 20);
            var first = checked((int)BitConverter.ToUInt32(bytes, at + 40));
            var next = checked((int)BitConverter.ToUInt32(bytes, at + 44));
            if (next < first || next > count)
                throw new InvalidDataException($"归档文件 {i} 的段范围无效。");
            if (first == next)
                continue;
            if (!files.TryAdd(hash, new RdarFile(hash, first, next - first)))
                throw new InvalidDataException($"归档里有重复的路径哈希 {hash:X16}。");
        }

        return new RdarArchive(bytes, segments, files);
    }

    public RdarFile? Find(ulong pathHash) => _files.TryGetValue(pathHash, out var file) ? file : null;

    public byte[] Read(RdarFile file, IOodleDecompressor oodle)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(oodle);
        using var output = new MemoryStream();
        for (var i = 0; i < file.SegmentCount; i++)
        {
            var segment = _segments[file.FirstSegment + i];
            if (segment.Compressed < 8
                || _bytes[segment.Offset] != (byte)'K'
                || _bytes[segment.Offset + 1] != (byte)'A'
                || _bytes[segment.Offset + 2] != (byte)'R'
                || _bytes[segment.Offset + 3] != (byte)'K')
                throw new InvalidDataException("归档段缺少 KARK 头。");
            var declared = BitConverter.ToUInt32(_bytes, segment.Offset + 4);
            if (declared != (uint)segment.Uncompressed)
                throw new InvalidDataException("KARK 声明的长度和段表不一致。");
            var decoded = oodle.Decompress(_bytes.AsSpan(segment.Offset + 8, segment.Compressed - 8), segment.Uncompressed);
            if (decoded.Length != segment.Uncompressed)
                throw new InvalidDataException("解压结果长度和段表不一致。");
            output.Write(decoded);
        }

        return output.ToArray();
    }

    private readonly record struct Segment(int Offset, int Compressed, int Uncompressed);
}
