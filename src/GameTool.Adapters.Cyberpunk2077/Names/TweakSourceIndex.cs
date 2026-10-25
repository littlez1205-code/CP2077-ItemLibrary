using System.Text;
using System.Text.RegularExpressions;

namespace GameTool.Adapters.Cyberpunk2077.Names;

public sealed record TweakRecord(string FullName, ulong TweakDbId, string? DisplayName);

public sealed record TweakPresetPart(string Slot, string Item);

public sealed record EffectInfo(string LocKey, string? IntValues, string? FloatValues, string? Stats = null, string? SourceRecord = null);

/// <summary>
/// slotPartListPreset 里的一个原始条目：内联 <c>{ slot; itemPartPreset; } [: 模板]</c>，或裸字符串引用一条 SlotItemPartPreset 记录。
/// 解析阶段只记原文，缺的 slot / itemPartPreset 在 <see cref="TweakSourceIndex.FindPresetParts"/> 里按模板或被引用的记录补齐。
/// </summary>
public sealed record TweakPresetEntry(string? Slot, string? Item, string? Template, string? Reference);

/// <summary>
/// 只读解析 REDmod 的 .tweak 源。规则来自本机 tweaks 实测：package、using、顶层记录、深度 1 的 displayName、slotPartListPreset、字符串列表（tags 等，见 <see cref="ListFields"/>）
/// 和几个简单字段（quality、itemType 等，见 <see cref="ScalarFields"/>），
/// 以及顶层记录头上方独占一行的属性（如 <c>[ notQueryable ]</c>，可以连续叠几行）。记录体内的属性行修饰的是字段，不算记录的属性。
/// <para>
/// 往日之影（D023）：tweaks\ep1 下的记录、带 <c>[ EP1 ]</c> 属性的记录，以及继承链、配件预设、配方产物依赖它们的记录算往日之影记录（<see cref="IsExpansion"/>）。
/// 没有安装往日之影时（<see cref="ExpansionInstalled"/> 为 false），记录体第 1 层带 <c>[ EP1 ]</c> 的字段不生效，按本体的定义解析
/// （本机实测 ep1 源不重定义任何本体记录；资料片对本体记录的改动都写成这种字段级属性，如 nextUpgradeItem、craftingResult、entityName）。
/// </para>
/// </summary>
public sealed partial class TweakSourceIndex
{
    /// <summary>往日之影的属性名（记录级或字段级）。</summary>
    public const string ExpansionAttribute = "EP1";

    private readonly Dictionary<ulong, TweakRecord> _byId;
    private readonly Dictionary<string, RawRecord> _records;
    private readonly Dictionary<string, bool> _expansion = new(StringComparer.Ordinal);
    private readonly Lock _expansionGate = new();

    private TweakSourceIndex(Dictionary<ulong, TweakRecord> byId, Dictionary<string, RawRecord> records, int overridden, bool expansionInstalled)
    {
        _byId = byId;
        _records = records;
        Overridden = overridden;
        ExpansionInstalled = expansionInstalled;
    }

    public int Overridden { get; }

    /// <summary>游戏里装了往日之影。为 false 时字段级 <c>[ EP1 ]</c> 不生效，可生成目录隐藏 <see cref="IsExpansion"/> 的记录。</summary>
    public bool ExpansionInstalled { get; }

    public int Count => _byId.Count;

    public static TweakSourceIndex? TryLoad(string tweaksDirectory, out string? unavailable) =>
        TryLoad(tweaksDirectory, expansionInstalled: true, out unavailable);

    /// <summary>
    /// 读 tweaks\base 和 tweaks\ep1（都可以没有）。expansionInstalled 见 <see cref="ExpansionInstalled"/>；
    /// ep1 源不论装没装往日之影都读进来，用来认出往日之影记录（REDmod 可能自带 ep1 源）。
    /// </summary>
    public static TweakSourceIndex? TryLoad(string tweaksDirectory, bool expansionInstalled, out string? unavailable)
    {
        if (!Directory.Exists(tweaksDirectory))
        {
            unavailable = ItemNameCatalog.RedmodUnavailable;
            return null;
        }

        unavailable = null;
        return Parse(ReadPack(tweaksDirectory, "base"), ReadPack(tweaksDirectory, "ep1"), expansionInstalled);
    }

    private static List<(string Name, string Text)> ReadPack(string tweaksDirectory, string pack)
    {
        var files = new List<(string Name, string Text)>();
        var root = Path.Combine(tweaksDirectory, pack);
        if (!Directory.Exists(root))
            return files;
        foreach (var path in Directory.EnumerateFiles(root, "*.tweak", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            files.Add((path, File.ReadAllText(path)));
        return files;
    }

    public static TweakSourceIndex Parse(IEnumerable<(string Name, string Text)> files) => Parse(files, [], expansionInstalled: true);

    /// <summary>
    /// files 是本体源（tweaks\base），expansionFiles 是往日之影源（tweaks\ep1，里面的记录都算往日之影记录），按这个顺序解析，后出现的定义覆盖前面的。
    /// </summary>
    public static TweakSourceIndex Parse(IEnumerable<(string Name, string Text)> files, IEnumerable<(string Name, string Text)> expansionFiles, bool expansionInstalled)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(expansionFiles);
        var raw = new Dictionary<string, RawRecord>(StringComparer.Ordinal);
        var overridden = 0;
        var sources = files.Select(file => (file.Text, Expansion: false)).Concat(expansionFiles.Select(file => (file.Text, Expansion: true)));
        foreach (var (text, fromExpansion) in sources)
        {
            foreach (var record in ReadFile(text, fromExpansion, gateExpansionFields: !expansionInstalled))
            {
                if (!raw.TryAdd(record.FullName, record))
                {
                    // 后出现的定义覆盖前面的，属性取并集：任一处标了 notQueryable 等都算；任一处来自 ep1 也算往日之影记录。
                    var previous = raw[record.FullName];
                    var merged = previous.Attributes.Union(record.Attributes, StringComparer.Ordinal).ToArray();
                    raw[record.FullName] = record with { Attributes = merged, FromExpansion = previous.FromExpansion || record.FromExpansion };
                    overridden++;
                }
            }
        }

        var records = new Dictionary<string, RawRecord>(raw, StringComparer.Ordinal);
        var byId = new Dictionary<ulong, TweakRecord>();
        foreach (var record in records.Values)
        {
            var display = InheritedDisplayName(record, records);
            byId[record.Id] = new TweakRecord(record.FullName, record.Id, display);
        }

        return new TweakSourceIndex(byId, records, overridden, expansionInstalled);
    }

    /// <summary>
    /// 往日之影记录（D023）：来自 tweaks\ep1、带 <c>[ EP1 ]</c> 属性，或者继承链上的父记录、配件预设里的配件 / 挂点、
    /// 配方产物（craftingResult.item）是往日之影记录。记录不存在返回 false。结果缓存，线程安全。
    /// </summary>
    public bool IsExpansion(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        lock (_expansionGate)
            return ExpansionOf(recordName, 0);
    }

    private bool ExpansionOf(string name, int depth)
    {
        if (_expansion.TryGetValue(name, out var known))
            return known;
        if (depth > 32 || !_records.TryGetValue(name, out var record))
            return false;
        // 先记 false 当作「正在算」：继承或配件引用成环时不会无限递归。
        _expansion[name] = false;
        var result = record.FromExpansion
            || record.Attributes.Contains(ExpansionAttribute, StringComparer.OrdinalIgnoreCase)
            || (ResolveParent(record, _records) is { } parent && ExpansionOf(parent, depth + 1))
            || (FindPresetParts(name) ?? []).Any(part => DependsOn(part.Item, depth) || DependsOn(part.Slot, depth))
            || (FieldOf(name, CraftingResultItem) is { Length: > 0 } crafted && DependsOn(crafted, depth));
        _expansion[name] = result;
        return result;
    }

    private bool DependsOn(string name, int depth) => !string.IsNullOrEmpty(name) && ExpansionOf(name, depth + 1);

    public TweakRecord? Find(ulong tweakDbId) => _byId.TryGetValue(tweakDbId, out var record) ? record : null;

    public IEnumerable<string> RecordNames => _records.Keys;

    /// <summary>记录头上方的属性名（不含方括号），按出现顺序。记录不存在返回空。</summary>
    public IReadOnlyList<string> AttributesOf(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        return _records.TryGetValue(recordName, out var record) ? record.Attributes : [];
    }

    /// <summary>继承链：自身在前，依次是父记录。解析不到的父记录或出现环时停止。</summary>
    public IReadOnlyList<string> Ancestors(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var name = recordName;
        while (chain.Count < 32 && seen.Add(name) && _records.TryGetValue(name, out var record))
        {
            chain.Add(name);
            var parent = ResolveParent(record, _records);
            if (parent is null)
                break;
            name = parent;
        }

        return chain;
    }

    /// <summary>
    /// 记录的 slotPartListPreset 里能确定 slot 和 itemPartPreset 的条目，按 tweak 顺序：
    /// 内联条目自己没写的字段从 <c>} : 模板</c> 的模板记录（沿继承链）补齐；裸字符串条目取被引用记录（沿继承链）的这两个字段。
    /// 补齐后仍缺任一字段的条目跳过（例如只写了 slot 的模组槽）。
    /// 自己没写时沿父记录查找，+= 接在父记录的列表后面。记录不存在返回 null。
    /// </summary>
    public IReadOnlyList<TweakPresetPart>? FindPresetParts(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        if (!_records.ContainsKey(recordName))
            return null;
        var parts = InheritedParts(recordName, new HashSet<string>(StringComparer.Ordinal));
        if (parts.Count > 0)
            return parts;

        if (Ancestors(recordName).Any(name => name is "RTDB.Clothing" or "Clothing" or "Items.Clothing"))
        {
            var tags = TagsOf(recordName);
            foreach (var (tag, enhancer) in IntrinsicEnhancerTags)
            {
                if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    var slot = EnhancerSlotOf(FieldOf(recordName, "itemType"));
                    return
                    [
                        new TweakPresetPart("AttachmentSlots.GenericItemRoot", "Items.GenericItemRoot"),
                        new TweakPresetPart(slot, enhancer)
                    ];
                }
            }

            return [new TweakPresetPart("AttachmentSlots.GenericItemRoot", "Items.GenericItemRoot")];
        }

        return parts;
    }

    private static readonly (string Tag, string Enhancer)[] IntrinsicEnhancerTags =
    [
        ("ArmorMod", "Items.IntrinsicFabricEnhancer01"),
        ("TherResMod", "Items.IntrinsicFabricEnhancer02"),
        ("ShockResMod", "Items.IntrinsicFabricEnhancer03"),
        ("ChemResMod", "Items.IntrinsicFabricEnhancer04"),
        ("BleedResMod", "Items.IntrinsicFabricEnhancer05"),
        ("BlindResMod", "Items.IntrinsicFabricEnhancer06"),
        ("ZoomMod", "Items.IntrinsicFabricEnhancer07"),
        ("QuickhackMod", "Items.IntrinsicFabricEnhancer08"),
        ("QuickhackUploadMod", "Items.IntrinsicFabricEnhancer09"),
        ("MeleeStaminaMod", "Items.IntrinsicFabricEnhancer10"),
        ("ReloadMod", "Items.IntrinsicFabricEnhancer11"),
        ("VendorMod", "Items.IntrinsicFabricEnhancer12"),
        ("SprintStaminaMod", "Items.IntrinsicFabricEnhancer13"),
        ("VisibilityMod", "Items.IntrinsicFabricEnhancer14"),
        ("GrenadeChargeMod", "Items.IntrinsicFabricEnhancer15"),
        ("EnemyHackMod", "Items.IntrinsicFabricEnhancer16"),
        ("ExplosionMod", "Items.IntrinsicFabricEnhancer17"),
        ("DLCItemMod", "Items.DLCItemsQualityUpgradeMod")
    ];

    private static string EnhancerSlotOf(string? itemType) => itemType switch
    {
        "ItemType.Clo_InnerChest" => "AttachmentSlots.InnerChestFabricEnhancer1",
        "ItemType.Clo_Head" => "AttachmentSlots.HeadFabricEnhancer1",
        "ItemType.Clo_Face" => "AttachmentSlots.FaceFabricEnhancer1",
        "ItemType.Clo_Legs" => "AttachmentSlots.LegsFabricEnhancer1",
        "ItemType.Clo_Feet" => "AttachmentSlots.FeetFabricEnhancer1",
        _ => "AttachmentSlots.OuterChestFabricEnhancer1"
    };

    /// <summary>索引里记下的深度 1 简单字段（物品图标用到外观名、实体名和 UIIcon 的图集路径与部件名；描述与关键属性）。</summary>
    public static readonly string[] ScalarFields =
    [
        "quality", "isSingleInstance", "itemType", "itemCategory", "iconPath",
        "appearanceName", "appearanceResourceName", "entityName", "atlasResourcePath", "atlasPartName",
        "slot", "itemPartPreset", "equipArea", "nextUpgradeItem",
        "localizedDescription", "primaryTriggerMode", "ammo",
        "evolution", "baseReloadTime", "baseEmptyReloadTime"
    ];

    /// <summary>
    /// 配方（RTDB.ItemRecipe）的产物：记录体第 1 层 <c>craftingResult = { item = "Items.X"; … };</c> 里的 item，
    /// 按简单字段记在这个名字下，可用 <see cref="FieldOf"/> 沿继承链取（T023；tweak 源里 928 处全是内联块写法）。
    /// </summary>
    public const string CraftingResultItem = "craftingResult.item";

    /// <summary>嵌套在 UIData 或技能块里的效果文本 LocKey。</summary>
    public const string UiDataLocalizedDescription = "UIData.localizedDescription";

    /// <summary>索引里记下的深度 1 字符串列表字段，沿继承链按 = 覆盖、+= 追加合并。</summary>
    public static readonly string[] ListFields = ["tags", "visualTags", "appearanceSuffixes", "placementSlots", "OnAttach", "OnEquip", "variants", "statModifierGroups"];

    /// <summary>
    /// 沿继承链取深度 1 的简单字段（只记 <see cref="ScalarFields"/> 里的几个），自身没写就看父记录；值去掉引号。
    /// 记录不存在或整条链都没写时返回 null。
    /// </summary>
    public string? FieldOf(string recordName, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        foreach (var name in Ancestors(recordName))
        {
            if (_records[name].Fields.TryGetValue(field, out var value))
                return value;
        }

        return null;
    }

    /// <summary>记录自己写的简单字段（不看父记录），没写或记录不存在返回 null。</summary>
    public string? OwnFieldOf(string recordName, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return _records.TryGetValue(recordName, out var record) && record.Fields.TryGetValue(field, out var value) ? value : null;
    }

    /// <summary>继承后的 tags：自己用 = 写就覆盖父记录，用 += 就接在父记录后面。记录不存在返回空。</summary>
    public IReadOnlyList<string> TagsOf(string recordName) => ListOf(recordName, "tags");

    /// <summary>
    /// 继承后的字符串列表（<see cref="ListFields"/> 之一，如 visualTags、appearanceSuffixes）：
    /// 自己用 = 写就覆盖父记录，用 += 就接在父记录后面。记录不存在或整条链都没写时返回空。
    /// </summary>
    public IReadOnlyList<string> ListOf(string recordName, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return _records.ContainsKey(recordName) ? InheritedList(recordName, field, new HashSet<string>(StringComparer.Ordinal)) : [];
    }

    /// <summary>继承链上任一记录的记录体第 1 层出现了 "Quality.IconicItem"（标志性物品的属性修饰）。</summary>
    public bool MentionsIconicItem(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        return Ancestors(recordName).Any(name => _records[name].MentionsIconic);
    }

    /// <summary>沿继承链获取物品的官方背景说明 LocKey（自身或父类记录中定义的 localizedDescription）。</summary>
    public string? LocalizedDescriptionOf(string recordName) => FieldOf(recordName, "localizedDescription");

    /// <summary>
    /// 获取物品的特殊技能/词条说明 LocKey（如不朽武器专属特效、义体技能、插件效果）。
    /// 支持自身 UIData、挂载技能包（OnAttach/OnEquip）以及预设配件中的不朽插件。
    /// </summary>
    public string? EffectDescriptionOf(string recordName) => EffectInfoOf(recordName)?.LocKey;

    /// <summary>
    /// 获取物品的特殊技能/词条说明信息（含 LocKey 与 UIData 中的 intValues / floatValues 实参）。
    /// </summary>
    public EffectInfo? EffectInfoOf(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        if (!_records.ContainsKey(recordName))
            return null;

        var direct = FieldOf(recordName, UiDataLocalizedDescription);
        if (!string.IsNullOrEmpty(direct))
        {
            return new EffectInfo(
                direct,
                FieldOf(recordName, "UIData.intValues"),
                FieldOf(recordName, "UIData.floatValues"),
                FieldOf(recordName, "UIData.stats"),
                recordName);
        }

        var uiTemplate = FieldOf(recordName, "UIData.template");
        if (!string.IsNullOrEmpty(uiTemplate))
        {
            var tplKey = ResolveExistingName(uiTemplate, recordName);
            if (tplKey is not null)
            {
                var tplLoc = FieldOf(tplKey, UiDataLocalizedDescription) ?? FieldOf(tplKey, "localizedDescription");
                if (!string.IsNullOrEmpty(tplLoc))
                {
                    return new EffectInfo(
                        tplLoc,
                        FieldOf(recordName, "UIData.intValues") ?? FieldOf(tplKey, "UIData.intValues"),
                        FieldOf(recordName, "UIData.floatValues") ?? FieldOf(tplKey, "UIData.floatValues"),
                        FieldOf(recordName, "UIData.stats") ?? FieldOf(tplKey, "UIData.stats"),
                        recordName);
                }
            }
        }

        var abilities = ListOf(recordName, "OnAttach").Concat(ListOf(recordName, "OnEquip"));
        foreach (var ability in abilities)
        {
            var key = ResolveExistingName(ability, recordName);
            if (key is null)
                continue;
            var loc = FieldOf(key, UiDataLocalizedDescription) ?? FieldOf(key, "localizedDescription");
            if (!string.IsNullOrEmpty(loc))
            {
                return new EffectInfo(
                    loc,
                    FieldOf(key, "UIData.intValues") ?? FieldOf(recordName, "UIData.intValues"),
                    FieldOf(key, "UIData.floatValues") ?? FieldOf(recordName, "UIData.floatValues"),
                    FieldOf(key, "UIData.stats") ?? FieldOf(recordName, "UIData.stats"),
                    key);
            }
        }

        var parts = FindPresetParts(recordName);
        if (parts is not null)
        {
            foreach (var part in parts)
            {
                if (part.Slot.Contains("IconicWeaponMod", StringComparison.OrdinalIgnoreCase)
                    || part.Slot.Contains("IconicMeleeWeaponMod", StringComparison.OrdinalIgnoreCase)
                    || part.Item.Contains("Iconic", StringComparison.OrdinalIgnoreCase)
                    || part.Item.EndsWith("WeaponMod", StringComparison.OrdinalIgnoreCase))
                {
                    var modRecord = ResolveExistingName(part.Item, recordName) ?? part.Item;
                    var modDirect = FieldOf(modRecord, UiDataLocalizedDescription);
                    if (!string.IsNullOrEmpty(modDirect))
                    {
                        return new EffectInfo(
                            modDirect,
                            FieldOf(modRecord, "UIData.intValues") ?? FieldOf(recordName, "UIData.intValues"),
                            FieldOf(modRecord, "UIData.floatValues") ?? FieldOf(recordName, "UIData.floatValues"),
                            FieldOf(modRecord, "UIData.stats") ?? FieldOf(recordName, "UIData.stats"),
                            modRecord);
                    }

                    var modAbilities = ListOf(modRecord, "OnAttach").Concat(ListOf(modRecord, "OnEquip"));
                    foreach (var ability in modAbilities)
                    {
                        var key = ResolveExistingName(ability, modRecord);
                        if (key is null)
                            continue;
                        var loc = FieldOf(key, UiDataLocalizedDescription) ?? FieldOf(key, "localizedDescription");
                        if (!string.IsNullOrEmpty(loc))
                        {
                            return new EffectInfo(
                                loc,
                                FieldOf(key, "UIData.intValues") ?? FieldOf(modRecord, "UIData.intValues") ?? FieldOf(recordName, "UIData.intValues"),
                                FieldOf(key, "UIData.floatValues") ?? FieldOf(modRecord, "UIData.floatValues") ?? FieldOf(recordName, "UIData.floatValues"),
                                FieldOf(key, "UIData.stats") ?? FieldOf(modRecord, "UIData.stats") ?? FieldOf(recordName, "UIData.stats"),
                                modRecord);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>记录直接或继承获得的属性修正组（statModifierGroups）。</summary>
    public IReadOnlyList<string> StatModifierGroupsOf(string recordName) => ListOf(recordName, "statModifierGroups");

    /// <summary>
    /// 沿继承链和关联的 statModifierGroups 查找特定常数修正值（如 CycleTime、MagazineCapacity、ProjectilesPerShot、MeleeAttackDuration、AttacksPerSecond、ReloadTime）。
    /// </summary>
    public string? StatModifierValueOf(string recordName, string statKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(statKey);

        foreach (var ancestor in Ancestors(recordName))
        {
            var direct = OwnFieldOf(ancestor, statKey);
            if (!string.IsNullOrEmpty(direct))
                return direct;

            var groups = ListOf(ancestor, "statModifierGroups");
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                var group = groups[i];
                var resolved = ResolveExistingName(group, ancestor) ?? group;
                var val = FieldOf(resolved, statKey);
                if (!string.IsNullOrEmpty(val))
                    return val;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取物品或词条在指定品质档次下的属性值。
    /// 支持常数修正、按 Quality 线性缩放（CombinedStatModifier）、官方品质曲线（CurveStatModifier）以及基于其它属性乘算的复合修正。
    /// </summary>
    public double? StatValueAtQuality(string recordName, string statName, int qualityTier, int depth = 0)
    {
        if (depth > 5) return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(statName);

        var shortName = statName.StartsWith("BaseStats.", StringComparison.Ordinal)
            ? statName["BaseStats.".Length..]
            : statName;

        var chain = Ancestors(recordName);
        var found = false;
        var total = 0.0;

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var ancestor = chain[i];

            if (OwnFieldOf(ancestor, $"StatZero.{shortName}") is not null)
            {
                total = 0.0;
                found = true;
            }

            var curveCol = OwnFieldOf(ancestor, $"StatCurve.{shortName}");
            if (curveCol is not null && TryEvalQualityCurve(curveCol, qualityTier, out var curveVal))
            {
                total += curveVal;
                found = true;
            }

            var baseStr = OwnFieldOf(ancestor, $"StatBase.{shortName}");
            var stepStr = OwnFieldOf(ancestor, $"StatQualityStep.{shortName}");
            if (baseStr is not null || stepStr is not null)
            {
                var baseVal = double.TryParse(baseStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0.0;
                var stepVal = double.TryParse(stepStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0.0;
                total += baseVal + qualityTier * stepVal;
                found = true;
            }

            var refStr = OwnFieldOf(ancestor, $"StatRef.{shortName}");
            if (refStr is not null)
            {
                foreach (var entry in refStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = entry.Split('|');
                    if (parts.Length == 3)
                    {
                        var refStat = parts[0];
                        if (refStat is "DamagePerHit" or "StunApplicationRate")
                            continue;
                        var op = parts[1];
                        if (double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var factor))
                        {
                            var refVal = StatValueAtQuality(recordName, refStat, qualityTier, depth + 1);
                            if (refVal is not null)
                            {
                                total += op == "*" ? refVal.Value * factor : refVal.Value + factor;
                                found = true;
                            }
                        }
                    }
                }
            }

            if (!found && _records.TryGetValue(ancestor, out var rec) && rec.Lists.TryGetValue("statModifierGroups", out var ownGroups))
            {
                foreach (var group in ownGroups.Values)
                {
                    var resolved = ResolveExistingName(group, ancestor) ?? group;
                    var groupCurve = FieldOf(resolved, $"StatCurve.{shortName}");
                    if (groupCurve is not null && TryEvalQualityCurve(groupCurve, qualityTier, out var gcVal))
                    {
                        total += gcVal;
                        found = true;
                    }
                }
            }
        }

        if (found)
            return total;

        // 兜底：近战/手臂义体元素异常施加几率曲线（Common: 15%, Uncommon: 20%, Rare: 25%, Epic: 30%, Legendary: 35%, Legendary++: 40%）
        if (shortName is "PoisonedApplicationRate" or "BurningApplicationRate"
            or "BleedingApplicationRate" or "ElectrocutedApplicationRate")
        {
            var tier = Math.Clamp(qualityTier, 0, 5);
            return 15 + tier * 5;
        }

        return null;
    }

    private static readonly Dictionary<string, double[]> QualityCurves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hgmod1_stacks"] = [2, 3, 4, 5, 5, 5],
        ["throwmod1_returnchance"] = [20, 25, 30, 40, 50, 50],
        ["hgmod2_critchance"] = [5, 5, 10, 10, 15, 15],
        ["hgmod2_spread"] = [10, 15, 15, 20, 20, 20],
        ["hgmod2_swap"] = [15, 20, 20, 25, 25, 25],
        ["smartmod2_armor_pene"] = [0.1, 0.135, 0.17, 0.21, 0.25, 0.25],
        ["powermod2_armor_pene"] = [0.1, 0.1, 0.1, 0.2, 0.3, 0.3],
        ["powermod2_stamina_cost"] = [25, 25, 20, 20, 15, 15],
        ["meleemod3_crit_damage"] = [0, 0, 0, 25, 50, 50],
        ["meleemod3_crit_chance"] = [20, 30, 40, 50, 100, 100],
        ["bluntmod3_stun_chance"] = [0, 0, 0, 15, 30, 30],
        ["smartmod3_locktime"] = [-20, -20, -20, -10, -10, -10],
        ["smartmod1_locktime"] = [20, 20, 20, 20, 30, 30],
        ["prsrmod3_hitreaction"] = [-0.35, -0.35, -0.35, -0.15, -0.15, -0.15],
        ["prsrmod3_armor_pene_potency"] = [40, 30, 30, 20, 20, 20],
        ["bluntmod1_stamina_cost"] = [-0.25, -0.25, -0.2, -0.2, -0.1, -0.1],
        ["bluntmod1_knockback"] = [15, 22, 25, 32, 40, 40],
        ["rangedmod2_reload"] = [0.25, 0.25, 0.25, 0.15, 0.15, 0.15],
        ["powermod1_dmg_convert_mult"] = [-0.895, -0.86, -0.825, -0.79, -0.65, -0.65],
        ["shotgunmod3_dmg"] = [-20, -15, -15, -10, -10, -10],
        ["shotgunmod3_spread"] = [15, 15, 20, 20, 25, 25],
        ["techmod2_pierce_dmg"] = [-0.2, -0.2, -0.25, -0.25, -0.3, -0.3],
        ["techmod2_pierce_threshold"] = [-0.2, -0.25, -0.25, -0.3, -0.35, -0.35],
        ["meleemod2_stacks"] = [2, 3, 4, 5, 7, 7],
        ["prsrmod1_handling"] = [12, 16, 20, 25, 30, 30],
        ["throwmod2_range"] = [10, 10, 10, 25, 50, 50],
        ["meleemod1_critchance"] = [10, 13, 16, 20, 25, 25],
        ["meleemod1_critdamage"] = [20, 30, 40, 55, 70, 70],
        ["quality_to_strong_arm_skillcheck_bonus"] = [0, 1, 2, 4, 6, 6],
        ["quality_to_debuff_application_rate_on_melee"] = [15, 20, 25, 30, 35, 40],
        ["quality_to_debuff_application_rate_on_fists"] = [15, 20, 25, 30, 35, 40],
        ["quality_to_debuff_application_rate_on_ranged"] = [5, 7, 9, 12, 15, 18],
        ["sfe09_quality_to_reveal"] = [0, -0.01, -0.02, -0.04, -0.06, -0.06],
        ["ife16_quality_to_enemy_hack_upload"] = [5, 7, 9, 12, 15, 15],
        ["ife14_quality_to_healing_recharge"] = [0.01, 0.017, 0.024, 0.032, 0.04, 0.04]
    };

    private static bool TryEvalQualityCurve(string column, int qualityTier, out double value)
    {
        if (QualityCurves.TryGetValue(column, out var pts) && pts.Length > 0)
        {
            var idx = Math.Clamp(qualityTier, 0, pts.Length - 1);
            value = pts[idx];
            return true;
        }

        value = 0;
        return false;
    }

    private string? ResolveExistingName(string raw, string contextRecord)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (_records.ContainsKey(raw))
            return raw;
        if (_records.TryGetValue(contextRecord, out var ctx))
        {
            var resolved = ResolveName(raw, ctx, _records);
            if (resolved is not null)
                return resolved;
        }
        if (!raw.Contains('.') && _records.ContainsKey("Items." + raw))
            return "Items." + raw;
        return null;
    }

    private List<string> InheritedList(string name, string field, HashSet<string> seen)
    {
        if (seen.Count >= 32 || !seen.Add(name) || !_records.TryGetValue(name, out var record))
            return [];
        var own = record.Lists.TryGetValue(field, out var value) ? value : null;
        if (own is not null && !own.Append)
            return [.. own.Values];
        var parent = ResolveParent(record, _records);
        List<string> inherited = parent is null ? [] : InheritedList(parent, field, seen);
        if (own is not null)
            inherited.AddRange(own.Values);
        return inherited;
    }

    private List<TweakPresetPart> InheritedParts(string name, HashSet<string> seen)
    {
        if (seen.Count >= 32 || !seen.Add(name) || !_records.TryGetValue(name, out var record))
            return [];
        if (record.OwnParts is not null && !record.AppendParts)
            return ResolveEntries(record);
        var parent = ResolveParent(record, _records);
        List<TweakPresetPart> inherited = parent is null ? [] : InheritedParts(parent, seen);
        if (record.OwnParts is not null)
            inherited.AddRange(ResolveEntries(record));
        return inherited;
    }

    private List<TweakPresetPart> ResolveEntries(RawRecord owner)
    {
        var parts = new List<TweakPresetPart>();
        foreach (var entry in owner.OwnParts!)
        {
            var source = entry.Reference ?? entry.Template;
            var resolved = source is null ? null : ResolveName(source, owner, _records);
            // 自己写的字段原样保留（含空串，与 T021 以前一致）；从模板或引用记录补的字段必须非空，
            // 模组槽模板（如 BladeModList_Slot1）把 itemPartPreset 置成空串，不能因此多出空配件。
            var slot = entry.Slot ?? NonEmpty(resolved is null ? null : FieldOf(resolved, "slot"));
            var item = entry.Item ?? NonEmpty(resolved is null ? null : FieldOf(resolved, "itemPartPreset"));
            if (slot is not null && item is not null)
                parts.Add(new TweakPresetPart(slot, item));
        }

        return parts;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? InheritedDisplayName(RawRecord start, Dictionary<string, RawRecord> records)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var name = start.FullName;
        for (var depth = 0; depth < 32; depth++)
        {
            if (!seen.Add(name) || !records.TryGetValue(name, out var record))
                return null;
            if (record.OwnDisplayName is not null)
                return record.OwnDisplayName;
            var parent = ResolveParent(record, records);
            if (parent is null)
                return null;
            name = parent;
        }

        return null;
    }

    private static string? ResolveParent(RawRecord record, Dictionary<string, RawRecord> records) =>
        record.RawParent is null ? null : ResolveName(record.RawParent, record, records);

    /// <summary>记录名按写它的文件解析：带点的原样查，否则先试本文件 package，再试各个 using。</summary>
    private static string? ResolveName(string raw, RawRecord record, Dictionary<string, RawRecord> records)
    {
        if (raw.Contains('.'))
            return records.ContainsKey(raw) ? raw : null;
        if (record.Package is not null)
        {
            var local = record.Package + "." + raw;
            if (records.ContainsKey(local))
                return local;
        }

        foreach (var imported in record.Usings)
        {
            var candidate = imported + "." + raw;
            if (records.ContainsKey(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<RawRecord> ReadFile(string text, bool fromExpansion, bool gateExpansionFields)
    {
        string? package = null;
        var usings = Array.Empty<string>();
        string? pendingName = null;
        string? pendingParent = null;
        var depth = 0;
        string? current = null;
        string? ownDisplay = null;
        List<TweakPresetEntry>? ownParts = null;
        var appendParts = false;
        var lists = new Dictionary<string, OwnList>(StringComparer.Ordinal);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var mentionsIconic = false;
        var crafting = false;
        string? listField = null;
        var listAppend = false;
        StringBuilder? list = null;
        var listBalance = 0;
        var listOpened = false;
        var attributes = new List<string>();
        var currentAttributes = Array.Empty<string>();
        string? pendingStatType = null;
        string? pendingModifierVal = null;
        string? pendingRefStat = null;
        string? pendingOpSymbol = null;
        string? pendingColumn = null;
        string? pendingModifierKind = null;
        string? currentUiList = null;
        List<string>? currentUiInts = null;
        List<string>? currentUiFloats = null;
        List<string>? currentUiStats = null;
        // 字段级 [ EP1 ]（只在 gateExpansionFields 时处理）：紧跟的那条语句（含多行列表、内联块）整条跳过。
        var gatePending = false;
        var gated = false;
        var gateBalance = 0;
        var gateOpened = false;
        var records = new List<RawRecord>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (depth == 0)
            {
                if (line.StartsWith('['))
                {
                    // 顶层属性行：挂到下一条打开的记录上，中间隔了别的内容就作废。
                    var attribute = AttributePattern().Match(line);
                    if (attribute.Success)
                    {
                        foreach (var name in attribute.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            attributes.Add(name);
                    }

                    continue;
                }

                var packageMatch = PackagePattern().Match(line);
                if (packageMatch.Success)
                {
                    package = packageMatch.Groups[1].Value;
                    pendingName = null;
                    attributes.Clear();
                    continue;
                }

                var usingMatch = UsingPattern().Match(line);
                if (usingMatch.Success)
                {
                    usings = usingMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    pendingName = null;
                    attributes.Clear();
                    continue;
                }

                var header = HeaderPattern().Match(line);
                if (header.Success && !line.Contains('{'))
                {
                    pendingName = header.Groups[1].Value;
                    pendingParent = header.Groups[2].Success ? header.Groups[2].Value : null;
                    continue;
                }

                if (line == "{" && pendingName is not null)
                {
                    current = package is null ? pendingName : package + "." + pendingName;
                    currentAttributes = [.. attributes];
                    attributes.Clear();
                    ownDisplay = null;
                    ownParts = null;
                    appendParts = false;
                    lists = new Dictionary<string, OwnList>(StringComparer.Ordinal);
                    fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    mentionsIconic = false;
                    crafting = false;
                    list = null;
                    gatePending = false;
                    gated = false;
                    pendingStatType = null;
                    pendingModifierVal = null;
                    pendingRefStat = null;
                    pendingOpSymbol = null;
                    pendingColumn = null;
                    pendingModifierKind = null;
                    currentUiList = null;
                    currentUiInts = null;
                    currentUiFloats = null;
                    currentUiStats = null;
                    depth = 1;
                    pendingName = null;
                    continue;
                }

                pendingName = null;
                attributes.Clear();
                depth = Math.Max(0, depth + BraceDelta(line));
                continue;
            }

            if (gateExpansionFields && !gated && depth == 1 && list is null && line != "{")
            {
                // 记录体第 1 层的一条新语句。属性行只登记，修饰的是紧跟的下一条语句。
                var fieldAttribute = line.StartsWith('[') ? AttributePattern().Match(line) : Match.Empty;
                if (fieldAttribute.Success)
                {
                    if (fieldAttribute.Groups[1].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(ExpansionAttribute, StringComparer.OrdinalIgnoreCase))
                        gatePending = true;
                    continue;
                }

                if (gatePending)
                {
                    gatePending = false;
                    gated = true;
                    gateBalance = 0;
                    gateOpened = false;
                    crafting = false;
                }
            }

            if (gated)
            {
                // 被 [ EP1 ] 修饰的语句：只跟踪花括号和方括号，回到第 1 层、括号配平、以分号结尾时语句结束。
                depth += BraceDelta(line);
                Brackets(line, ref gateBalance, ref gateOpened);
                if (depth <= 0)
                    EndRecord();
                else if (depth == 1 && gateBalance <= 0 && line.EndsWith(';'))
                    gated = false;
                continue;
            }

            // 只看记录体第 1 层（含第 1 层的多行列表）；npcRPGData 这类嵌套块里的修饰不算物品本身的。
            if (depth == 1 && line.Contains("\"Quality.IconicItem\"", StringComparison.Ordinal))
                mentionsIconic = true;
            // craftingResult 的内联块：块内第 1 层（记录体深度 2）的 item 就是配方产物。
            if (depth == 1 && list is null && line != "{")
                crafting = CraftingPattern().IsMatch(line);
            else if (crafting && depth == 2)
            {
                var item = CraftingItemPattern().Match(line);
                if (item.Success)
                    fields[CraftingResultItem] = item.Groups[1].Value;
            }

            if (depth >= 1 || list is not null)
            {
                if (depth > 1 || list is not null)
                {
                    var loc = LocDescriptionPattern().Match(line);
                    if (loc.Success && !fields.ContainsKey(UiDataLocalizedDescription))
                        fields[UiDataLocalizedDescription] = loc.Groups[1].Value.Trim().Trim('"');
                }

                var uiListMatch = UiListPattern().Match(line);
                if (uiListMatch.Success)
                {
                    currentUiList = uiListMatch.Groups[1].Value;
                    var rest = uiListMatch.Groups[3].Value;
                    if (currentUiList == "stats")
                    {
                        if (rest.Contains('{'))
                        {
                            currentUiList = null;
                        }
                        else
                        {
                            currentUiStats ??= [];
                            if (!string.IsNullOrEmpty(rest))
                            {
                                foreach (Match m in StringPattern().Matches(rest))
                                    currentUiStats.Add(m.Groups[1].Value);
                                if (rest.Contains(']'))
                                {
                                    if (!fields.ContainsKey("UIData.stats") && currentUiStats.Count > 0)
                                        fields["UIData.stats"] = string.Join(",", currentUiStats);
                                    currentUiList = null;
                                }
                            }
                        }
                    }
                    else
                    {
                        var target = currentUiList == "intValues" ? (currentUiInts ??= []) : (currentUiFloats ??= []);
                        if (!string.IsNullOrEmpty(rest))
                        {
                            ExtractNumbers(rest, target);
                            if (rest.Contains(']'))
                            {
                                if (currentUiList == "intValues" && !fields.ContainsKey("UIData.intValues") && currentUiInts?.Count > 0)
                                    fields["UIData.intValues"] = string.Join(",", currentUiInts);
                                else if (currentUiList == "floatValues" && !fields.ContainsKey("UIData.floatValues") && currentUiFloats?.Count > 0)
                                    fields["UIData.floatValues"] = string.Join(",", currentUiFloats);
                                currentUiList = null;
                            }
                        }
                    }
                }
                else if (currentUiList is not null)
                {
                    if (currentUiList == "stats")
                    {
                        if (line.Contains('{') || line.Contains('='))
                        {
                            currentUiStats?.Clear();
                            currentUiList = null;
                        }
                        else
                        {
                            currentUiStats ??= [];
                            foreach (Match m in StringPattern().Matches(line))
                                currentUiStats.Add(m.Groups[1].Value);
                            if (line.Contains(']'))
                            {
                                if (!fields.ContainsKey("UIData.stats") && currentUiStats.Count > 0)
                                    fields["UIData.stats"] = string.Join(",", currentUiStats);
                                currentUiList = null;
                            }
                        }
                    }
                    else
                    {
                        var target = currentUiList == "intValues" ? (currentUiInts ??= []) : (currentUiFloats ??= []);
                        ExtractNumbers(line, target);
                        if (line.Contains(']'))
                        {
                            if (currentUiList == "intValues" && !fields.ContainsKey("UIData.intValues") && currentUiInts?.Count > 0)
                                fields["UIData.intValues"] = string.Join(",", currentUiInts);
                            else if (currentUiList == "floatValues" && !fields.ContainsKey("UIData.floatValues") && currentUiFloats?.Count > 0)
                                fields["UIData.floatValues"] = string.Join(",", currentUiFloats);
                            currentUiList = null;
                        }
                    }
                }
            }

            var statTypeMatch = StatTypePattern().Match(line);
            if (statTypeMatch.Success)
                pendingStatType = statTypeMatch.Groups[1].Value;

            var modKindMatch = ModifierKindPattern().Match(line);
            if (modKindMatch.Success)
                pendingModifierKind = modKindMatch.Groups[1].Value;

            var valMatch = ModifierValuePattern().Match(line);
            if (valMatch.Success)
                pendingModifierVal = valMatch.Groups[1].Value.TrimEnd('f', '.');

            var refStatMatch = RefStatPattern().Match(line);
            if (refStatMatch.Success)
                pendingRefStat = refStatMatch.Groups[1].Value;

            var opSymbolMatch = OpSymbolPattern().Match(line);
            if (opSymbolMatch.Success)
                pendingOpSymbol = opSymbolMatch.Groups[1].Value;

            var colMatch = ColumnPattern().Match(line);
            if (colMatch.Success)
                pendingColumn = colMatch.Groups[1].Value;

            var modMatch = ModifierTypePattern().Match(line);
            if (modMatch.Success)
            {
                var modType = modMatch.Groups[1].Value;
                if (modType.EndsWith("UIData", StringComparison.Ordinal) && !fields.ContainsKey("UIData.template"))
                {
                    fields["UIData.template"] = modType;
                }
                else if (modType == "CurveStatModifier")
                {
                    if (pendingStatType is not null && pendingColumn is not null)
                        fields[$"StatCurve.{pendingStatType}"] = pendingColumn;
                }
                else if (pendingModifierVal is not null)
                {
                    if (modType == "CycleTimeModifier")
                        fields["CycleTime"] = pendingModifierVal;
                    else if (modType == "MagazineCapacityModifier")
                        fields["MagazineCapacity"] = pendingModifierVal;
                    else if (modType == "ProjectilesPerShotModifier")
                        fields["ProjectilesPerShot"] = pendingModifierVal;
                    else if (modType == "ReloadTimeModifier")
                        fields["ReloadTime"] = pendingModifierVal;
                    else if (pendingStatType == "MeleeAttackDuration")
                        fields["MeleeAttackDuration"] = pendingModifierVal;
                    else if (pendingStatType == "AttacksPerSecond")
                        fields["AttacksPerSecond"] = pendingModifierVal;

                    if (pendingStatType is not null)
                    {
                        if (modType == "ConstantStatModifier")
                        {
                            if (pendingModifierKind == "Multiplier" && (pendingModifierVal == "0" || pendingModifierVal == "0.0"))
                                fields[$"StatZero.{pendingStatType}"] = "1";
                            else
                                fields[$"StatBase.{pendingStatType}"] = pendingModifierVal;
                        }
                        else if (modType == "CombinedStatModifier")
                        {
                            if (pendingRefStat == "Quality")
                            {
                                fields[$"StatQualityStep.{pendingStatType}"] = pendingModifierVal;
                            }
                            else if (pendingRefStat is not null)
                            {
                                var entry = $"{pendingRefStat}|{pendingOpSymbol ?? "*"}|{pendingModifierVal}";
                                if (fields.TryGetValue($"StatRef.{pendingStatType}", out var existing))
                                    fields[$"StatRef.{pendingStatType}"] = existing + ";" + entry;
                                else
                                    fields[$"StatRef.{pendingStatType}"] = entry;
                            }
                        }
                    }
                }

                pendingStatType = null;
                pendingModifierVal = null;
                pendingRefStat = null;
                pendingOpSymbol = null;
                pendingColumn = null;
                pendingModifierKind = null;
            }

            if (list is not null)
            {
                list.Append('\n').Append(line);
                Brackets(line, ref listBalance, ref listOpened);
                if (listOpened && listBalance <= 0)
                {
                    if (listField is not null)
                        lists[listField] = new OwnList(ReadStrings(list.ToString()), listAppend);
                    else
                        ownParts = ReadPartList(list.ToString());
                    list = null;
                }
            }
            else if (depth == 1)
            {
                var display = DisplayPattern().Match(line);
                if (display.Success)
                    ownDisplay = display.Groups[1].Value;
                var scalar = ScalarPattern().Match(line);
                if (scalar.Success)
                    fields[scalar.Groups[1].Value] = scalar.Groups[2].Value.Trim().Trim('"').TrimEnd('f');
                var parts = PartListPattern().Match(line);
                var strings = parts.Success ? Match.Empty : StringListPattern().Match(line);
                if (parts.Success || strings.Success)
                {
                    listField = parts.Success ? null : strings.Groups[1].Value;
                    var append = (parts.Success ? parts.Groups[1].Value : strings.Groups[2].Value) == "+=";
                    if (listField is not null)
                        listAppend = append;
                    else
                        appendParts = append;
                    var rest = parts.Success ? parts.Groups[2].Value : strings.Groups[3].Value;
                    listBalance = 0;
                    listOpened = false;
                    Brackets(rest, ref listBalance, ref listOpened);
                    if (listOpened && listBalance <= 0)
                    {
                        if (listField is not null)
                            lists[listField] = new OwnList(ReadStrings(rest), listAppend);
                        else
                            ownParts = ReadPartList(rest);
                    }
                    else
                    {
                        list = new StringBuilder(rest);
                    }
                }
            }

            var delta = BraceDelta(line);
            depth += delta;
            if (depth <= 0)
                EndRecord();
        }

        return records;

        void EndRecord()
        {
            if (currentUiInts is { Count: > 0 } && !fields.ContainsKey("UIData.intValues"))
                fields["UIData.intValues"] = string.Join(",", currentUiInts);
            if (currentUiFloats is { Count: > 0 } && !fields.ContainsKey("UIData.floatValues"))
                fields["UIData.floatValues"] = string.Join(",", currentUiFloats);
            if (currentUiStats is { Count: > 0 } && !fields.ContainsKey("UIData.stats"))
                fields["UIData.stats"] = string.Join(",", currentUiStats);

            records.Add(new RawRecord(current!, package, pendingParent, usings, ownDisplay, TweakDbId.FromName(current!), ownParts, appendParts, currentAttributes)
            {
                Fields = fields,
                Lists = lists,
                MentionsIconic = mentionsIconic,
                FromExpansion = fromExpansion
            });
            depth = 0;
            current = null;
            pendingParent = null;
            list = null;
            pendingStatType = null;
            pendingModifierVal = null;
            pendingRefStat = null;
            pendingOpSymbol = null;
            pendingColumn = null;
            pendingModifierKind = null;
            currentUiList = null;
            currentUiInts = null;
            currentUiFloats = null;
            currentUiStats = null;
            gatePending = false;
            gated = false;
        }
    }

    private static int BraceDelta(string line)
    {
        var delta = 0;
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted)
                continue;
            if (ch == '{')
                delta++;
            else if (ch == '}')
                delta--;
        }

        return delta;
    }

    private static void Brackets(string text, ref int balance, ref bool opened)
    {
        var quoted = false;
        foreach (var ch in text)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (quoted)
                continue;
            else if (ch == '[')
            {
                balance++;
                opened = true;
            }
            else if (ch == ']')
                balance--;
        }
    }

    /// <summary>
    /// 列表的顶层条目（按顶层逗号分开）：<c>{ … }</c> 内联条目只看深度 1 的 slot / itemPartPreset（更深的嵌套内容不看），
    /// 后面可跟 <c>: 模板</c>；<c>"Items.X"</c> 裸字符串是对另一条记录的引用。其他写法跳过。
    /// </summary>
    internal static List<TweakPresetEntry> ReadPartList(string text)
    {
        var entries = new List<TweakPresetEntry>();
        var quoted = false;
        var braces = 0;
        var brackets = 0;
        var started = false;
        var outside = new StringBuilder();
        StringBuilder? body = null;
        string? ownBody = null;

        void Flush()
        {
            var tail = outside.ToString().Trim();
            if (ownBody is not null)
            {
                var slot = SlotPattern().Match(ownBody);
                var item = ItemPartPattern().Match(ownBody);
                var template = TemplatePattern().Match(tail);
                entries.Add(new TweakPresetEntry(
                    slot.Success ? slot.Groups[1].Value : null,
                    item.Success ? item.Groups[1].Value : null,
                    template.Success ? template.Groups[1].Value : null,
                    null));
            }
            else
            {
                var reference = ReferencePattern().Match(tail);
                if (reference.Success)
                    entries.Add(new TweakPresetEntry(null, null, null, reference.Groups[1].Value));
            }

            outside.Clear();
            ownBody = null;
        }

        foreach (var ch in text)
        {
            if (!started)
            {
                if (ch == '[')
                {
                    started = true;
                    brackets = 1;
                }

                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted)
            {
                if (ch == '{')
                {
                    braces++;
                    if (braces == 1 && brackets == 1)
                    {
                        body = new StringBuilder();
                        outside.Clear();
                        continue;
                    }
                }
                else if (ch == '}')
                {
                    braces--;
                    if (braces == 0 && brackets == 1 && body is not null)
                    {
                        ownBody = body.ToString();
                        body = null;
                        continue;
                    }
                }
                else if (ch == '[')
                {
                    brackets++;
                }
                else if (ch == ']')
                {
                    brackets--;
                    if (brackets == 0 && braces == 0)
                    {
                        Flush();
                        break;
                    }
                }
                else if (ch == ',' && braces == 0 && brackets == 1)
                {
                    Flush();
                    continue;
                }
            }

            if (braces == 0 && brackets == 1)
                outside.Append(ch);
            else if (braces == 1)
                body?.Append(ch);
        }

        return entries;
    }

    /// <summary>列表里所有带引号的字符串（tags 这类 CName 列表）。</summary>
    private static List<string> ReadStrings(string text) =>
        StringPattern().Matches(text).Select(match => match.Groups[1].Value).ToList();

    private static void ExtractNumbers(string text, List<string> target)
    {
        var code = text;
        var commentIdx = text.IndexOf("//", StringComparison.Ordinal);
        if (commentIdx >= 0)
            code = text[..commentIdx];

        foreach (Match m in NumberPattern().Matches(code))
        {
            var num = m.Groups[1].Value.TrimEnd('.');
            if (!string.IsNullOrEmpty(num))
                target.Add(num);
        }
    }

    [GeneratedRegex("(?<![A-Za-z0-9_])(intValues|floatValues|stats)\\s*(\\+?=)\\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UiListPattern();

    [GeneratedRegex("refStat\\s*=\\s*\"BaseStats\\.([A-Za-z0-9_]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RefStatPattern();

    [GeneratedRegex("opSymbol\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex OpSymbolPattern();

    [GeneratedRegex("column\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnPattern();

    [GeneratedRegex("modifierType\\s*=\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ModifierKindPattern();

    [GeneratedRegex("([-+]?[0-9]+(?:\\.[0-9]*)?)[fF]?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex("\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex StringPattern();

    [GeneratedRegex("(?:^|[\\s>\\]])(tags|visualTags|appearanceSuffixes|placementSlots|OnAttach|OnEquip|variants|statModifierGroups)\\s*(\\+?=)\\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex StringListPattern();

    [GeneratedRegex("^(?:[^=]*\\s)?(quality|isSingleInstance|itemType|itemCategory|iconPath|appearanceName|appearanceResourceName|entityName|atlasResourcePath|atlasPartName|slot|itemPartPreset|equipArea|nextUpgradeItem|localizedDescription|primaryTriggerMode|ammo|evolution|baseReloadTime|baseEmptyReloadTime)\\s*=\\s*([^;]*);\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ScalarPattern();

    [GeneratedRegex("statType\\s*=\\s*\"BaseStats\\.([A-Za-z0-9_]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex StatTypePattern();

    [GeneratedRegex("(?:float\\s+)?value\\s*=\\s*([0-9.-]+)f?;", RegexOptions.CultureInvariant)]
    private static partial Regex ModifierValuePattern();

    [GeneratedRegex("\\}\\s*:\\s*([A-Za-z0-9_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ModifierTypePattern();

    [GeneratedRegex("^(?:[^=]*\\s)?localizedDescription\\s*=\\s*\"([^\"]*)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex LocDescriptionPattern();

    [GeneratedRegex("^(?:[^=]*\\s)?craftingResult\\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex CraftingPattern();

    [GeneratedRegex("^item\\s*=\\s*\"([^\"]+)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex CraftingItemPattern();

    [GeneratedRegex("(?:^|[\\s>])slotPartListPreset\\s*(\\+?=)\\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PartListPattern();

    [GeneratedRegex("(?<![A-Za-z0-9_])slot\\s*=\\s*\"([^\"]*)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex SlotPattern();

    [GeneratedRegex("(?<![A-Za-z0-9_])itemPartPreset\\s*=\\s*\"([^\"]*)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex ItemPartPattern();

    [GeneratedRegex("^\\[\\s*([A-Za-z_][A-Za-z0-9_]*(?:\\s*,\\s*[A-Za-z_][A-Za-z0-9_]*)*)\\s*\\]$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();

    [GeneratedRegex("^:\\s*([A-Za-z_][A-Za-z0-9_.]*)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePattern();

    [GeneratedRegex("^\"([A-Za-z_][A-Za-z0-9_.]*)\"$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("^package\\s+(\\S+)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^using\\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex UsingPattern();

    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_.]*)\\s*(?::\\s*([A-Za-z_][A-Za-z0-9_.]*))?\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex("^(?:[A-Za-z_][A-Za-z0-9_.]*\\s+)?displayName\\s*=\\s*\"([^\"]*)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayPattern();

    private sealed record OwnList(List<string> Values, bool Append);

    private sealed record RawRecord(
        string FullName,
        string? Package,
        string? RawParent,
        string[] Usings,
        string? OwnDisplayName,
        ulong Id,
        List<TweakPresetEntry>? OwnParts,
        bool AppendParts,
        string[] Attributes)
    {
        public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, OwnList> Lists { get; init; } = new(StringComparer.Ordinal);

        public bool MentionsIconic { get; init; }

        /// <summary>定义在 tweaks\ep1 里。</summary>
        public bool FromExpansion { get; init; }
    }
}
