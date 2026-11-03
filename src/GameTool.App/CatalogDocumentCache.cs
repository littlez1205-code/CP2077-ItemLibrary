using GameTool.Core;

namespace GameTool.App;

/// <summary>
/// 物品库目录（只读查询）用的存档缓存。翻页、搜索、切换分类和筛选都要查目录，原来每次都完整读取、解压、解析整个存档；
/// 现在存档没变时直接复用上一次解析好的文档。
/// 「没变」= 同一个适配器、同一个文件、大小和修改时间都相同、游戏目录和语言相同，并且本进程之后没有写过存档
/// （<see cref="SaveFileWrites"/>，文件时间的精度不够区分两次很快的写入）。
/// 只留最近一份。缓存的文档只给只读查询用，同一份文档上的查询串行进行；写入路径一律自己重新读取存档，不经过这里。
/// </summary>
internal sealed class CatalogDocumentCache
{
    private readonly Lock _gate = new();
    private Entry? _entry;

    public T Query<T>(IGameAdapter adapter, string saveFilePath, Func<IItemGenerateCapable, T> query)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(query);
        // 先取写入次数和文件状态、再读存档：读的过程中文件被改写时，下一次取到的键一定对不上，会重新读取，
        // 不会出现「新的键配旧的内容」。
        var key = CacheKey.Of(adapter, saveFilePath);
        if (key is null)
            return query(Load(adapter, saveFilePath));

        Entry? entry;
        lock (_gate)
            entry = _entry is not null && _entry.Key == key.Value ? _entry : null;
        if (entry is null)
        {
            entry = new Entry(key.Value, Load(adapter, saveFilePath));
            lock (_gate)
                _entry = entry;
        }

        lock (entry.Gate)
            return query(entry.Document);
    }

    private static IItemGenerateCapable Load(IGameAdapter adapter, string saveFilePath) =>
        adapter.Load(saveFilePath) as IItemGenerateCapable
            ?? throw new PatchRejectedException(UiText.T("这个存档不能生成物品。", "這個存檔不能生成物品。", "This save does not support generating items."));

    private readonly record struct CacheKey(
        IGameAdapter Adapter,
        string Path,
        long Length,
        DateTime LastWriteTimeUtc,
        string? GameDirectory,
        string? Language,
        long Writes)
    {
        /// <summary>文件不存在或取不到状态时返回 null：不走缓存，照原样读取，报原来的错误。</summary>
        public static CacheKey? Of(IGameAdapter adapter, string saveFilePath)
        {
            var writes = SaveFileWrites.Generation;
            FileInfo info;
            try
            {
                info = new FileInfo(saveFilePath);
                if (!info.Exists)
                    return null;
                var setup = adapter as IGamePathSetup;
                return new CacheKey(adapter, info.FullName, info.Length, info.LastWriteTimeUtc, setup?.GameDirectory, setup?.Language, writes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }

    private sealed class Entry(CacheKey key, IItemGenerateCapable document)
    {
        public CacheKey Key { get; } = key;

        public IItemGenerateCapable Document { get; } = document;

        public Lock Gate { get; } = new();
    }
}
