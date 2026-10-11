namespace GameTool.App;

/// <summary>
/// 监视存档根目录（T030）：游戏存档（多次写入加改名）、新建 / 删除 / 改名槽位目录之后，去抖合并成一次通知，页面据此刷新存档列表。
/// 只关心两类变化：子目录里存档文件（2077 为 sav.dat）的创建、修改、删除、改名；根目录下一级条目（槽位目录）的创建、删除、改名。
/// 事件处理只做字符串匹配和重设计时器，不碰文件系统（T023：对 File.Replace 的 ~RF 中间文件查路径会撞上改名）；
/// 去抖到期后在计时器线程上调用一次回调，回调的异常吞掉。根目录不存在或无法监视时什么也不做，也不报错。
/// 工具自己写存档同样会触发一次通知，页面只是多刷新一次；刷新只读不写，不会形成循环。
/// </summary>
public sealed class SaveRootWatcher : IDisposable
{
    /// <summary>游戏写一次存档是多次写入加改名，1.5 秒内的事件合并成一次。</summary>
    public const int DefaultDebounceMilliseconds = 1500;

    public const string DefaultSaveFileName = "sav.dat";

    private readonly Action _onChanged;
    private readonly int _debounceMilliseconds;
    private readonly string _saveFileName;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly Lock _gate = new();
    private bool _disposed;

    public SaveRootWatcher(
        string root,
        Action onChanged,
        int debounceMilliseconds = DefaultDebounceMilliseconds,
        string saveFileName = DefaultSaveFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFileName);
        if (debounceMilliseconds is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(debounceMilliseconds), "防抖毫秒的安全范围是 0..60000。");
        _onChanged = onChanged;
        _debounceMilliseconds = debounceMilliseconds;
        _saveFileName = saveFileName;
        Root = root;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            Root = Path.GetFullPath(root);
            if (!Directory.Exists(Root))
                return;
            var watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            watcher.Created += (_, args) => OnEntry(args.Name, changed: false);
            watcher.Deleted += (_, args) => OnEntry(args.Name, changed: false);
            watcher.Changed += (_, args) => OnEntry(args.Name, changed: true);
            watcher.Renamed += (_, args) =>
            {
                OnEntry(args.OldName, changed: false);
                OnEntry(args.Name, changed: false);
            };
            // 缓冲区溢出等：不知道丢了什么，保守地刷新一次。
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or PlatformNotSupportedException)
        {
            _watcher = null;
        }
    }

    /// <summary>监视的根目录（规范化后的完整路径；无法规范化时为原样）。</summary>
    public string Root { get; }

    /// <summary>根目录存在、监视已开始。</summary>
    public bool Active => _watcher is not null;

    public int DebounceMilliseconds => _debounceMilliseconds;

    /// <summary>
    /// relative 是事件里相对根目录的路径。一级条目：槽位目录的创建、删除、改名（目录的 Changed 只表示里面有文件变了，交给二级判断）；
    /// 二级：槽位里的存档文件，任何变化都算。截图、metadata、更深的目录一律不管。
    /// </summary>
    public static bool IsRelevant(string? relative, bool changed, string saveFileName = DefaultSaveFileName)
    {
        if (string.IsNullOrEmpty(relative))
            return false;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length == 1)
            return !changed;
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Equals(saveFileName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        _timer.Dispose();
    }

    private void OnEntry(string? relative, bool changed)
    {
        if (IsRelevant(relative, changed, _saveFileName))
            Schedule();
    }

    /// <summary>每来一个相关事件就把计时器推迟到 debounce 之后：一串事件只在最后一个之后通知一次。</summary>
    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _timer.Change(_debounceMilliseconds, Timeout.Infinite);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        try
        {
            _onChanged();
        }
        catch (Exception)
        {
            // 通知失败（如窗口正在关闭）不影响监视本身。
        }
    }
}
