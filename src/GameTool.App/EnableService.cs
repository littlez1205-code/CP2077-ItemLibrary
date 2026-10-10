using GameTool.Core;

namespace GameTool.App;

public sealed record EnableStatus(
    string? Slot,
    string? SlotName,
    IReadOnlyList<ResourceChange> Changes,
    string? LastOutcome,
    long? LastElapsedMilliseconds,
    DateTime? LastAtUtc);

public sealed record EnableNotice(string Event, string? Slot, string? Message, long? ElapsedMilliseconds);

public sealed class EnableService : IAsyncDisposable
{
    private static readonly PatchOptions NoBackup = new(null);
    private readonly BackupStore _backups;
    private readonly Action<EnableNotice> _onEvent;
    private readonly object _gate = new();
    private WatchService? _watch;
    private IGameAdapter? _adapter;
    private string? _saveRoot;
    private string? _slot;
    private string? _slotName;
    private List<ResourceChange> _changes = [];
    private string? _lastOutcome;
    private long? _lastElapsed;
    private DateTime? _lastAt;
    private int _generation;

    public EnableService(BackupStore backups, Action<EnableNotice> onEvent)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(onEvent);
        _backups = backups;
        _onEvent = onEvent;
    }

    public EnableStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new EnableStatus(_slot, _slotName, _changes.ToArray(), _lastOutcome, _lastElapsed, _lastAt);
            }
        }
    }

    public bool IsEnabled(string saveFilePath)
    {
        lock (_gate)
        {
            return _slot is not null
                && Path.GetFullPath(saveFilePath).Equals(_slot, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? Start(
        IGameAdapter adapter,
        string saveFilePath,
        IReadOnlyList<ResourceChange> changes,
        bool backupFirst,
        string saveRoot)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            throw new PatchRejectedException("没有改动");

        StopCurrent(notify: true);
        string? backupId = null;
        if (backupFirst)
            backupId = _backups.Add(adapter, Path.GetDirectoryName(saveFilePath)!).Id;

        var report = PatchEngine.Patch(adapter, saveFilePath, changes, NoBackup);
        var slotName = Path.GetFileName(Path.GetDirectoryName(saveFilePath) ?? saveFilePath);
        lock (_gate)
        {
            _adapter = adapter;
            _saveRoot = saveRoot;
            _slot = Path.GetFullPath(saveFilePath);
            _slotName = slotName;
            _changes = changes.ToArray().ToList();
            Remember(report);
        }

        StartWatch();
        NoteOwnWrite(saveFilePath, report.WrittenLastWriteTimeUtc);
        _onEvent(new EnableNotice("enabled", saveFilePath, backupId, report.ElapsedMilliseconds));
        Publish(report, saveFilePath);
        try
        {
            CatchUp(saveFilePath);
        }
        catch
        {
            StopCurrent(notify: false);
            throw;
        }

        return backupId;
    }

    /// <summary>
    /// 用传入列表整体替换当前目标。调用方必须带上仍然要保持的旧目标，这里不会与旧列表合并。
    /// </summary>
    public void Update(IReadOnlyList<ResourceChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            throw new PatchRejectedException("没有改动");
        IGameAdapter adapter;
        string slot;
        lock (_gate)
        {
            if (_adapter is null || _slot is null)
                throw new PatchRejectedException("当前没有使能。");
            adapter = _adapter;
            slot = _slot;
        }

        var report = PatchEngine.Patch(adapter, slot, changes, NoBackup);
        lock (_gate)
        {
            _changes = changes.ToArray().ToList();
            Remember(report);
        }

        RestartWatch();
        NoteOwnWrite(slot, report.WrittenLastWriteTimeUtc);
        Publish(report, slot);
        CatchUp(slot);
    }

    public void AcceptOwnWrite(string slot, DateTime? written)
    {
        if (!IsEnabled(slot))
            return;
        NoteOwnWrite(slot, written);
        CatchUp(slot);
    }

    private void NoteOwnWrite(string slot, DateTime? written)
    {
        if (written is not DateTime value)
            return;
        WatchService? watch;
        lock (_gate)
            watch = _watch;
        watch?.NoteOwnWrite(slot, value);
    }

    private void CatchUp(string slot)
    {
        IGameAdapter adapter;
        IReadOnlyList<ResourceChange> changes;
        lock (_gate)
        {
            if (_adapter is null)
                return;
            adapter = _adapter;
            changes = _changes.ToArray();
        }

        try
        {
            var report = PatchEngine.Patch(adapter, slot, changes, NoBackup);
            NoteOwnWrite(slot, report.WrittenLastWriteTimeUtc);
            lock (_gate)
                Remember(report);
            if (report.Outcome == PatchOutcome.Patched)
                Publish(report, slot);
        }
        catch (SaveChangedDuringPatchException)
        {
        }
    }

    public void Stop() => StopCurrent(notify: true);

    public async ValueTask DisposeAsync() => await StopQuietAsync();

    private void StartWatch()
    {
        WatchService watch;
        lock (_gate)
        {
            if (_adapter is null || _saveRoot is null || _slot is null)
                return;
            var relative = Path.GetRelativePath(_saveRoot, _slot).Replace('\\', '/');
            var generation = ++_generation;
            watch = new WatchService(
                _adapter,
                _saveRoot,
                [relative],
                _changes.ToArray(),
                _adapter.WatchSpec.DebounceMilliseconds,
                onEvent => OnWatch(generation, onEvent),
                NoBackup);
            _watch = watch;
        }

        watch.Start();
    }

    private void RestartWatch()
    {
        WatchService? previous;
        lock (_gate)
        {
            previous = _watch;
            _watch = null;
        }

        previous?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        StartWatch();
    }

    private void OnWatch(int generation, WatchEvent watchEvent)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;
            _lastOutcome = watchEvent.Outcome;
            _lastElapsed = watchEvent.WriteCompleteToDoneMs;
            _lastAt = DateTime.UtcNow;
        }

        _onEvent(new EnableNotice(
            watchEvent.Outcome,
            watchEvent.Path,
            watchEvent.Error,
            watchEvent.WriteCompleteToDoneMs));
    }

    private void Publish(PatchReport report, string slot)
    {
        _onEvent(new EnableNotice(Surface(report), slot, null, report.ElapsedMilliseconds));
    }

    private void Remember(PatchReport report)
    {
        _lastOutcome = Surface(report);
        _lastElapsed = report.ElapsedMilliseconds;
        _lastAt = DateTime.UtcNow;
    }

    private static string Surface(PatchReport report)
    {
        if (report.Outcome == PatchOutcome.Patched
            && report.Results.Any(result => result.Outcome == ChangeOutcome.Inserted)
            && report.Results.All(result => result.Outcome != ChangeOutcome.Changed))
            return ChangeOutcome.Inserted;
        return report.Outcome;
    }

    private void StopCurrent(bool notify)
    {
        WatchService? watch;
        string? slot;
        lock (_gate)
        {
            watch = _watch;
            _watch = null;
            slot = _slot;
            _generation++;
            _adapter = null;
            _saveRoot = null;
            _slot = null;
            _slotName = null;
            _changes = [];
        }

        watch?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (notify && slot is not null)
            _onEvent(new EnableNotice("disabled", slot, null, null));
    }

    private async Task StopQuietAsync()
    {
        WatchService? watch;
        lock (_gate)
        {
            watch = _watch;
            _watch = null;
            _generation++;
            _adapter = null;
            _slot = null;
        }

        if (watch is not null)
            await watch.DisposeAsync();
    }
}
