using System.Reflection;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077;

public sealed class Cp2077Adapter : IGameAdapter, ISaveSupportProbe, IThemeProvider, IResearchProvider, ITweakDbIdSource, IInsertCapable, IResourceExtensible, IItemIconProvider, IGamePathSetup
{
    public const string Game = "cp2077";
    private const int DebounceMilliseconds = 150;
    private const string ResourceManifest = "GameTool.Adapters.Cyberpunk2077.resources.json";
    private const string ThemeManifest = "GameTool.Adapters.Cyberpunk2077.theme.json";

    private readonly Func<Cp2077ItemNames> _names;
    private readonly GameDirectoryHolder _game;

    /// <summary>
    /// 名字表、图标按 <see cref="UseGameDirectory"/> 设置的游戏目录加载（D020）；还没设置时名字表不可用。
    /// 桌面端从用户设置传入，CLI 从 --game 或 Steam 探测传入。
    /// </summary>
    public Cp2077Adapter()
    {
        _game = new GameDirectoryHolder();
        var game = _game;
        _names = () => Cp2077ItemNames.ForGame(game.Value);
        Resources = LoadResources();
    }

    /// <summary>names 提供名字表和可生成目录（测试用）；此时 <see cref="UseGameDirectory"/> 只记录目录，不影响 names。</summary>
    public Cp2077Adapter(Func<Cp2077ItemNames> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _names = names;
        _game = new GameDirectoryHolder();
        Resources = LoadResources();
    }

    private static IReadOnlyList<ResourceDescriptor> LoadResources()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceManifest)
            ?? throw new InvalidOperationException($"缺少内嵌资源 {ResourceManifest}。");
        return ResourceCatalog.Load(stream);
    }

    private Cp2077Adapter(Func<Cp2077ItemNames> names, GameDirectoryHolder game, IReadOnlyList<ResourceDescriptor> resources)
    {
        _names = names;
        _game = game;
        Resources = resources;
    }

    public string GameId => Game;
    public string DisplayName => "Cyberpunk 2077";

    /// <summary>常见的存档位置。CLI 默认用它；桌面端只把它当作设置页的提示（D020）。</summary>
    public string DefaultSaveRoot => Cp2077Paths.DefaultSaveRoot;

    public string SaveRootHint => Cp2077Paths.DefaultSaveRoot;

    public string GameDirectoryHint => Cp2077Paths.GameDirectoryHint;

    public string? GameDirectory => _game.Value;

    public PathCheck CheckSaveRoot(string? path) => Cp2077Paths.CheckSaveRoot(path);

    public PathCheck CheckGameDirectory(string? path) => Cp2077Paths.CheckGameDirectory(path);

    public void UseGameDirectory(string? gameDirectory) =>
        _game.Value = string.IsNullOrWhiteSpace(gameDirectory) ? null : gameDirectory;

    public SaveWatchSpec WatchSpec => new(DefaultSaveRoot, ["QuickSave-*/sav.dat"], DebounceMilliseconds);

    public IReadOnlyList<ResourceDescriptor> Resources { get; }

    public IEnumerable<SaveSlotInfo> DiscoverSaves(string saveRoot)
    {
        if (!Directory.Exists(saveRoot))
            return [];

        var slots = new List<SaveSlotInfo>();
        foreach (var directory in Directory.EnumerateDirectories(saveRoot))
        {
            var saveFile = Path.Combine(directory, "sav.dat");
            if (!File.Exists(saveFile))
                continue;

            var info = new FileInfo(saveFile);
            var name = Path.GetFileName(directory);
            slots.Add(new SaveSlotInfo(
                directory,
                saveFile,
                name,
                info.LastWriteTimeUtc,
                GroupOf(name)));
        }

        return slots
            .OrderByDescending(slot => slot.LastWriteTimeUtc)
            .ThenBy(slot => slot.SlotName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GroupOf(string slotName)
    {
        if (slotName.StartsWith("QuickSave-", StringComparison.OrdinalIgnoreCase))
            return "quick";
        if (slotName.StartsWith("ManualSave-", StringComparison.OrdinalIgnoreCase))
            return "manual";
        if (slotName.StartsWith("AutoSave-", StringComparison.OrdinalIgnoreCase))
            return "auto";
        return "other";
    }

    public Stream? OpenTheme() => Assembly.GetExecutingAssembly().GetManifestResourceStream(ThemeManifest);

    /// <summary>图标来自名字表同一份游戏目录；没有游戏或名字表时为 null，界面用占位图。</summary>
    public string? IconVersion => _names().Icons.IconVersion;

    public byte[]? TryGetIconPng(string recordName, IconGender gender) => _names().Icons.TryGetIconPng(recordName, gender);

    public ulong TweakId(string name) => TweakDbId.FromName(name);

    public ResearchImage OpenForResearch(string saveFilePath) => ToImage(SaveContainer.Read(saveFilePath));

    internal static ResearchImage ToImage(SaveContainer container)
    {
        var nodes = NodeTable.Parse(container.CopyNodeTable());
        var parents = new int?[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var child = nodes[i].Child;
            var guard = 0;
            while (child >= 0 && child < nodes.Count && guard++ <= nodes.Count)
            {
                parents[child] ??= i;
                child = nodes[child].Next;
            }
        }

        var regions = new ResearchRegion[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            regions[i] = new ResearchRegion(node.Index, node.Name, node.PayloadOffset, node.Length, parents[i]);
        }

        ResearchField[] header =
        [
            new("saveVersion", container.SaveVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("gameVersion", container.GameVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("timestamp", container.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("archiveVersion", container.ArchiveVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("chunkCount", container.ChunkCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("decompressedLength", container.DecompressedLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("nodeCount", nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ];
        return new ResearchImage(container.Payload.ToArray(), header, regions);
    }

    public SaveSupport Probe(string saveFilePath)
    {
        try
        {
            using var stream = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[12];
            if (stream.Read(head, 0, head.Length) != head.Length || !head.AsSpan(0, 4).SequenceEqual(Cp2077Format.Vasc))
                return new SaveSupport(false, "不是可识别的存档。");
            var saveVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
            var gameVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
            if (saveVersion == Cp2077Format.SupportedSaveVersion && gameVersion == Cp2077Format.SupportedGameVersion)
                return new SaveSupport(true, null);
            // 比 2.31 新的存档（游戏更新后）不能靠重新存档解决，只能等工具适配（D024）。
            if (saveVersion > Cp2077Format.SupportedSaveVersion || gameVersion > Cp2077Format.SupportedGameVersion)
                return new SaveSupport(
                    false,
                    $"游戏 {FormatGameVersion(gameVersion)} 版本的存档（存档格式 {saveVersion}），比本工具支持的 2.31 更新；需要等待工具适配新版本");
            return new SaveSupport(
                false,
                $"游戏 {FormatGameVersion(gameVersion)} 版本的存档（存档格式 {saveVersion}），本工具只支持 2.31；在游戏里读取后重新存档即可升级");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveSupport(false, "暂时无法读取存档头。");
        }
    }

    internal static string FormatGameVersion(uint gameVersion)
    {
        var major = gameVersion / 1000;
        var minor = gameVersion % 1000;
        var minorText = minor % 100 == 0
            ? (minor / 100).ToString()
            : minor % 10 == 0
                ? (minor / 10).ToString()
                : minor.ToString();
        return major + "." + minorText;
    }

    public bool IsComplete(string saveFilePath)
    {
        try
        {
            using var stream = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < Cp2077Format.FirstChunkOffset + 8)
                return false;

            var head = new byte[4];
            if (stream.Read(head, 0, 4) != 4 || !head.AsSpan().SequenceEqual(Cp2077Format.Vasc))
                return false;

            stream.Position = stream.Length - 4;
            var tail = new byte[4];
            return stream.Read(tail, 0, 4) == 4 && tail.AsSpan().SequenceEqual(Cp2077Format.Done);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool CanInsert(ResourceId id) => Resources.Any(resource => resource.Id == id);

    /// <summary>追加的资源 id 和 locator 都不能与已有的重复，locator 必须是 tdb:记录名。</summary>
    public IGameAdapter WithResources(IReadOnlyList<ResourceDescriptor> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        var merged = Resources.ToList();
        foreach (var resource in extra)
        {
            if (!resource.Locator.StartsWith("tdb:", StringComparison.Ordinal) || resource.Locator.Length == 4)
                throw new InvalidDataException($"资源 {resource.Id.Value} 的 locator 必须是 tdb:记录名。");
            if (merged.Any(item => item.Id == resource.Id || item.Locator == resource.Locator))
                throw new InvalidDataException($"资源 {resource.Id.Value} 与已有资源重复。");
            merged.Add(resource);
        }

        return new Cp2077Adapter(_names, _game, merged);
    }

    public ISaveDocument Load(string saveFilePath)
    {
        return Cp2077SaveDocument.Open(SaveContainer.Read(saveFilePath), Resources, CanInsert, _names, _names);
    }

    /// <summary>同一个适配器及其 <see cref="WithResources"/> 副本共用的游戏目录。</summary>
    private sealed class GameDirectoryHolder
    {
        private volatile string? _value;

        public string? Value
        {
            get => _value;
            set => _value = value;
        }
    }
}
