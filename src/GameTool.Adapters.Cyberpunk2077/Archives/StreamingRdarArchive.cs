using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077.Localization;
using Microsoft.Win32.SafeHandles;

namespace GameTool.Adapters.Cyberpunk2077.Archives;

/// <summary>
/// 只读、按需读取的 RDAR v12 归档（T020 预研第 2 节）。只把索引留在内存里，段数据用定位读取（RandomAccess），
/// 所以能打开 3.3 GB 的 basegame_4_gamedata，也可以多线程同时读。
/// 头 40 字节：RDAR、版本 12、索引偏移 u64 @8、索引长度 u32 @16。
/// 索引：u32 表偏移、u32 表长、u64 校验、u32 文件数、u32 段数、u32 依赖数，
/// 然后是文件数 × 56 字节（u64 路径哈希、i64 时间、u32 内联缓冲数、u32 段起、u32 段止、u32 依赖起、u32 依赖止、20 字节 SHA-1），
/// 再是段数 × 16 字节（u64 偏移、u32 盘上长度、u32 解压后长度）。
/// 一个文件的第 0 段是 CR2W 本体，第 1..n 段是它的延迟缓冲（贴图的像素数据在第 1 段）。
/// 与 <see cref="RdarArchive"/> 不同：那个整文件读入且假定文件数等于段数，只适合语言包。
/// </summary>
public sealed class StreamingRdarArchive : IDisposable
{
    private const int HeaderLength = 40;
    private const int IndexHeaderLength = 28;
    private const int FileEntryLength = 56;
    private const int SegmentLength = 16;
    private const int MaxIndexLength = 256 * 1024 * 1024;

    private readonly SafeFileHandle _handle;
    private readonly Dictionary<ulong, (int Start, int End)> _files;
    private readonly Segment[] _segments;

    private StreamingRdarArchive(string path, SafeFileHandle handle, Dictionary<ulong, (int Start, int End)> files, Segment[] segments)
    {
        Path = path;
        _handle = handle;
        _files = files;
        _segments = segments;
    }

    public string Path { get; }

    public int FileCount => _files.Count;

    public static StreamingRdarArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // 只读打开，并允许别的进程（包括正在运行的游戏）照常读写这个文件。
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            var length = RandomAccess.GetLength(handle);
            var header = new byte[HeaderLength];
            ReadExactly(handle, header, 0);
            if (!header.AsSpan(0, 4).SequenceEqual("RDAR"u8))
                throw new InvalidDataException($"不是 RDAR 归档：{path}");
            var version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (version != 12)
                throw new InvalidDataException($"不支持的归档版本 {version}：{path}");
            var indexOffset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
            var indexSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
            if (indexSize < IndexHeaderLength || indexSize > MaxIndexLength || indexOffset < HeaderLength || indexOffset > (ulong)length - indexSize)
                throw new InvalidDataException($"归档索引越界：{path}");
            var index = new byte[indexSize];
            ReadExactly(handle, index, (long)indexOffset);
            var (files, segments) = ParseIndex(index, length);
            return new StreamingRdarArchive(path, handle, files, segments);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>解析索引块（从索引头开始）。公开给测试用合成数据验证多段文件。</summary>
    public static (Dictionary<ulong, (int Start, int End)> Files, Segment[] Segments) ParseIndex(ReadOnlySpan<byte> index, long archiveLength)
    {
        if (index.Length < IndexHeaderLength)
            throw new InvalidDataException("归档索引太短。");
        var fileCount = BinaryPrimitives.ReadUInt32LittleEndian(index[16..]);
        var segmentCount = BinaryPrimitives.ReadUInt32LittleEndian(index[20..]);
        var needed = IndexHeaderLength + (long)fileCount * FileEntryLength + (long)segmentCount * SegmentLength;
        if (needed > index.Length)
            throw new InvalidDataException("归档索引的文件数或段数超出索引长度。");
        var segmentsAt = IndexHeaderLength + (int)fileCount * FileEntryLength;
        var segments = new Segment[segmentCount];
        for (var i = 0; i < segments.Length; i++)
        {
            var at = index[(segmentsAt + i * SegmentLength)..];
            var offset = BinaryPrimitives.ReadInt64LittleEndian(at);
            var disk = BinaryPrimitives.ReadUInt32LittleEndian(at[8..]);
            var memory = BinaryPrimitives.ReadUInt32LittleEndian(at[12..]);
            if (offset < 0 || offset > archiveLength - disk || memory > int.MaxValue)
                throw new InvalidDataException($"归档段 {i} 越界。");
            segments[i] = new Segment(offset, disk, memory);
        }

        var files = new Dictionary<ulong, (int Start, int End)>((int)fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            var at = index[(IndexHeaderLength + i * FileEntryLength)..];
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(at);
            var start = BinaryPrimitives.ReadUInt32LittleEndian(at[20..]);
            var end = BinaryPrimitives.ReadUInt32LittleEndian(at[24..]);
            if (end < start || end > segmentCount)
                throw new InvalidDataException($"归档文件 {i} 的段范围无效。");
            if (start == end)
                continue;
            files.TryAdd(hash, ((int)start, (int)end));
        }

        return (files, segments);
    }

    public bool Contains(ulong pathHash) => _files.ContainsKey(pathHash);

    /// <summary>文件的段数（第 0 段是本体）；不在归档里返回 0。</summary>
    public int SegmentCount(ulong pathHash) => _files.TryGetValue(pathHash, out var file) ? file.End - file.Start : 0;

    /// <summary>读出并解压文件的第 n 段。段不存在返回 null。</summary>
    public byte[]? ReadSegment(ulong pathHash, int segment, IOodleDecompressor oodle)
    {
        ArgumentNullException.ThrowIfNull(oodle);
        if (!_files.TryGetValue(pathHash, out var file) || segment < 0 || segment >= file.End - file.Start)
            return null;
        var info = _segments[file.Start + segment];
        var raw = new byte[info.DiskSize];
        ReadExactly(_handle, raw, info.Offset);
        return Decode(raw, (int)info.MemorySize, oodle);
    }

    /// <summary>段数据：盘上长度等于解压长度时原样存放，否则是 KARK + u32 长度 + Oodle 流。</summary>
    public static byte[] Decode(byte[] raw, int memorySize, IOodleDecompressor oodle)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(oodle);
        if (raw.Length == memorySize)
            return raw;
        if (raw.Length < 8 || !raw.AsSpan(0, 4).SequenceEqual("KARK"u8))
            throw new InvalidDataException("归档段缺少 KARK 头。");
        if (BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)) != (uint)memorySize)
            throw new InvalidDataException("KARK 声明的长度和段表不一致。");
        var decoded = oodle.Decompress(raw.AsSpan(8), memorySize);
        if (decoded.Length != memorySize)
            throw new InvalidDataException("解压结果长度和段表不一致。");
        return decoded;
    }

    public void Dispose() => _handle.Dispose();

    private static void ReadExactly(SafeFileHandle handle, byte[] buffer, long offset)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var read = RandomAccess.Read(handle, buffer.AsSpan(done), offset + done);
            if (read <= 0)
                throw new EndOfStreamException("归档被截断。");
            done += read;
        }
    }

    public readonly record struct Segment(long Offset, uint DiskSize, uint MemorySize);
}
