using System.Text;
using System.Text.Json;
using GameTool.Core;

namespace GameTool.Tests.Fake;

public sealed class FakeJsonGameAdapter : IGameAdapter, IInsertCapable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _saveRoot;

    public FakeJsonGameAdapter(string saveRoot, string catalogJson)
    {
        _saveRoot = saveRoot;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        Resources = ResourceCatalog.Load(stream);
    }

    public bool AllowInsert { get; set; }
    public bool CorruptWrites { get; set; }
    public bool CanInsert(ResourceId id) => AllowInsert && Resources.Any(resource => resource.Id == id);
    public bool RewriteOriginalOnSave { get; set; }
    public const string RewrittenMarker = "{\"gold\":7}";
    public string GameId => "fake";
    public string DisplayName => "Fake JSON Game";
    public string DefaultSaveRoot => _saveRoot;
    public SaveWatchSpec WatchSpec => new(_saveRoot, ["save.json"], 150);
    public IReadOnlyList<ResourceDescriptor> Resources { get; }

    public IEnumerable<SaveSlotInfo> DiscoverSaves(string saveRoot)
    {
        if (!Directory.Exists(saveRoot))
            return [];
        var slots = new List<SaveSlotInfo>();
        var file = Path.Combine(saveRoot, "save.json");
        if (File.Exists(file))
        {
            slots.Add(new SaveSlotInfo(saveRoot, file, "save", File.GetLastWriteTimeUtc(file), "manual"));
        }
        foreach (var directory in Directory.EnumerateDirectories(saveRoot))
        {
            var nested = Path.Combine(directory, "save.json");
            if (!File.Exists(nested))
                continue;
            slots.Add(new SaveSlotInfo(
                directory,
                nested,
                Path.GetFileName(directory),
                File.GetLastWriteTimeUtc(nested),
                "manual"));
        }

        return slots;
    }

    public bool IsComplete(string saveFilePath) => File.Exists(saveFilePath);

    public ISaveDocument Load(string saveFilePath) =>
        FakeJsonSaveDocument.Load(saveFilePath, Resources, CorruptWrites, RewriteOriginalOnSave, CanInsert);

    public static string Catalog(params (string Id, long Max)[] resources)
    {
        var rows = resources.Select(resource => new
        {
            id = resource.Id,
            name = new { zh = resource.Id, en = resource.Id },
            group = "item",
            max = resource.Max,
            defaultFill = 1,
            locator = "json:" + resource.Id
        });
        return JsonSerializer.Serialize(rows, JsonOptions);
    }
}

internal sealed class FakeJsonSaveDocument : ISaveDocument, IStackInspector, ISaveDiagnostics
{
    private readonly Dictionary<string, long[]> _values;
    private readonly IReadOnlyList<ResourceDescriptor> _resources;
    private readonly string _path;
    private readonly bool _corrupt;
    private readonly bool _rewriteOriginal;
    private readonly IReadOnlyList<DiagnosticField> _diagnostics;
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    private readonly Func<ResourceId, bool> _canInsert;

    private FakeJsonSaveDocument(
        string path,
        Dictionary<string, long[]> values,
        IReadOnlyList<ResourceDescriptor> resources,
        bool corrupt,
        bool rewriteOriginal,
        IReadOnlyList<DiagnosticField> diagnostics,
        Func<ResourceId, bool> canInsert)
    {
        _path = path;
        _values = values;
        _resources = resources;
        _corrupt = corrupt;
        _rewriteOriginal = rewriteOriginal;
        _diagnostics = diagnostics;
        _canInsert = canInsert;
        Version = new SaveVersionInfo(1, 1);
    }

    public IReadOnlyList<DiagnosticField> Fields => _diagnostics;

    public SaveVersionInfo Version { get; }

    public static FakeJsonSaveDocument Load(
        string path,
        IReadOnlyList<ResourceDescriptor> resources,
        bool corrupt,
        bool rewriteOriginal,
        Func<ResourceId, bool>? canInsert = null)
    {
        using var stream = File.OpenRead(path);
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream)
            ?? throw new InvalidDataException("假存档不是 JSON 对象。");
        if (root.TryGetValue("badVersion", out var bad) && bad.ValueKind == JsonValueKind.True)
            throw new UnsupportedSaveException(0, 0, "假游戏版本不受支持。");
        var diagnostics = new List<DiagnosticField>();
        if (root.TryGetValue("_diagnostics", out var diag))
        {
            if (diag.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("假存档 _diagnostics 必须是对象。");
            foreach (var field in diag.EnumerateObject())
                diagnostics.Add(new DiagnosticField(field.Name, field.Value.GetInt64()));
        }

        var values = new Dictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var pair in root)
        {
            if (pair.Key is "badVersion" or "_diagnostics")
                continue;
            values[pair.Key] = pair.Value.ValueKind switch
            {
                JsonValueKind.Null => [],
                JsonValueKind.Number => [pair.Value.GetInt64()],
                JsonValueKind.Array => pair.Value.EnumerateArray().Select(item => item.GetInt64()).ToArray(),
                _ => throw new InvalidDataException($"假存档字段 {pair.Key} 不是数量。")
            };
        }

        return new FakeJsonSaveDocument(path, values, resources, corrupt, rewriteOriginal, diagnostics, canInsert ?? (_ => false));
    }

    public IReadOnlyList<long> GetStacks(ResourceId id)
    {
        var key = Key(id);
        if (!_values.TryGetValue(key, out var stacks) || stacks.Length == 0)
            return [];
        return stacks.ToArray();
    }

    public long? GetQuantity(ResourceId id)
    {
        var key = Key(id);
        if (!_values.TryGetValue(key, out var stacks) || stacks.Length == 0)
            return null;
        if (stacks.Length > 1)
            throw new AmbiguousResourceException(id);
        return stacks[0];
    }

    public IReadOnlyList<ResourceChangeResult> Apply(IReadOnlyList<ResourceChange> changes)
    {
        var results = new List<ResourceChangeResult>(changes.Count);
        foreach (var change in changes)
        {
            var key = Key(change.Id);
            if (!_values.TryGetValue(key, out var stacks) || stacks.Length == 0)
            {
                if (_canInsert(change.Id))
                {
                    _values[key] = [change.TargetQuantity];
                    _changed.Add(key);
                    results.Add(new ResourceChangeResult(change.Id, null, change.TargetQuantity, ChangeOutcome.Inserted));
                }
                else
                    results.Add(new ResourceChangeResult(change.Id, null, null, ChangeOutcome.NotHeld));
                continue;
            }

            if (stacks.Length > 1)
            {
                results.Add(new ResourceChangeResult(change.Id, null, null, ChangeOutcome.Ambiguous));
                continue;
            }

            if (stacks[0] == change.TargetQuantity)
            {
                results.Add(new ResourceChangeResult(change.Id, stacks[0], stacks[0], ChangeOutcome.Unchanged));
                continue;
            }

            var previous = stacks[0];
            _values[key] = [change.TargetQuantity];
            _changed.Add(key);
            results.Add(new ResourceChangeResult(change.Id, previous, change.TargetQuantity, ChangeOutcome.Changed));
        }

        return results;
    }

    public void Save(Stream output)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in _values)
        {
            var numbers = pair.Value.ToArray();
            if (_corrupt && _changed.Contains(pair.Key) && numbers.Length == 1)
                numbers[0]++;
            payload[pair.Key] = numbers.Length switch
            {
                0 => null,
                1 => numbers[0],
                _ => numbers
            };
        }

        JsonSerializer.Serialize(output, payload, JsonOptions);
        if (_rewriteOriginal)
            File.WriteAllText(_path, FakeJsonGameAdapter.RewrittenMarker);
    }

    private static readonly JsonSerializerOptions JsonOptions = new();

    private string Key(ResourceId id)
    {
        var resource = _resources.FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException(id.Value);
        const string prefix = "json:";
        if (!resource.Locator.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"假游戏 locator 必须以 json: 开头：{resource.Locator}");
        return resource.Locator[prefix.Length..];
    }
}
