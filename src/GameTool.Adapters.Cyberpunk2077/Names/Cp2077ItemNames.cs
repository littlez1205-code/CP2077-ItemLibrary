using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Names;

/// <summary>
/// 名字表（懒加载）。没有 REDmod 时不抛错。同一份数据也提供生成功能的目录和图标。
/// 游戏目录由调用方给出（D020：桌面端来自用户设置，CLI 来自 --game 或 Steam 探测）；进程内只缓存最近一个目录的实例。
/// </summary>
public sealed class Cp2077ItemNames : IItemNameProvider, IGearCatalog
{
    private static readonly Lock Gate = new();
    private static string? _sharedKey;
    private static Lazy<Cp2077ItemNames>? _shared;

    private readonly ItemNameCatalog? _catalog;

    private readonly Lazy<Cp2077ItemIcons> _icons;

    private Cp2077ItemNames(ItemNameCatalog? catalog, string? gameDirectory)
    {
        _catalog = catalog;
        GameDirectory = gameDirectory;
        _icons = new Lazy<Cp2077ItemIcons>(() => new Cp2077ItemIcons(() => IconResolver, GameDirectory));
    }

    /// <summary>
    /// 指定游戏目录的共享实例：同一个目录只加载一次；换目录后丢掉旧实例，下次请求按新目录重新加载。
    /// null 或空表示没有设置游戏目录（名字表不可用）。
    /// </summary>
    public static Cp2077ItemNames ForGame(string? gameDirectory)
    {
        var key = string.IsNullOrWhiteSpace(gameDirectory) ? null : gameDirectory;
        Lazy<Cp2077ItemNames> lazy;
        lock (Gate)
        {
            if (_shared is null || !string.Equals(_sharedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _sharedKey = key;
                _shared = new Lazy<Cp2077ItemNames>(() => Create(key), LazyThreadSafetyMode.ExecutionAndPublication);
            }

            lazy = _shared;
        }

        return lazy.Value;
    }

    /// <summary>用现成的名字表构造（测试或自定义游戏目录）。null 表示不可用。gameDirectory 给图标用，可空。</summary>
    public static Cp2077ItemNames FromCatalog(ItemNameCatalog? catalog, string? gameDirectory = null) => new(catalog, gameDirectory);

    /// <summary>名字表来自的游戏目录（只读）。</summary>
    public string? GameDirectory { get; }

    /// <summary>记录 → UIIcon 解析器；名字表不可用时为 null。</summary>
    public ItemIconResolver? IconResolver => Available ? _catalog!.IconResolver : null;

    /// <summary>物品图标（懒加载，第一次请求才打开归档）。</summary>
    public Cp2077ItemIcons Icons => _icons.Value;

    public bool Available => _catalog?.Available == true;

    public string? UnavailableMessage => Available ? null : ItemNameCatalog.RedmodUnavailable;

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

    private static Cp2077ItemNames Create(string? game)
    {
        if (string.IsNullOrWhiteSpace(game))
            return new Cp2077ItemNames(null, null);
        try
        {
            return new Cp2077ItemNames(ItemNameCatalog.Load(game), game);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // 名字表只影响显示；读不出来时按不可用处理，复制功能照常。
            return new Cp2077ItemNames(null, null);
        }
    }
}
