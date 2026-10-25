using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Names;

/// <summary>
/// 名字表（懒加载）。没有 REDmod 时不抛错。同一份数据也提供生成功能的目录和图标。
/// 游戏目录由调用方给出（D020：桌面端来自用户设置，CLI 来自 --game 或 Steam 探测）；进程内只缓存最近一个目录的实例。
/// </summary>
public sealed class Cp2077ItemNames : IItemNameProvider, IGearCatalog
{
    private sealed class GameLangCache(
        Lazy<(TweakSourceIndex? Index, long ScanMs)>? index,
        Lazy<ItemIconResolver?>? iconResolver,
        Lazy<Cp2077ItemIcons>? itemIcons)
    {
        public Lazy<(TweakSourceIndex? Index, long ScanMs)>? Index { get; } = index;
        public Lazy<ItemIconResolver?>? IconResolver { get; } = iconResolver;
        public Lazy<Cp2077ItemIcons>? ItemIcons { get; } = itemIcons;
        public Dictionary<string, Lazy<Cp2077ItemNames>> ByLang { get; } = new(StringComparer.Ordinal);
    }

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, GameLangCache> CacheByGame = new(StringComparer.OrdinalIgnoreCase);

    private readonly ItemNameCatalog? _catalog;
    private readonly string? _diagnosticReason;

    private readonly Lazy<Cp2077ItemIcons> _icons;

    private Cp2077ItemNames(ItemNameCatalog? catalog, string? gameDirectory, string? diagnosticReason = null, Lazy<Cp2077ItemIcons>? sharedIcons = null)
    {
        _catalog = catalog;
        _diagnosticReason = diagnosticReason;
        GameDirectory = gameDirectory;
        _icons = sharedIcons ?? new Lazy<Cp2077ItemIcons>(() => new Cp2077ItemIcons(() => IconResolver, GameDirectory));
    }

    /// <summary>
    /// 指定游戏目录和语言的共享实例：同一个目录的 tweak 索引和图标缓存只加载一次；切换语言时直接复用索引并按语言缓存；
    /// null 或空表示没有设置游戏目录（名字表不可用）。
    /// </summary>
    public static Cp2077ItemNames ForGame(string? gameDirectory, string? lang = null)
    {
        var key = string.IsNullOrWhiteSpace(gameDirectory) ? null : gameDirectory;
        var cacheKey = key ?? "";
        var normLang = GearKinds.NormalizeLang(lang);
        Lazy<Cp2077ItemNames> lazy;
        lock (Gate)
        {
            if (!CacheByGame.TryGetValue(cacheKey, out var gameCache))
            {
                if (CacheByGame.Count >= 8)
                    CacheByGame.Clear();
                if (key is not null)
                {
                    var game = key;
                    var idxLazy = new Lazy<(TweakSourceIndex? Index, long ScanMs)>(() => LoadIndex(game), LazyThreadSafetyMode.ExecutionAndPublication);
                    var resolverLazy = new Lazy<ItemIconResolver?>(() => idxLazy.Value.Index is { } idx ? ItemIconResolver.Build(idx) : null, LazyThreadSafetyMode.ExecutionAndPublication);
                    var iconsLazy = new Lazy<Cp2077ItemIcons>(() => new Cp2077ItemIcons(() => resolverLazy.Value, game), LazyThreadSafetyMode.ExecutionAndPublication);
                    gameCache = new GameLangCache(idxLazy, resolverLazy, iconsLazy);
                }
                else
                {
                    gameCache = new GameLangCache(null, null, null);
                }

                CacheByGame[cacheKey] = gameCache;
            }

            if (!gameCache.ByLang.TryGetValue(normLang, out lazy!))
            {
                var idxLazy = gameCache.Index;
                var resolverLazy = gameCache.IconResolver;
                var iconsLazy = gameCache.ItemIcons;
                lazy = new Lazy<Cp2077ItemNames>(() => Create(key, normLang, idxLazy, resolverLazy, iconsLazy), LazyThreadSafetyMode.ExecutionAndPublication);
                gameCache.ByLang[normLang] = lazy;
            }
        }

        return lazy.Value;
    }

    /// <summary>用现成的名字表构造（测试或自定义游戏目录）。null 表示不可用。gameDirectory 给图标用，可空。</summary>
    public static Cp2077ItemNames FromCatalog(ItemNameCatalog? catalog, string? gameDirectory = null) => new(catalog, gameDirectory);

    /// <summary>名字表来自的游戏目录（只读）。</summary>
    public string? GameDirectory { get; }

    /// <summary>当前目录使用的语言代码（zh-cn / zh-tw / en-us）。</summary>
    public string Language => _catalog?.Language ?? "zh-cn";

    /// <summary>记录 → UIIcon 解析器；名字表不可用时为 null。</summary>
    public ItemIconResolver? IconResolver => Available ? _catalog!.IconResolver : null;

    /// <summary>物品图标（懒加载，第一次请求才打开归档）。</summary>
    public Cp2077ItemIcons Icons => _icons.Value;

    public bool Available => _catalog?.Available == true;

    public string? UnavailableMessage => Available
        ? null
        : string.IsNullOrWhiteSpace(_diagnosticReason)
            ? ItemNameCatalog.RedmodUnavailable
            : ItemNameCatalog.RedmodUnavailable + "（诊断详情：" + _diagnosticReason + "）";

    public IReadOnlyList<GearEntry> Gear => Available ? _catalog!.Gear?.Entries ?? [] : [];

    public IReadOnlyList<GearEntry> Stackables => Available ? _catalog!.Gear?.Stackables ?? [] : [];

    public IReadOnlyList<GearGroup> Groups => Available ? _catalog!.Gear?.Groups ?? [] : [];

    public string? KindOf(ulong tweakDbId) => Available ? _catalog!.Gear?.KindOf(tweakDbId) : null;

    /// <summary>按装了往日之影加载（D023）；名字表不可用时为 true。</summary>
    public bool ExpansionInstalled => _catalog?.ExpansionInstalled ?? true;

    public IReadOnlyList<GearGroup> HiddenGroups => Available ? _catalog!.Gear?.HiddenGroups ?? [] : [];

    public bool IsHidden(string recordName) => Available && _catalog!.Gear?.IsHidden(recordName) == true;

    public IReadOnlyList<TweakPresetPart>? PresetParts(string recordName) => _catalog?.PresetParts(recordName);

    public ItemLabel Resolve(string tweakDbIdHex)
    {
        if (_catalog is null || !Available)
            return new ItemLabel(tweakDbIdHex, null);
        var name = _catalog.Resolve(tweakDbIdHex);
        return new ItemLabel(name.Label, name.RecordName);
    }

    public bool IsInternal(string? recordName) => ItemNameCatalog.IsInternalRecord(recordName);

    public string? ResolveText(string? key) => Available ? _catalog?.ResolveText(key) : null;

    private static (TweakSourceIndex? Index, long ScanMs) LoadIndex(string game)
    {
        var tweaks = Path.Combine(game, "tools", "redmod", "tweaks");
        var expansion = Cp2077Paths.HasPhantomLiberty(game);
        var scan = System.Diagnostics.Stopwatch.StartNew();
        var index = TweakSourceIndex.TryLoad(tweaks, expansion, out _);
        scan.Stop();
        return (index, scan.ElapsedMilliseconds);
    }

    private static Cp2077ItemNames Create(
        string? game,
        string lang,
        Lazy<(TweakSourceIndex? Index, long ScanMs)>? sharedIndex,
        Lazy<ItemIconResolver?>? sharedResolver,
        Lazy<Cp2077ItemIcons>? sharedIcons)
    {
        if (string.IsNullOrWhiteSpace(game))
            return new Cp2077ItemNames(null, null);
        try
        {
            var (index, scanMs) = sharedIndex is not null ? sharedIndex.Value : LoadIndex(game);
            var catalog = ItemNameCatalog.LoadFromIndex(game, index, scanMs, lang, sharedIcons: sharedResolver);
            string? reason = null;
            if (!catalog.Available)
            {
                var tweaksDir = Path.Combine(game, Cp2077Paths.TweaksRelative);
                reason = Directory.Exists(tweaksDir)
                    ? "tools\\redmod\\tweaks 文件夹内没有找到 .tweak 数据文件，请确认 REDmod DLC 已完整下载"
                    : null;
            }
            return new Cp2077ItemNames(catalog, game, reason, sharedIcons);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // 名字表只影响显示；读不出来时按不可用处理，复制功能照常，但保留具体诊断原因方便排查。
            return new Cp2077ItemNames(null, game, ex.Message, sharedIcons);
        }
    }
}
