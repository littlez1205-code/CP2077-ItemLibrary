using GameTool.Core;

namespace GameTool.App;

public sealed class WatchService : IAsyncDisposable
{
    private readonly SaveWatcher _watcher;

    public WatchService(
        IGameAdapter adapter,
        string root,
        IReadOnlyList<string> includeGlobs,
        IReadOnlyList<ResourceChange> changes,
        int debounceMilliseconds,
        Action<WatchEvent> onEvent,
        PatchOptions? patchOptions = null)
    {
        _watcher = new SaveWatcher(adapter, root, includeGlobs, changes, debounceMilliseconds, onEvent, patchOptions);
    }

    public void Start() => _watcher.Start();

    public void NoteOwnWrite(string path, DateTime lastWriteTimeUtc) =>
        _watcher.NoteOwnWrite(path, lastWriteTimeUtc);

    public ValueTask DisposeAsync() => _watcher.DisposeAsync();
}
