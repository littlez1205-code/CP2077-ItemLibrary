using GameTool.Adapters.Cyberpunk2077.Localization;

namespace GameTool.Adapters.Cyberpunk2077.Archives;

/// <summary>
/// 游戏目录里的一组归档，按深度路径查文件。archive\pc\ep1 先于 archive\pc\content，各自按文件名排序（ep1 可以没有：没买往日之影的玩家只有 content，D023）；
/// 跳过 audio_* 和 lang_*（图标用不到）。只读，不复制、不改动游戏文件。
/// </summary>
public sealed class GameArchiveSet : IDisposable
{
    private readonly List<StreamingRdarArchive> _archives;
    private readonly IOodleDecompressor _oodle;

    private GameArchiveSet(List<StreamingRdarArchive> archives, IOodleDecompressor oodle)
    {
        _archives = archives;
        _oodle = oodle;
    }

    public int ArchiveCount => _archives.Count;

    public static string[] ArchiveFolders(string gameDirectory) =>
    [
        Path.Combine(gameDirectory, "archive", "pc", "ep1"),
        Path.Combine(gameDirectory, "archive", "pc", "content")
    ];

    public static GameArchiveSet Open(string gameDirectory, IOodleDecompressor oodle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(oodle);
        var archives = new List<StreamingRdarArchive>();
        try
        {
            foreach (var folder in ArchiveFolders(gameDirectory))
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var path in Directory.EnumerateFiles(folder, "*.archive").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(path);
                    if (name.StartsWith("audio_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("lang_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    archives.Add(StreamingRdarArchive.Open(path));
                }
            }
        }
        catch
        {
            foreach (var archive in archives)
                archive.Dispose();
            throw;
        }

        if (archives.Count == 0)
            throw new InvalidDataException($"游戏目录里没有可用的归档：{gameDirectory}");
        return new GameArchiveSet(archives, oodle);
    }

    /// <summary>深度路径的哈希：小写、正斜杠换成反斜杠，再做 FNV-1a 64。</summary>
    public static ulong HashDepotPath(string depotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(depotPath);
        return Fnv1a64.HashPath(depotPath.Trim().Replace('/', '\\').ToLowerInvariant());
    }

    /// <summary>按深度路径找到文件；不存在返回 null。</summary>
    public ArchivedFile? Find(string depotPath)
    {
        var hash = HashDepotPath(depotPath);
        foreach (var archive in _archives)
        {
            if (archive.Contains(hash))
                return new ArchivedFile(archive, hash, _oodle);
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var archive in _archives)
            archive.Dispose();
    }
}

public sealed class ArchivedFile
{
    private readonly StreamingRdarArchive _archive;
    private readonly ulong _hash;
    private readonly IOodleDecompressor _oodle;

    internal ArchivedFile(StreamingRdarArchive archive, ulong hash, IOodleDecompressor oodle)
    {
        _archive = archive;
        _hash = hash;
        _oodle = oodle;
    }

    public int SegmentCount => _archive.SegmentCount(_hash);

    /// <summary>第 0 段：CR2W 本体。</summary>
    public byte[] ReadBody() => ReadSegment(0) ?? throw new InvalidDataException("归档文件没有本体段。");

    /// <summary>第 n 段（n ≥ 1 对应 CR2W 的第 n 个延迟缓冲）；不存在返回 null。</summary>
    public byte[]? ReadSegment(int segment) => _archive.ReadSegment(_hash, segment, _oodle);
}
