namespace GameTool.Core;

public sealed class AdapterRegistry
{
    private readonly Dictionary<string, IGameAdapter> _byId = new(StringComparer.Ordinal);

    public void Register(IGameAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!_byId.TryAdd(adapter.GameId, adapter))
            throw new InvalidOperationException($"Adapter '{adapter.GameId}' is already registered.");
    }

    public IGameAdapter GetRequired(string gameId)
    {
        if (_byId.TryGetValue(gameId, out var adapter))
            return adapter;
        throw new KeyNotFoundException($"No adapter registered for '{gameId}'.");
    }

    public IReadOnlyCollection<IGameAdapter> All => _byId.Values;
}
