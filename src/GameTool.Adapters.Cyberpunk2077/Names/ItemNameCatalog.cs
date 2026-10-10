using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Names;

public sealed record ItemName(string Label, string? RecordName, string? DisplayName, string? Text, bool Resolved);

public readonly record struct ParsedNameKey(bool IsId, ulong Id, string Name);

public sealed class ItemNameCatalog
{
    public const string RedmodUnavailable = "名字表不可用，需要安装官方免费 DLC REDmod（Steam / GOG / Epic 均可获取）。";

    private readonly TweakSourceIndex? _index;
    private readonly Dictionary<ulong, string> _byKey = [];
    private readonly Dictionary<string, string> _bySecondary = new(StringComparer.Ordinal);

    private readonly Lazy<GearCatalog?> _gear;
    private readonly Lazy<ItemIconResolver?> _icons;

    private ItemNameCatalog(TweakSourceIndex? index, bool available, long tweakScanMilliseconds, long localizationMilliseconds)
    {
        _index = index;
        _icons = new Lazy<ItemIconResolver?>(() => _index is null ? null : ItemIconResolver.Build(_index));
        _gear = new Lazy<GearCatalog?>(() => Available && _index is not null ? GearCatalog.Build(_index, Resolve, IsInternalRecord, HasIcon) : null);
        Available = available;
        TweakScanMilliseconds = tweakScanMilliseconds;
        LocalizationMilliseconds = localizationMilliseconds;
    }

    public bool Available { get; }

    public long TweakScanMilliseconds { get; }

    public long LocalizationMilliseconds { get; }

    public int Overridden => _index?.Overridden ?? 0;

    /// <summary>按装了往日之影加载（D023）；为 false 时目录隐藏往日之影物品。名字表不可用时为 true。</summary>
    public bool ExpansionInstalled => _index?.ExpansionInstalled ?? true;

    /// <summary>
    /// 从游戏目录加载。expansionInstalled 为 null 时按 <see cref="Cp2077Paths.HasPhantomLiberty"/> 检测往日之影（D023）。
    /// </summary>
    public static ItemNameCatalog Load(string gameDirectory, string lang = "zh-cn", IOodleDecompressor? oodle = null, bool? expansionInstalled = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var tweaks = Path.Combine(gameDirectory, "tools", "redmod", "tweaks");
        var expansion = expansionInstalled ?? Cp2077Paths.HasPhantomLiberty(gameDirectory);
        var scan = System.Diagnostics.Stopwatch.StartNew();
        var index = TweakSourceIndex.TryLoad(tweaks, expansion, out _);
        scan.Stop();
        if (index is null)
            return new ItemNameCatalog(null, false, scan.ElapsedMilliseconds, 0);

        var localization = System.Diagnostics.Stopwatch.StartNew();
        var text = LocalizationCatalog.Load(gameDirectory, lang, oodle);
        localization.Stop();
        var catalog = new ItemNameCatalog(index, true, scan.ElapsedMilliseconds, localization.ElapsedMilliseconds);
        catalog.IndexText(text.Entries);
        return catalog;
    }

    public static ItemNameCatalog FromParts(TweakSourceIndex index, IReadOnlyList<LocEntry> entries)
    {
        var catalog = new ItemNameCatalog(index, true, 0, 0);
        catalog.IndexText(entries);
        return catalog;
    }

    public static bool TryParseKey(string key, out ParsedNameKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return TryReadId(key[2..], key, out parsed);
        if (key.Length == 16 && IsHex(key))
            return TryReadId(key, key, out parsed);
        if (IsHex(key))
            return false;
        parsed = new ParsedNameKey(false, 0, key);
        return true;
    }

    public ItemName Resolve(string key)
    {
        if (!TryParseKey(key, out var parsed))
            throw new ArgumentException($"无法解析 {key}。");
        return parsed.IsId ? Resolve(parsed.Id) : Resolve(TweakDbId.FromName(parsed.Name));
    }

    public ItemName Resolve(ulong tweakDbId)
    {
        var hex = Convert.ToHexString(BitConverter.GetBytes(tweakDbId)).ToLowerInvariant();
        if (!Available || _index is null)
            return new ItemName(hex, null, null, null, false);
        var record = _index.Find(tweakDbId);
        if (record is null)
            return new ItemName(hex, null, null, null, false);
        if (string.IsNullOrEmpty(record.DisplayName))
            return new ItemName(record.FullName, record.FullName, null, null, false);
        var text = Lookup(record.DisplayName);
        return new ItemName(text ?? record.FullName, record.FullName, record.DisplayName, text, true);
    }

    public IReadOnlyList<TweakPresetPart>? PresetParts(string recordName) =>
        Available ? _index?.FindPresetParts(recordName) : null;

    /// <summary>可生成目录，首次访问时构建并缓存。名字表不可用时为 null。</summary>
    public GearCatalog? Gear => _gear.Value;

    /// <summary>记录 → UIIcon 的解析器（只用 tweak 源，不读归档），首次访问时构建。名字表不可用时为 null。</summary>
    public ItemIconResolver? IconResolver => Available ? _icons.Value : null;

    /// <summary>记录能解析到背包图标（女性图标，游戏默认）。</summary>
    public bool HasIcon(string recordName) => IconResolver?.Resolve(recordName, IconGender.Female) is not null;

    /// <summary>内部物品（个人链接、收起的拳头等）不进复制和生成的列表。</summary>
    public static bool IsInternalRecord(string? recordName) =>
        recordName is not null
        && (recordName.Contains("PersonalLink", StringComparison.OrdinalIgnoreCase)
            || recordName.Contains("personal_link", StringComparison.OrdinalIgnoreCase)
            || recordName.Contains("HolsteredFists", StringComparison.OrdinalIgnoreCase)
            || recordName.Contains("__fists_", StringComparison.OrdinalIgnoreCase));

    private static bool TryReadId(string hex, string original, out ParsedNameKey parsed)
    {
        parsed = default;
        if (hex.Length != 16 || !IsHex(hex))
            return false;
        parsed = new ParsedNameKey(true, BitConverter.ToUInt64(Convert.FromHexString(hex)), original);
        return true;
    }

    private static bool IsHex(string text) =>
        text.Length > 0 && text.All(static ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private void IndexText(IReadOnlyList<LocEntry> entries)
    {
        foreach (var entry in entries)
        {
            var text = string.IsNullOrEmpty(entry.FemaleVariant) ? entry.MaleVariant : entry.FemaleVariant;
            if (string.IsNullOrEmpty(text))
                continue;
            _byKey[entry.PrimaryKey] = text;
            if (!string.IsNullOrEmpty(entry.SecondaryKey))
                _bySecondary[entry.SecondaryKey] = text;
        }
    }

    private string? Lookup(string displayName)
    {
        if (displayName.StartsWith("LocKey#", StringComparison.Ordinal) && ulong.TryParse(displayName["LocKey#".Length..], out var key))
            return _byKey.TryGetValue(key, out var text) ? text : null;
        return _bySecondary.TryGetValue(displayName, out var secondary) ? secondary : null;
    }

}
