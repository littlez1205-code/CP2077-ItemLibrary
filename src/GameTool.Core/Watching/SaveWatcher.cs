using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace GameTool.Core;

public sealed record WatchEvent(
    string Path,
    string Outcome,
    IReadOnlyList<ResourceChangeResult>? Results,
    string? Error,
    long? WriteCompleteToDoneMs,
    long? FirstEventToDoneMs);

/// <summary>
/// 监听存档落盘并串行补丁。防抖毫秒由调用方传入；2077 使用 150。
/// 独占打开的重试间隔是 50 ms，最长 3 s。
/// </summary>
public sealed class SaveWatcher : IAsyncDisposable
{
    private const int OpenRetryMilliseconds = 50;
    private const int OpenTimeoutMilliseconds = 3000;

    private readonly IGameAdapter _adapter;
    private readonly string _root;
    private readonly IReadOnlyList<string> _globs;
    private readonly IReadOnlyList<ResourceChange> _changes;
    private readonly PatchOptions? _patchOptions;
    private readonly int _debounceMilliseconds;
    private readonly Action<WatchEvent> _onEvent;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<WorkItem> _channel = Channel.CreateUnbounded<WorkItem>();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _ownWrites = new(StringComparer.OrdinalIgnoreCase);
    private Task? _worker;

    public SaveWatcher(
        IGameAdapter adapter,
        string root,
        IReadOnlyList<string> includeGlobs,
        IReadOnlyList<ResourceChange> changes,
        int debounceMilliseconds,
        Action<WatchEvent> onEvent,
        PatchOptions? patchOptions = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(includeGlobs);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(onEvent);
        if (debounceMilliseconds < 0 || debounceMilliseconds > 60_000)
            throw new ArgumentOutOfRangeException(nameof(debounceMilliseconds), "防抖毫秒的安全范围是 0..60000。");

        _adapter = adapter;
        _root = Path.GetFullPath(root);
        _globs = includeGlobs;
        _changes = changes;
        _patchOptions = patchOptions;
        _debounceMilliseconds = debounceMilliseconds;
        _onEvent = onEvent;
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024
        };
        // 用事件里相对监听根的 Name，不用 FullPath 再做 GetRelativePath / GetFullPath：
        // File.Replace 会把原文件改名成 sav.dat~RFxxxx.TMP 再删除，名字带 ~ 时 Path.GetFullPath 会调 GetLongPathName
        // 去查这个文件，正好撞上 ReplaceFile 改名，导致写回方的 File.Replace 抛「无法删除要被替换的文件」（T023 实测）。
        _watcher.Created += (_, args) => OnPathSafe(args.Name);
        _watcher.Changed += (_, args) => OnPathSafe(args.Name);
        _watcher.Renamed += (_, args) => OnPathSafe(args.Name);
    }

    public void Start()
    {
        _worker = Task.Run(() => RunAsync(_stop.Token));
        _watcher.EnableRaisingEvents = true;
    }

    public void NoteOwnWrite(string path, DateTime lastWriteTimeUtc) =>
        _ownWrites[Path.GetFullPath(path)] = lastWriteTimeUtc;

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        await _stop.CancelAsync();
        _channel.Writer.TryComplete();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private void OnPathSafe(string? relative)
    {
        try
        {
            OnPath(relative);
        }
        catch (Exception ex)
        {
            try
            {
                _onEvent(new WatchEvent(Path.Join(_root, relative), "error", null, ex.Message, null, null));
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// relative 是事件里相对监听根的路径。先只做字符串匹配，匹配上了才拼出完整路径、碰文件系统；
    /// 不匹配的事件（临时文件、File.Replace 的 ~RF 中间文件、目录本身）不做任何文件系统调用。
    /// </summary>
    private void OnPath(string? relative)
    {
        if (_stop.IsCancellationRequested || string.IsNullOrEmpty(relative))
            return;
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || !IsMatch(relative, _globs))
            return;
        var fullPath = Path.Join(_root, relative);
        if (IsOwnWrite(fullPath))
            return;

        CancellationToken token;
        DateTime firstEvent;
        lock (_gate)
        {
            if (_pending.TryGetValue(fullPath, out var existing))
            {
                existing.Cancel.Cancel();
                existing.Cancel.Dispose();
                firstEvent = existing.FirstEvent;
            }
            else
            {
                firstEvent = DateTime.UtcNow;
            }

            var cancel = new CancellationTokenSource();
            _pending[fullPath] = new Pending(firstEvent, cancel);
            token = cancel.Token;
        }

        _ = DebounceAsync(fullPath, firstEvent, token);
    }

    private async Task DebounceAsync(string fullPath, DateTime firstEvent, CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounceMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pending.TryGetValue(fullPath, out var pending) || pending.Cancel.Token != token)
                return;
            _pending.Remove(fullPath);
            pending.Cancel.Dispose();
        }

        if (_stop.IsCancellationRequested || IsOwnWrite(fullPath))
            return;
        _channel.Writer.TryWrite(new WorkItem(fullPath, firstEvent, 0));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (IsOwnWrite(item.Path))
                continue;
            try
            {
                await ProcessAsync(item, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Emit(item, "error", null, ex.Message, null);
            }
        }
    }

    private async Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var ready = Stopwatch.StartNew();
        if (!await WaitUntilReadyAsync(item.Path, cancellationToken))
        {
            Emit(item, "error", null, "等待存档写完超时。", null);
            return;
        }

        ready.Restart();
        try
        {
            var report = _patchOptions is null
                ? PatchEngine.Patch(_adapter, item.Path, _changes)
                : PatchEngine.Patch(_adapter, item.Path, _changes, _patchOptions);
            var elapsed = ready.ElapsedMilliseconds;
            if (report.Outcome == PatchOutcome.Patched && report.WrittenLastWriteTimeUtc is DateTime written)
                _ownWrites[item.Path] = written;
            var outcome = report.Outcome;
            if (outcome == PatchOutcome.Patched
                && report.Results.Any(result => result.Outcome == ChangeOutcome.Inserted)
                && report.Results.All(result => result.Outcome != ChangeOutcome.Changed))
                outcome = ChangeOutcome.Inserted;
            Emit(item, outcome, report.Results, null, elapsed);
        }
        catch (Exception ex) when (ex is PatchRejectedException or AmbiguousResourceException or UnsupportedSaveException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (item.Attempt < 4 && IsRetryable(ex))
            {
                await Task.Delay(100, cancellationToken);
                _channel.Writer.TryWrite(item with { Attempt = item.Attempt + 1 });
                return;
            }

            Emit(item, "error", null, ex.Message, ready.ElapsedMilliseconds);
        }
    }

    private async Task<bool> WaitUntilReadyAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(OpenTimeoutMilliseconds);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                    return false;
                await Task.Delay(OpenRetryMilliseconds, cancellationToken);
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    return false;
                await Task.Delay(OpenRetryMilliseconds, cancellationToken);
                continue;
            }

            if (_adapter.IsComplete(path))
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(OpenRetryMilliseconds, cancellationToken);
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is IOException
        || exception is SaveChangedDuringPatchException;

    private void Emit(WorkItem item, string outcome, IReadOnlyList<ResourceChangeResult>? results, string? error, long? writeCompleteToDoneMs)
    {
        var firstToDone = (long)Math.Max(0, (DateTime.UtcNow - item.FirstEvent).TotalMilliseconds);
        _onEvent(new WatchEvent(item.Path, outcome, results, error, writeCompleteToDoneMs, firstToDone));
    }

    private bool IsOwnWrite(string path) =>
        _ownWrites.TryGetValue(path, out var writeTime) && File.Exists(path) && File.GetLastWriteTimeUtc(path) == writeTime;

    private static bool IsMatch(string relativePath, IReadOnlyList<string> globs)
    {
        var path = relativePath.Replace('\\', '/');
        foreach (var glob in globs)
        {
            var pattern = glob.Replace('\\', '/');
            var left = path.Split('/');
            var right = pattern.Split('/');
            if (left.Length != right.Length)
                continue;
            var matched = true;
            for (var i = 0; i < left.Length; i++)
            {
                if (!SegmentMatches(left[i], right[i]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return true;
        }

        return false;
    }

    private static bool SegmentMatches(string text, string pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;
        var star = -1;
        var mark = 0;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '*' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(text[textIndex])))
            {
                if (pattern[patternIndex] == '*')
                {
                    star = patternIndex++;
                    mark = textIndex;
                }
                else
                {
                    patternIndex++;
                    textIndex++;
                }
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                textIndex = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;
        return patternIndex == pattern.Length;
    }

    private sealed record Pending(DateTime FirstEvent, CancellationTokenSource Cancel);

    private readonly record struct WorkItem(string Path, DateTime FirstEvent, int Attempt);
}
