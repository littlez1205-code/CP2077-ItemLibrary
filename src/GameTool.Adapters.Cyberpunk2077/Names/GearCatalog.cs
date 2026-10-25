namespace GameTool.Adapters.Cyberpunk2077.Names;

public static class GearKinds
{
    public const string Clothing = "clothing";
    public const string Ranged = "ranged";
    public const string Melee = "melee";
    public const string Cyberware = "cyberware";
    public const string Grenade = "grenade";
    public const string ArmCyberware = "arm_cyberware";
    public const string Quickhack = "quickhack";
    public const string Mod = "mod";
    public const string Consumable = "consumable";
    public const string Ammo = "ammo";
    public const string Material = "material";
    public const string Recipe = "recipe";
    public const string Currency = "currency";

    /// <summary>界面上的分类顺序；装备在前，最后五类（消耗品、弹药、制作材料、配方、货币）是堆叠物品。</summary>
    public static readonly string[] Order = [Clothing, Ranged, Melee, Grenade, Cyberware, ArmCyberware, Quickhack, Mod, Consumable, Ammo, Material, Recipe, Currency];

    public static bool IsStackable(string kind) => kind is Consumable or Ammo or Material or Recipe or Currency;

    /// <summary>分类的中文名（界面分类标签、搜索词、配方的类型说明共用）。</summary>
    public static string Label(string kind) => kind switch
    {
        Clothing => "服装",
        Ranged => "远程武器",
        Melee => "近战武器",
        Grenade => "手雷",
        Cyberware => "义体",
        ArmCyberware => "手臂义体",
        Quickhack => "快速破解",
        Mod => "模组",
        Consumable => "消耗品",
        Ammo => "弹药",
        Material => "制作材料",
        Recipe => "配方",
        Currency => "货币",
        _ => kind
    };

    /// <summary>
    /// 不要求同类模板、只借任意可解析装备的前缀生成的类别（T021 起：近战武器、义体；T022：手雷、手臂义体、快速破解、模组）。
    /// 服装和远程武器仍按 C2b 第一批的规则选同类模板。
    /// </summary>
    public static bool UsesPrefixSource(string kind) => kind is Melee or Cyberware or Grenade or ArmCyberware or Quickhack or Mod;
}

/// <summary>品质（卡片顶边颜色）。tweak 里的 Plus / PlusPlus 归到同一档，Random 等不是固定品质的记为 null。</summary>
public static partial class GearQuality
{
    public const string Common = "Common";
    public const string Uncommon = "Uncommon";
    public const string Rare = "Rare";
    public const string Epic = "Epic";
    public const string Legendary = "Legendary";

    public static readonly string[] Known = [Common, Uncommon, Rare, Epic, Legendary];

    public static string? Normalize(string? tweakValue)
    {
        if (string.IsNullOrWhiteSpace(tweakValue))
            return null;
        var name = tweakValue.StartsWith("Quality.", StringComparison.Ordinal) ? tweakValue["Quality.".Length..] : tweakValue;
        if (name.EndsWith("PlusPlus", StringComparison.Ordinal))
            name = name[..^"PlusPlus".Length];
        else if (name.EndsWith("Plus", StringComparison.Ordinal))
            name = name[..^"Plus".Length];
        return Known.FirstOrDefault(item => item == name);
    }

    /// <summary>排序用：传说最前。</summary>
    public static int Rank(string? quality) => quality is null ? 0 : Array.IndexOf(Known, quality) + 1;

    /// <summary>带 Plus 档的细排序：普通 = 3、普通+ = 4 … 传说++ = 17；没有品质为 0。</summary>
    public static int Rank(string? quality, int plus) => quality is null ? 0 : (Rank(quality) * 3) + Math.Clamp(plus, 0, 2);

    /// <summary>tweak 值或名字里 Plus 的档数：…PlusPlus = 2，…Plus = 1，其余 0。</summary>
    public static int PlusOf(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        if (value.EndsWith("PlusPlus", StringComparison.OrdinalIgnoreCase))
            return 2;
        return value.EndsWith("Plus", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    public static string Word(string quality) => quality switch
    {
        Common => "普通",
        Uncommon => "罕见",
        Rare => "稀有",
        Epic => "史诗",
        Legendary => "传说",
        _ => quality
    };

    /// <summary>品阶标签：传说、史诗+、传说++。</summary>
    public static string TierLabel(string quality, int plus) => Word(quality) + new string('+', Math.Clamp(plus, 0, 2));

    /// <summary>
    /// 记录名末尾的品阶（T023）：<c>…Legendary</c>、<c>…EpicPlus</c>、<c>…_LegendaryPlusPlus</c>，
    /// 也认小写加下划线的写法（<c>w_att_scope_short_07_legendary</c>）。返回 (品质, Plus 档)，没有返回 null。
    /// </summary>
    public static (string Quality, int Plus)? FromNameSuffix(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        var name = ShortName(recordName);
        var match = SuffixPattern().Match(name);
        if (!match.Success)
            match = LowerSuffixPattern().Match(name);
        if (!match.Success || match.Index == 0)
            return null;
        var quality = Normalize(match.Groups[1].Value.Length == 0 ? null : Capitalize(match.Groups[1].Value));
        return quality is null ? null : (quality, PlusOf(match.Groups[2].Value));
    }

    /// <summary>
    /// 可制作标志性武器的前缀品阶（<c>Common_Igla_Sovereign</c> … <c>Legendary_Igla_Sovereign</c>）。
    /// <c>Preset_</c> 前缀（同名的原版）返回 ("Preset", 0)，调用方显示为「原版」、不当品质用。没有返回 null。
    /// </summary>
    public static (string Quality, int Plus)? FromPrefix(string recordName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        var name = ShortName(recordName);
        var underscore = name.IndexOf('_', StringComparison.Ordinal);
        if (underscore <= 0 || underscore == name.Length - 1)
            return null;
        var head = name[..underscore];
        if (head == PresetPrefix)
            return (PresetPrefix, 0);
        return Known.Contains(head, StringComparer.Ordinal) ? (head, 0) : null;
    }

    /// <summary>Preset_ 前缀在品阶分组里的标记。</summary>
    public const string PresetPrefix = "Preset";

    private static string ShortName(string recordName)
    {
        var dot = recordName.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 ? recordName[(dot + 1)..] : recordName;
    }

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    [System.Text.RegularExpressions.GeneratedRegex("(?<=[A-Za-z0-9_])(Uncommon|Common|Rare|Epic|Legendary)(PlusPlus|Plus)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SuffixPattern();

    [System.Text.RegularExpressions.GeneratedRegex("(?<=_)(uncommon|common|rare|epic|legendary)(plusplus|plus)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex LowerSuffixPattern();
}

/// <summary>
/// 可生成目录中的一条。LabelShared：同一分类里还有别的记录叫同一个中文名。
/// Quality 见 <see cref="GearQuality"/>；Iconic 为标志性物品；TypeLabel 是更细的中文类型（如 外衣、突击步枪），推不出来时为 null。
/// </summary>
public sealed record GearEntry(string RecordName, ulong TweakDbId, string Label, string Kind, bool LabelShared)
{
    public string TweakDbIdHex => Convert.ToHexString(BitConverter.GetBytes(TweakDbId)).ToLowerInvariant();

    public string? Quality { get; init; }

    public bool Iconic { get; init; }

    public string? TypeLabel { get; init; }

    /// <summary>
    /// 快速破解和模组（T022）：物品自己就是根配件，挂点取 placementSlots 第一项（存档实测：背包里的 6 件散装模组全是这样；
    /// 快速破解装在接入仓里时记的是所在槽位，散装的按第一项）。其他类别为 null。
    /// </summary>
    public string? RootSlot { get; init; }

    /// <summary>品阶里的 Plus 档（0 / 1 / 2），与 <see cref="Quality"/> 一起决定同名物品里谁最高。</summary>
    public int Plus { get; init; }

    /// <summary>记录名末尾的品阶（如 LegendaryPlus），没有为 null。</summary>
    public string? NameTier { get; init; }

    /// <summary>可制作标志性武器的前缀品阶（Common … Legendary，或 Preset 表示原版），没有为 null。</summary>
    public string? PrefixTier { get; init; }

    /// <summary>tweak 的 quality 字段（去掉 Quality. 前缀，保留 Plus，如 EpicPlus；Random 等为 null）。</summary>
    public string? FieldTier { get; init; }

    /// <summary>外观标签（visualTags，如 Default / Neon）。</summary>
    public IReadOnlyList<string> VisualTags { get; init; } = [];

    /// <summary>配方的产物记录（craftingResult.item），其他类别为 null。</summary>
    public string? CraftedItem { get; init; }

    /// <summary>合并卡片的键（分类 + 中文名），见 <see cref="GearGroup"/>。</summary>
    public string GroupKey { get; init; } = "";

    /// <summary>同名合并后在确认框里的选项标签；只有一条时为 null。</summary>
    public string? VariantLabel { get; init; }

    /// <summary>物品的背景说明文本。</summary>
    public string? Description { get; init; }

    /// <summary>物品的特殊技能/词条说明（如不朽专属效果、义体技能）。</summary>
    public string? EffectDesc { get; init; }

    /// <summary>物品的关键常数标签（如 [全自动]、[手枪弹药]、[义体容量 20]）。</summary>
    public IReadOnlyList<string> StatBadges { get; init; } = [];

    public bool Stackable => GearKinds.IsStackable(Kind);

    public int Rank => GearQuality.Rank(Quality, Plus);
}

/// <summary>
/// 同一分类里中文名相同的记录合并成一张卡（T023）。Variants 按品阶从低到高（款式按标签）排列，DefaultIndex 是默认选中的一条：
/// 品阶分组取最高品阶，款式分组取 Default 款，没有就取第一条。Noun 是「品阶」或「款式」，只有一条时为 null。
/// </summary>
public sealed record GearGroup(string Key, string Kind, string Label, IReadOnlyList<GearEntry> Variants, int DefaultIndex, string? Noun)
{
    public GearEntry Default => Variants[DefaultIndex];

    public bool Stackable => GearKinds.IsStackable(Kind);

    public static string KeyOf(string kind, string label) => kind + ":" + label;
}

/// <summary>生成功能需要的只读数据：可生成目录、任意记录的分类（选模板用）、目标的配件预设。</summary>
public interface IGearCatalog
{
    bool Available { get; }

    string? UnavailableMessage { get; }

    /// <summary>装备（非堆叠，按模板生成）。</summary>
    IReadOnlyList<GearEntry> Gear { get; }

    /// <summary>堆叠物品（按数量获得：已持有就加数量，没有就插入新堆叠）。</summary>
    IReadOnlyList<GearEntry> Stackables => [];

    /// <summary>同名合并后的卡片（装备和堆叠物品都在内，按分类、中文名排列）。</summary>
    IReadOnlyList<GearGroup> Groups => GearCatalog.Group(Gear.Concat(Stackables));

    /// <summary>只按继承链和配件预设分类，不看属性和中文名；用于判断存档里的物品能不能当模板。</summary>
    string? KindOf(ulong tweakDbId);

    IReadOnlyList<TweakPresetPart>? PresetParts(string recordName);

    /// <summary>没装往日之影而从目录里隐藏的卡片（D023）。</summary>
    IReadOnlyList<GearGroup> HiddenGroups => [];

    /// <summary>记录是因为没装往日之影而被隐藏的目录物品（D023）：生成时要拒绝并说明原因。</summary>
    bool IsHidden(string recordName) => false;
}

/// <summary>
/// 从 tweak 源推出可生成目录（C2b 第一批，基类在 RTDB 包）：
/// 服装 = 继承链到达 Clothing；远程武器 = 继承链到达 WeaponItem 且配件预设里有 AttachmentSlots.Receiver。
/// 第二批（T021）：近战武器 = 继承链到达 WeaponItem、配件预设里有 AttachmentSlots.Grip、没有 Receiver；
/// 义体 = 继承链到达 Items.Cyberware，配件预设正好是 GenericItemRoot 根（可再加 1 个属性碎片），见 <see cref="IsCyberwareShape"/>。
/// 第三批（T022）：义体操作系统并入义体（没有预设、安装部位是 SystemReplacementCW）；手雷 = WeaponItem 链 + itemType Gad_Grenade + GrenadeCore 预设；
/// 手臂义体 = WeaponItem 链 + CyberwareRoot 预设 + 安装部位 ArmsCW，只收升级链上的档位；
/// 快速破解 = itemType Prt_Program，模组 = 武器模组 / 瞄具 / 枪口装置的 itemType，两者都要有 placementSlots。
/// 堆叠物品见 <see cref="IsStackableRecord"/>，再按 itemType 分成消耗品、弹药、制作材料，带 Currency 标签的是货币。
/// 目录再排除带 notQueryable / Debug / DisabledContent / abstract / CPO 属性的记录、任务物品（Quest 标签）、内部物品和没有中文名的记录。
/// </summary>
public sealed partial class GearCatalog
{
    /// <summary>tweak 源里服装基类实际写在 RTDB 包（RTDB.Clothing），也接受 Items.Clothing。</summary>
    public static readonly string[] ClothingRoots = ["RTDB.Clothing", "Items.Clothing"];
    public static readonly string[] WeaponRoots = ["RTDB.WeaponItem", "Items.WeaponItem"];
    public const string ReceiverSlot = "AttachmentSlots.Receiver";
    public const string GripSlot = "AttachmentSlots.Grip";

    /// <summary>义体基类写在 Items 包（base_values.tweak 的 <c>Cyberware : Item</c>）。</summary>
    public static readonly string[] CyberwareRoots = ["Items.Cyberware"];
    public const string GenericItemRootSlot = "AttachmentSlots.GenericItemRoot";
    public const string GenericItemRoot = "Items.GenericItemRoot";
    public const string StatsShardSlot = "AttachmentSlots.StatsShardSlot";

    /// <summary>没装往日之影时生成往日之影物品的拒绝说明（D023）。</summary>
    public const string ExpansionHiddenMessage = "这件物品属于往日之影资料片，当前游戏目录未检测到往日之影。";

    // CPO 是多人模式原型残留（如 CPO_Base_Lexington），一并排除。
    private static readonly string[] Excluded = ["notQueryable", "Debug", "DisabledContent", "abstract", "CPO"];

    // 带这些标签的记录在存档里是带配件树的独立物品，不是 29 字节堆叠。
    private static readonly string[] UniqueTags = ["Cyberware", "itemPart", "Weapon", "Clothing", "base_fists"];

    private static readonly string[] ConsumableTypes = ["ItemType.Con_Edible", "ItemType.Con_Inhaler", "ItemType.Con_Injector", "ItemType.Con_LongLasting"];

    private readonly Dictionary<ulong, string> _kinds;

    private readonly HashSet<string> _hidden;

    private GearCatalog(IReadOnlyList<GearEntry> entries, IReadOnlyList<GearEntry> stackables, IReadOnlyList<GearGroup> groups, Dictionary<ulong, string> kinds, IReadOnlyList<GearGroup> hiddenGroups)
    {
        Entries = entries;
        Stackables = stackables;
        Groups = groups;
        _kinds = kinds;
        HiddenGroups = hiddenGroups;
        _hidden = hiddenGroups.SelectMany(group => group.Variants).Select(entry => entry.RecordName).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 没装往日之影时从目录里隐藏的往日之影物品（D023），按同样的规则合并成卡片；装了往日之影时为空。
    /// </summary>
    public IReadOnlyList<GearGroup> HiddenGroups { get; }

    /// <summary>记录是因为没装往日之影而被隐藏的目录物品。</summary>
    public bool IsHidden(string recordName) => _hidden.Contains(recordName);

    /// <summary>装备目录（服装、远程武器）。</summary>
    public IReadOnlyList<GearEntry> Entries { get; }

    /// <summary>堆叠物品目录（消耗品、弹药、制作材料、配方、货币）。</summary>
    public IReadOnlyList<GearEntry> Stackables { get; }

    /// <summary>同名合并后的卡片，见 <see cref="Group"/>。</summary>
    public IReadOnlyList<GearGroup> Groups { get; }

    public string? KindOf(ulong tweakDbId) => _kinds.TryGetValue(tweakDbId, out var kind) ? kind : null;

    /// <summary>
    /// hasIcon：记录能否解析到背包图标。给了它，带 notQueryable 的标志性远程武器（见 <see cref="IsObtainableIconic"/>）也进目录；
    /// 不给就和以前一样排除所有 notQueryable。
    /// </summary>
    public static GearCatalog Build(
        TweakSourceIndex index,
        Func<ulong, ItemName> resolve,
        Func<string?, bool> isInternal,
        Func<string, bool>? hasIcon = null,
        Func<string?, string?>? resolveText = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(isInternal);
        var kinds = new Dictionary<ulong, string>();
        var picked = new List<GearEntry>();
        var hidden = new List<GearEntry>();
        var classified = new List<(string Record, string Kind, bool Stackable)>();
        var kindByRecord = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in index.RecordNames)
        {
            var kind = Classify(index, record);
            if (kind is not null)
            {
                classified.Add((record, kind, false));
                continue;
            }

            kind = ClassifyStackable(index, record);
            if (kind is not null)
                classified.Add((record, kind, true));
        }

        foreach (var (record, kind, _) in classified)
            kindByRecord[record] = kind;

        // D023：没装往日之影时，往日之影记录不进目录，单独记下（用于报告和拒绝生成），也不能把本体记录带成升级档。
        Func<string, bool> expansion = index.ExpansionInstalled ? _ => false : index.IsExpansion;

        // 义体的升级档（…Plus / …PlusPlus）带 notQueryable，只能由低一档的 nextUpgradeItem 升上来；存档里实际就有（如 AdvancedRamUpgradeLegendaryPlusPlus）。
        // 手臂义体（T022）连最低档（如 AdvancedMantisBladesUncommon）也带 notQueryable，它们是升级链的起点：自己写了 nextUpgradeItem。
        // 往日之影的升级档（如 IconicBioConductorsRare → RarePlus）也照常认出来，只是归到隐藏的目录里。
        var allUpgradeTargets = UpgradeTargets(index, classified, _ => true);
        var baseUpgradeTargets = index.ExpansionInstalled ? allUpgradeTargets : UpgradeTargets(index, classified, record => !expansion(record));

        foreach (var (record, kind, stackable) in classified)
        {
            var id = TweakDbId.FromName(record);
            if (!stackable)
                kinds[id] = kind;
            var isExpansion = expansion(record);
            var upgradeTargets = isExpansion ? allUpgradeTargets : baseUpgradeTargets;
            var tags = index.TagsOf(record);
            var attributes = index.AttributesOf(record);
            var chainStart = !string.IsNullOrEmpty(index.OwnFieldOf(record, "nextUpgradeItem"));
            // 手臂义体只收升级链上的档位：基类、旧版（MantisBlades、StrongArmsEpic…）、NPC 配装和任务副本（Q306_orbital_cyberware）都不是。
            if (kind == GearKinds.ArmCyberware && !chainStart && !upgradeTargets.Contains(record))
                continue;
            if (kind is GearKinds.Quickhack or GearKinds.Mod && tags.Any(tag => HiddenPartTags.Contains(tag, StringComparer.Ordinal)))
                continue;
            var exception = false;
            if (attributes.Any(attribute => Excluded.Contains(attribute, StringComparer.OrdinalIgnoreCase)))
            {
                exception = hasIcon is not null && kind switch
                {
                    GearKinds.Ranged or GearKinds.Melee => IsObtainableIconic(record, attributes, tags),
                    // 网络接入仓的升级链起点也可以带 notQueryable（如配方做出的 HauntedCyberdeck_Rare，T023）；只放宽带 Cyberdeck 标签的。
                    GearKinds.Cyberware => IsUpgradeTier(record, attributes, upgradeTargets)
                        || (chainStart && IsOnlyNotQueryable(record, attributes) && tags.Contains(CyberdeckTag, StringComparer.Ordinal)),
                    // 其他义体的链起点不放宽：那里带 notQueryable 的起点多是 …2 这类第二副本（如 AdvancedTitaniumInfusedBonesCommon2），会让目录重复。
                    GearKinds.ArmCyberware => IsUpgradeTier(record, attributes, upgradeTargets) || (chainStart && IsOnlyNotQueryable(record, attributes)),
                    GearKinds.Grenade => IsPlayerGrenadeTier(record, attributes, index.FieldOf(record, "quality")),
                    GearKinds.Mod => IsOnlyNotQueryable(record, attributes) && tags.Contains(RetrievableTag, StringComparer.Ordinal),
                    _ => false
                };
                if (!exception)
                    continue;
            }

            if (isInternal(record))
                continue;
            if (stackable && tags.Contains("Quest", StringComparer.Ordinal))
                continue;
            var crafted = kind == GearKinds.Recipe ? NonEmpty(index.FieldOf(record, TweakSourceIndex.CraftingResultItem)) : null;
            var name = resolve(id);
            var text = name.Resolved && !string.IsNullOrWhiteSpace(name.Text) ? name.Text : null;
            // 武器模组的配方（如 ShotgunMod3_Epic_Recipe）自己没有 displayName，游戏里显示的是产物的名字。
            if (text is null && crafted is not null)
            {
                var product = resolve(TweakDbId.FromName(crafted));
                text = product.Resolved && !string.IsNullOrWhiteSpace(product.Text) ? product.Text : null;
            }

            // 没装往日之影时游戏目录里通常也没有 ep1 文本包，往日之影物品查不到中文名：有 displayName 的照样记进隐藏目录（用记录名当标签），
            // 这样报告的隐藏数接近实际，生成它们时也能给出往日之影的说明。
            if (text is null && isExpansion && !string.IsNullOrEmpty(name.DisplayName))
                text = ShortRecord(record);
            if (text is null)
                continue;
            if (exception && !hasIcon!(record))
                continue;
            var described = Describe(index, record, id, text, kind, tags, crafted, crafted is not null && kindByRecord.TryGetValue(crafted, out var made) ? made : null, resolveText);
            (isExpansion ? hidden : picked).Add(described);
        }

        var groups = Group(MarkShared(picked));
        // 目录顺序与合并卡片一致：分类、中文名，组内按品阶（或款式）排列。
        var ordered = groups.SelectMany(group => group.Variants).ToArray();
        return new GearCatalog(
            ordered.Where(item => !item.Stackable).ToArray(),
            ordered.Where(item => item.Stackable).ToArray(),
            groups,
            kinds,
            Group(MarkShared(hidden)));
    }

    private static HashSet<string> UpgradeTargets(TweakSourceIndex index, List<(string Record, string Kind, bool Stackable)> classified, Func<string, bool> source) =>
        classified
            .Where(item => item.Kind is GearKinds.Cyberware or GearKinds.ArmCyberware && source(item.Record))
            .Select(item => index.FieldOf(item.Record, "nextUpgradeItem"))
            .Where(value => !string.IsNullOrEmpty(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static GearEntry[] MarkShared(List<GearEntry> entries)
    {
        var shared = entries
            .GroupBy(item => (item.Kind, item.Label))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        return entries.Select(item => item with { LabelShared = shared.Contains((item.Kind, item.Label)) }).ToArray();
    }

    /// <summary>
    /// 一条目录记录的显示信息。品质（卡片颜色）按 记录名末尾品阶 → 前缀品阶 → quality 字段 取第一个有的（T023）；
    /// quality 是 Random 等不固定品质且名字里也没有品阶的保持 null（中性色）。
    /// 配方：名字和前缀都没有品阶时看产物的记录名和 quality（如 ShotgunMod3_Epic_Recipe → ShotgunMod3_Epic）。
    /// </summary>
    private static GearEntry Describe(
        TweakSourceIndex index,
        string record,
        ulong id,
        string label,
        string kind,
        IReadOnlyList<string> tags,
        string? crafted,
        string? craftedKind,
        Func<string?, string?>? resolveText)
    {
        var nameTier = GearQuality.FromNameSuffix(record);
        var prefixTier = GearQuality.FromPrefix(record);
        var fieldValue = index.FieldOf(record, "quality");
        if (crafted is not null && nameTier is null && prefixTier is null)
        {
            nameTier = GearQuality.FromNameSuffix(crafted) ?? PrefixQuality(crafted);
            fieldValue = index.FieldOf(crafted, "quality") ?? fieldValue;
        }

        var field = GearQuality.Normalize(fieldValue);
        // 快速破解的 ++ 档只写在名字里（OverheatLvl4PlusPlusProgram 的 quality 仍是 Legendary）。
        var fieldPlus = Math.Max(GearQuality.PlusOf(fieldValue), ProgramPlus(crafted ?? record));
        var fieldTier = field is null ? null : field + PlusWord(fieldPlus);
        var quality = nameTier ?? PrefixQuality(record);

        var descKey = index.LocalizedDescriptionOf(record);
        if (string.IsNullOrEmpty(descKey) && crafted is not null)
            descKey = index.LocalizedDescriptionOf(crafted);
        var description = CleanText(resolveText?.Invoke(descKey));

        var effectInfo = index.EffectInfoOf(record);
        if (effectInfo is null && crafted is not null)
            effectInfo = index.EffectInfoOf(crafted);

        var itemQuality = quality?.Quality ?? field;
        var itemPlus = quality?.Plus ?? (field is null ? 0 : fieldPlus);
        var qualityTier = itemQuality is not null ? Array.IndexOf(GearQuality.Known, itemQuality) : 0;
        if (qualityTier < 0) qualityTier = 0;
        if (qualityTier == 4 && itemPlus == 2) qualityTier = 5;

        IReadOnlyList<string?>? statValues = null;
        if (!string.IsNullOrEmpty(effectInfo?.Stats))
        {
            var statNames = effectInfo.Stats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var computed = new string?[statNames.Length];
            var targetRecord = effectInfo.SourceRecord ?? crafted ?? record;
            for (var i = 0; i < statNames.Length; i++)
            {
                var statName = statNames[i];
                var val = index.StatValueAtQuality(targetRecord, statName, qualityTier);
                if (val is null && targetRecord != record)
                    val = index.StatValueAtQuality(record, statName, qualityTier);

                if (val is not null)
                {
                    computed[i] = val.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    var shortName = statName.StartsWith("BaseStats.", StringComparison.Ordinal) ? statName["BaseStats.".Length..] : statName;
                    var directMod = index.StatModifierValueOf(targetRecord, shortName) ?? index.StatModifierValueOf(targetRecord, statName);
                    computed[i] = directMod;
                }
            }
            statValues = computed;
        }

        var effectDesc = FormatEffectText(resolveText?.Invoke(effectInfo?.LocKey), effectInfo?.IntValues, effectInfo?.FloatValues, statValues);

        if (description == effectDesc)
            description = null;

        var statBadges = BuildStatBadges(index, record, kind, crafted);

        return new GearEntry(record, id, label, kind, false)
        {
            Quality = quality?.Quality ?? field,
            Plus = quality?.Plus ?? (field is null ? 0 : fieldPlus),
            NameTier = nameTier is { } name ? name.Quality + PlusWord(name.Plus) : null,
            PrefixTier = prefixTier?.Quality,
            FieldTier = fieldTier,
            VisualTags = index.ListOf(record, "visualTags"),
            Iconic = index.MentionsIconicItem(record) || tags.Any(IsIconicTag),
            TypeLabel = kind == GearKinds.Recipe
                ? (craftedKind is null ? null : GearKinds.Label(craftedKind) + "配方")
                : TypeLabelOf(kind, index.FieldOf(record, "itemType"), tags, index.FieldOf(record, "equipArea")),
            RootSlot = kind is GearKinds.Quickhack or GearKinds.Mod ? index.ListOf(record, "placementSlots").FirstOrDefault() : null,
            CraftedItem = crafted,
            Description = description,
            EffectDesc = effectDesc,
            StatBadges = statBadges
        };
    }

    public static string? FormatEffectText(string? text, string? intValuesStr, string? floatValuesStr, IReadOnlyList<string?>? statValues = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var result = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\s*<Image[^>]*q000_tutorial_icon_boss[^>]*>(?:</>)?\s*",
            "骷髅头");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", "");
        result = result.Replace("\\n", "\n").Trim();

        var intValues = string.IsNullOrEmpty(intValuesStr)
            ? []
            : intValuesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var floatValues = string.IsNullOrEmpty(floatValuesStr)
            ? []
            : floatValuesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 1. 替换 {int_0}, {int_0 }, {int_1}, ...
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{int_(\d+)\s*\}", m =>
        {
            if (int.TryParse(m.Groups[1].Value, out var idx) && idx >= 0 && idx < intValues.Length)
                return intValues[idx];
            return "";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2. 替换 {float_0}, {float_0 }, {float_1}, ...
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{float_(\d+)\s*\}", m =>
        {
            if (int.TryParse(m.Groups[1].Value, out var idx) && idx >= 0 && idx < floatValues.Length)
                return floatValues[idx];
            return "";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 3. 替换 {stat_0}, {stat_1}, ...
        if (statValues is { Count: > 0 })
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\{stat_(\d+)\s*\}(%?)", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out var idx) && idx >= 0 && idx < statValues.Count && statValues[idx] is not null)
                {
                    var raw = statValues[idx]!;
                    var hasPercent = m.Groups[2].Success && m.Groups[2].Length > 0;
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
                    {
                        if (hasPercent && Math.Abs(num) > 0 && Math.Abs(num) < 1.0)
                            num *= 100.0;
                        var absNum = Math.Abs(num);
                        var formatted = absNum % 1 == 0
                            ? ((long)Math.Round(absNum)).ToString()
                            : absNum.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                        return formatted + (hasPercent ? "%" : "");
                    }
                    return raw + (hasPercent ? "%" : "");
                }
                return m.Value;
            }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // 4. 通用占位符 {value}, {value_0}, {value_1}, ...
        var primaryVal = floatValues.Length > 0 ? floatValues[0] : (intValues.Length > 0 ? intValues[0] : null);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{value(?:_(\d+))?\s*\}", m =>
        {
            if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var idx))
            {
                if (idx >= 0 && idx < floatValues.Length) return floatValues[idx];
                if (idx >= 0 && idx < intValues.Length) return intValues[idx];
            }
            return primaryVal ?? "";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 5. 数字占位符 {0}, {1}, ...
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{(\d+)\s*\}", m =>
        {
            if (int.TryParse(m.Groups[1].Value, out var idx))
            {
                if (idx >= 0 && idx < floatValues.Length) return floatValues[idx];
                if (idx >= 0 && idx < intValues.Length) return intValues[idx];
            }
            return "";
        });

        // 6. 清理义体同调（attunements）的当前运行时总量行（静态物品库中无需展示未装备时的动态总量计算）
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?m)^\s*当前总量[：:][^\r\n]*\r?\n?", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"当前总量[：:][^\r\n]*", "");

        // 7. 清理残留的未匹配代码占位符（如未提供具体数值的 {int_0}、{float_0}、{stat} 等），避免向玩家展示底层代码 token
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{\s*(?:int|float|value|VALUE|stat)\w*\s*\}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 8. 清理未匹配数值导致的裸露百分号（如 +%、-%、% 中毒几率）
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\d)[+-]?\s*%", "");

        // 9. 清理空行与多余空格
        result = System.Text.RegularExpressions.Regex.Replace(result, @"[ \t]{2,}", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?m)^\s+$", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(\r?\n){3,}", "\n\n");

        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static string? CleanText(string? text) => FormatEffectText(text, null, null);

    private static IReadOnlyList<string> BuildStatBadges(TweakSourceIndex index, string record, string kind, string? crafted)
    {
        var badges = new List<string>();
        var targetRecord = crafted ?? record;

        if (kind is GearKinds.Ranged or GearKinds.Melee)
        {
            // 武器机制（动能 / 技术 / 智能）
            var evo = index.FieldOf(targetRecord, "evolution");
            if (!string.IsNullOrEmpty(evo))
            {
                var evoName = evo.StartsWith("WeaponEvolution.", StringComparison.Ordinal) ? evo["WeaponEvolution.".Length..] : evo;
                var evoCn = evoName switch
                {
                    "Power" => "动能",
                    "Tech" => "技术",
                    "Smart" => "智能",
                    _ => null
                };
                if (evoCn is not null)
                    badges.Add(evoCn);
            }

            // 击发模式（全自动 / 半自动 / 连发 / 充能 / 蓄力）
            var mode = index.FieldOf(targetRecord, "primaryTriggerMode");
            if (!string.IsNullOrEmpty(mode))
            {
                var modeName = mode.StartsWith("TriggerMode.", StringComparison.Ordinal) ? mode["TriggerMode.".Length..] : mode;
                var modeCn = modeName switch
                {
                    "FullAuto" => "全自动",
                    "SemiAuto" => "半自动",
                    "Burst" => "连发",
                    "Charge" => "充能",
                    "Windup" => "蓄力",
                    _ => modeName
                };
                badges.Add(modeCn);
            }

            // 射速 / 攻速、弹匣、装弹
            if (kind == GearKinds.Ranged)
            {
                var cycleStr = index.StatModifierValueOf(targetRecord, "CycleTime");
                if (float.TryParse(cycleStr, System.Globalization.CultureInfo.InvariantCulture, out var cycle) && cycle > 0f)
                {
                    var rate = 1.0f / cycle;
                    badges.Add($"射速 {rate:0.#}发/秒");
                }

                // 弹丸数（如霰弹枪 8 弹丸）
                var projStr = index.StatModifierValueOf(targetRecord, "ProjectilesPerShot");
                if (float.TryParse(projStr, System.Globalization.CultureInfo.InvariantCulture, out var proj) && proj > 1.1f)
                {
                    badges.Add($"{(int)Math.Round(proj)}弹丸");
                }

                // 弹匣容量
                var magStr = index.StatModifierValueOf(targetRecord, "MagazineCapacity");
                if (float.TryParse(magStr, System.Globalization.CultureInfo.InvariantCulture, out var mag) && mag > 0f && mag < 9999f)
                {
                    badges.Add($"弹匣 {(int)Math.Round(mag)}");
                }

                // 装弹时间
                var reloadStr = index.FieldOf(targetRecord, "baseReloadTime") ?? index.StatModifierValueOf(targetRecord, "ReloadTime");
                if (float.TryParse(reloadStr, System.Globalization.CultureInfo.InvariantCulture, out var reload) && reload > 0f)
                {
                    badges.Add($"装弹 {reload:0.##}秒");
                }

                // 弹药类型
                var ammo = index.FieldOf(targetRecord, "ammo");
                if (!string.IsNullOrEmpty(ammo))
                {
                    var ammoName = ammo.StartsWith("Ammo.", StringComparison.Ordinal) ? ammo["Ammo.".Length..] : ammo;
                    var ammoCn = ammoName switch
                    {
                        "HandgunAmmo" => "手枪弹药",
                        "RifleAmmo" => "步枪弹药",
                        "ShotgunAmmo" => "霰弹弹药",
                        "SniperRifleAmmo" => "狙击弹药",
                        _ => null
                    };
                    if (ammoCn is not null)
                        badges.Add(ammoCn);
                }
            }
            else // 近战武器
            {
                var meleeDurStr = index.StatModifierValueOf(targetRecord, "MeleeAttackDuration");
                if (float.TryParse(meleeDurStr, System.Globalization.CultureInfo.InvariantCulture, out var meleeDur) && meleeDur > 0f)
                {
                    var speed = 1.0f / meleeDur;
                    badges.Add($"攻速 {speed:0.#}次/秒");
                }
                else
                {
                    var apsStr = index.StatModifierValueOf(targetRecord, "AttacksPerSecond");
                    if (float.TryParse(apsStr, System.Globalization.CultureInfo.InvariantCulture, out var aps) && aps > 0f)
                    {
                        badges.Add($"攻速 {aps:0.#}次/秒");
                    }
                }
            }
        }
        else if (kind is GearKinds.Cyberware or GearKinds.ArmCyberware)
        {
            var variants = index.ListOf(targetRecord, "variants");
            foreach (var v in variants)
            {
                var match = System.Text.RegularExpressions.Regex.Match(v, @"Humanity(\d+)Cost");
                if (match.Success)
                {
                    badges.Add($"义体容量 {match.Groups[1].Value}");
                    break;
                }
            }
        }

        return badges;
    }

    private static string PlusWord(int plus) => plus switch { 2 => "PlusPlus", 1 => "Plus", _ => "" };

    /// <summary>快速破解名字里 Program 前的 Plus 档（…Lvl4PlusPlusProgram → 2）。</summary>
    private static int ProgramPlus(string record) =>
        record.EndsWith("PlusPlusProgram", StringComparison.Ordinal) ? 2 : record.EndsWith("PlusProgram", StringComparison.Ordinal) ? 1 : 0;

    /// <summary>前缀品阶里真正的品质（Preset_ 不算）。</summary>
    private static (string Quality, int Plus)? PrefixQuality(string record) =>
        GearQuality.FromPrefix(record) is { } prefix && prefix.Quality != GearQuality.PresetPrefix ? prefix : null;

    /// <summary>标志性物品的标签：IconicWeapon（武器），以及 Iconic_ 开头的义体标签（如 Iconic_OS_CW、Iconic_Netdriver_Cyberdeck）。</summary>
    private static bool IsIconicTag(string tag) =>
        tag == "IconicWeapon" || tag.StartsWith("Iconic_", StringComparison.Ordinal);

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// 同一分类里中文名相同的记录合并成一张卡（T023）。选项标签按这个优先级取「整组都有、且互不相同」的第一种来源：
    /// 记录名末尾的品阶（普通 / 罕见 / 稀有 / 史诗 / 传说，带 + / ++）→ 可制作标志性武器的前缀品阶（Preset_ 显示为「原版」）→
    /// 款式（visualTags 里组内不共有的部分，Default 显示为「默认」）→ quality 字段的品阶（快速破解的 ++ 取自名字）。
    /// 都不行时每条取自己第一个有的品阶（没有的记为「标准」），重复的补上款式或记录名里不同的部分；
    /// 整组都没有品阶时用记录名（去掉组内共同的前后段）。
    /// </summary>
    public static IReadOnlyList<GearGroup> Group(IEnumerable<GearEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var groups = new List<GearGroup>();
        foreach (var bucket in entries
            .GroupBy(entry => (entry.Kind, entry.Label))
            .OrderBy(bucket => Array.IndexOf(GearKinds.Order, bucket.Key.Kind))
            .ThenBy(bucket => bucket.Key.Label, StringComparer.Ordinal))
        {
            var key = GearGroup.KeyOf(bucket.Key.Kind, bucket.Key.Label);
            var members = bucket.OrderBy(entry => entry.RecordName, StringComparer.Ordinal).ToArray();
            if (members.Length == 1)
            {
                groups.Add(new GearGroup(key, bucket.Key.Kind, bucket.Key.Label, [members[0] with { GroupKey = key, VariantLabel = null }], 0, null));
                continue;
            }

            var (labels, tiered) = VariantLabels(members);
            var labelled = members.Select((entry, i) => entry with { GroupKey = key, VariantLabel = labels[i] }).ToArray();
            var ordered = tiered
                ? labelled.OrderBy(entry => entry.PrefixTier == GearQuality.PresetPrefix ? -1 : entry.Rank).ThenBy(entry => entry.RecordName, StringComparer.Ordinal).ToArray()
                : labelled.OrderBy(entry => entry.VariantLabel == "默认" || entry.VisualTags.Contains("Default", StringComparer.Ordinal) ? 0 : 1).ThenBy(entry => entry.VariantLabel, StringComparer.Ordinal).ToArray();
            var defaultIndex = tiered
                ? Array.IndexOf(ordered, ordered.OrderByDescending(entry => entry.Rank).ThenBy(entry => entry.PrefixTier == GearQuality.PresetPrefix ? 1 : 0).First())
                : 0;
            groups.Add(new GearGroup(key, bucket.Key.Kind, bucket.Key.Label, ordered, defaultIndex, tiered ? "品阶" : "款式"));
        }

        return groups;
    }

    private static (string[] Labels, bool Tiered) VariantLabels(GearEntry[] members)
    {
        var commonTags = members.Skip(1).Aggregate(
            new HashSet<string>(members[0].VisualTags, StringComparer.Ordinal),
            (set, entry) =>
            {
                set.IntersectWith(entry.VisualTags);
                return set;
            });
        var styles = members.Select(entry => StyleLabel(entry.VisualTags.Where(tag => !commonTags.Contains(tag)).ToArray())).ToArray();
        var records = DiffLabels(members.Select(entry => ShortRecord(entry.RecordName)).ToArray());
        string?[] nameTiers = members.Select(entry => entry.NameTier is { } tier ? TierText(tier) : null).ToArray();
        // Preset_（原版）只有在组里还有 Common_ … Legendary_ 前缀的可制作版本时才算一档；整组都是 Preset_ 时不看前缀。
        var realPrefix = members.Any(entry => entry.PrefixTier is { } prefix && prefix != GearQuality.PresetPrefix);
        string?[] prefixTiers = members
            .Select(entry => realPrefix && entry.PrefixTier is { } prefix ? (prefix == GearQuality.PresetPrefix ? "原版" : GearQuality.Word(prefix)) : null)
            .ToArray();
        string?[] fieldTiers = members.Select(entry => entry.FieldTier is { } tier ? TierText(tier) : null).ToArray();
        (string?[] Labels, bool Tiered)[] sources = [(nameTiers, true), (prefixTiers, true), (styles, false), (fieldTiers, true)];
        foreach (var (labels, tiered) in sources)
        {
            if (labels.All(label => !string.IsNullOrEmpty(label)) && labels.Distinct(StringComparer.Ordinal).Count() == labels.Length)
                return (labels.Select(label => label!).ToArray(), tiered);
        }

        // 没有一种来源能单独区分整组时：每条取自己第一个有的品阶（名字 → 前缀 → quality 字段），
        // 组里有别的条目带品阶、自己没有的记为「标准」（如 w_muzzle_brake_10 与 w_muzzle_brake_10_legendary）；
        // 仍然重复的补上款式，再不行补记录名里不同的部分。整组都没有品阶时用记录名（第 4 优先级）。
        var tiers = Enumerable.Range(0, members.Length).Select(i => nameTiers[i] ?? prefixTiers[i] ?? fieldTiers[i]).ToArray();
        if (tiers.All(tier => tier is null))
            return (records.Select(StyleWord).ToArray(), false);
        var mixed = tiers.Select(tier => tier ?? "标准").ToArray();
        Disambiguate(mixed, i => styles[i]);
        Disambiguate(mixed, i => ShortRecord(members[i].RecordName), diff: true);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < mixed.Length; i++)
        {
            // 记录名唯一，走到这里不该还有重复；按序号兜底。
            var n = 2;
            var label = mixed[i];
            while (!seen.Add(label))
                label = mixed[i] + " #" + n++;
            mixed[i] = label;
        }

        return (mixed, true);
    }

    /// <summary>
    /// 重复的标签补上说明：extra 给出每条的说明；diff 为真时只取重复的几条之间不同的部分（见 <see cref="DiffLabels"/>），
    /// 说明为空的那条保持原样（如 史诗+ 与 史诗+ · Regina）。
    /// </summary>
    private static void Disambiguate(string[] labels, Func<int, string?> extra, bool diff = false)
    {
        foreach (var duplicate in Enumerable.Range(0, labels.Length).GroupBy(i => labels[i], StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray())
        {
            var indexes = duplicate.ToArray();
            var notes = indexes.Select(extra).ToArray();
            if (diff)
                notes = DiffLabels(notes.Select(note => note ?? "").ToArray(), keepEmpty: true);
            for (var k = 0; k < indexes.Length; k++)
            {
                if (!string.IsNullOrEmpty(notes[k]))
                    labels[indexes[k]] = labels[indexes[k]] + " · " + StyleWord(notes[k]!);
            }
        }
    }

    private static string StyleWord(string word) => word == "Default" ? "默认" : word;

    private static string TierText(string tier)
    {
        var plus = GearQuality.PlusOf(tier);
        var quality = plus switch { 2 => tier[..^"PlusPlus".Length], 1 => tier[..^"Plus".Length], _ => tier };
        return GearQuality.TierLabel(quality, plus);
    }

    private static string? StyleLabel(IReadOnlyList<string> tags) =>
        tags.Count == 0 ? null : string.Join(" / ", tags.Select(StyleWord));

    /// <summary>
    /// 去掉几个名字共同的开头和结尾，只留不同的部分；切口退到词边界（下划线后、大写字母或数字与字母交界处），
    /// 两头的下划线去掉。剩下为空时：keepEmpty 为真返回空串，否则返回名字的最后一段（Preset_Masamune_Rogue / …_Rogue_Stash → Rogue / Stash）。
    /// 例：Preset_Knife_Default / Preset_Knife_Military → Default / Military；RecipeMemoryBooster / RecipeStaminaBooster → Memory / Stamina。
    /// </summary>
    public static string[] DiffLabels(string[] names, bool keepEmpty = false)
    {
        if (names.Length < 2)
            return names.ToArray();
        var shortest = names.Min(name => name.Length);
        var head = 0;
        while (head < shortest && names.All(name => name[head] == names[0][head]))
            head++;
        while (head > 0 && !names.All(name => IsBoundary(name, head)))
            head--;
        var tail = 0;
        while (tail < shortest - head && names.All(name => name[name.Length - 1 - tail] == names[0][names[0].Length - 1 - tail]))
            tail++;
        while (tail > 0 && !names.All(name => IsBoundary(name, name.Length - tail)))
            tail--;
        return names.Select(name =>
        {
            var middle = name[head..(name.Length - tail)].Trim('_');
            return middle.Length > 0 || keepEmpty ? middle : name.Split('_')[^1];
        }).ToArray();
    }

    /// <summary>位置 i 是不是词边界：开头结尾、下划线后或下划线本身、大写字母开头、数字与字母交界。</summary>
    private static bool IsBoundary(string name, int i)
    {
        if (i <= 0 || i >= name.Length)
            return true;
        var previous = name[i - 1];
        var current = name[i];
        return previous == '_' || current == '_' || char.IsUpper(current) || char.IsDigit(previous) != char.IsDigit(current);
    }
    private static string ShortRecord(string record)
    {
        var dot = record.IndexOf('.', StringComparison.Ordinal);
        return dot >= 0 ? record[(dot + 1)..] : record;
    }

    /// <summary>
    /// 带 notQueryable 的远程武器里，哪些是玩家能拿到的标志性武器（T020，总监要求目录覆盖全部可获得物品）：
    /// 属性只有 notQueryable（可带 EP1），没有 Debug / DisabledContent / abstract / CPO；在 Items 包里；
    /// 标签有 IconicWeapon、没有 DeprecatedIconic（2.0 以前的品质分档，游戏里已拿不到）；
    /// 不是敌人配装（Weak_/Avg_/Normal_/Hard_/VHard_ 加难度数字开头）、任务流程里的副本（q/mq/sq 编号开头、Preset_Q 编号）、
    /// 以及 NPC / 首领 / 载具 / 狙击巢 / 全息 / 左手副枪这类变体。
    /// 中文名和背包图标在 <see cref="Build"/> 里另外检查。
    /// </summary>
    public static bool IsObtainableIconic(string record, IReadOnlyList<string> attributes, IReadOnlyList<string> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(tags);
        if (!attributes.Contains("notQueryable", StringComparer.OrdinalIgnoreCase))
            return false;
        if (attributes.Any(attribute => !attribute.Equals("notQueryable", StringComparison.OrdinalIgnoreCase)
                && Excluded.Contains(attribute, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (!record.StartsWith("Items.", StringComparison.Ordinal))
            return false;
        if (!tags.Contains("IconicWeapon", StringComparer.Ordinal) || tags.Contains("DeprecatedIconic", StringComparer.Ordinal))
            return false;
        var name = record["Items.".Length..];
        if (NpcLoadoutPattern().IsMatch(name) || QuestCopyPattern().IsMatch(name))
            return false;
        return !NonPlayerMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 带 notQueryable 的义体里，哪些是升级档（T021）：属性只有 notQueryable（可带 EP1），在 Items 包里，
    /// 并且是某件义体的 nextUpgradeItem（游戏里在义体医生处升级得到）。中文名和背包图标在 <see cref="Build"/> 里另外检查。
    /// </summary>
    public static bool IsUpgradeTier(string record, IReadOnlyList<string> attributes, IReadOnlySet<string> upgradeTargets)
    {
        ArgumentNullException.ThrowIfNull(upgradeTargets);
        return IsOnlyNotQueryable(record, attributes) && upgradeTargets.Contains(record);
    }

    /// <summary>
    /// 排除属性里只有 notQueryable（可带 EP1），没有 Debug / DisabledContent / abstract / CPO，并且在 Items 包里。
    /// 这是各类 notQueryable 例外（升级档、手雷档位、可回收模组）共同的前提。
    /// </summary>
    public static bool IsOnlyNotQueryable(string record, IReadOnlyList<string> attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record);
        ArgumentNullException.ThrowIfNull(attributes);
        if (!attributes.Contains("notQueryable", StringComparer.OrdinalIgnoreCase))
            return false;
        if (attributes.Any(attribute => !attribute.Equals("notQueryable", StringComparison.OrdinalIgnoreCase)
                && Excluded.Contains(attribute, StringComparer.OrdinalIgnoreCase)))
            return false;
        return record.StartsWith("Items.", StringComparison.Ordinal);
    }

    /// <summary>
    /// 带 notQueryable 的手雷里，哪些是玩家能拿到的档位（T022）：品质是 …Plus（2.x 制作出的档位，如 GrenadeSmokeLegendaryPlus、
    /// CuttingGrenadeLegendaryPlus，存档里实际持有）。不带 Plus 的 Regular / Epic / Legendary、NPC 版、Hack 版、帮派配装都不是。
    /// </summary>
    public static bool IsPlayerGrenadeTier(string record, IReadOnlyList<string> attributes, string? quality) =>
        IsOnlyNotQueryable(record, attributes) && quality is not null && quality.EndsWith("Plus", StringComparison.Ordinal);

    /// <summary>
    /// 义体操作系统（斯安威斯坦、狂暴、网络接入仓等，T022）：没有 slotPartListPreset，蓝图根是 GenericItemRoot；
    /// 存档实测（AdvancedSandevistanApogee、AdvancedBerserkC4MK5、AdvancedNetwatchNetdriverMKLegendaryPlus）都是只有 GenericItemRoot 根的 91 字节。
    /// 归入「义体」分类，类型显示「操作系统」。
    /// </summary>
    public const string SystemReplacementArea = "EquipmentArea.SystemReplacementCW";

    public const string ArmsArea = "EquipmentArea.ArmsCW";
    public const string CyberwareRootSlot = "AttachmentSlots.CyberwareRoot";
    public const string GrenadeCoreSlot = "AttachmentSlots.GrenadeCore";
    private const string RetrievableTag = "Retrievable";

    /// <summary>网络接入仓（义体操作系统的一种，基类 Items.BaseDeck 带这个标签）。</summary>
    public const string CyberdeckTag = "Cyberdeck";

    /// <summary>配方基类（schema.tweak 的 <c>ItemRecipe : Item</c>，在 RTDB 包）。</summary>
    public static readonly string[] RecipeRoots = ["RTDB.ItemRecipe", "Items.ItemRecipe"];

    // 隐藏 / 占位的配件（DummyPart、HideInUI）和 2.0 弃用的快速破解不进目录。
    private static readonly string[] HiddenPartTags = ["DummyPart", "HideInUI", "DeprecatedQuickhack"];

    private const string ProgramType = "ItemType.Prt_Program";
    private const string GrenadeType = "ItemType.Gad_Grenade";

    /// <summary>
    /// 武器模组与配件（瞄具、枪口装置）的 itemType。服装模组（Prt_*FabricEnhancer）2.0 起已弃用（全部 notQueryable，挂在 DeprecatedFabricEnhancerBase 下，服装也没有模组槽），不收。
    /// </summary>
    private static readonly Dictionary<string, string> ModTypes = new(StringComparer.Ordinal)
    {
        ["ItemType.Prt_Mod"] = "武器模组",
        ["ItemType.Prt_RangedMod"] = "远程武器模组",
        ["ItemType.Prt_PowerMod"] = "威力武器模组",
        ["ItemType.Prt_TechMod"] = "技术武器模组",
        ["ItemType.Prt_SmartMod"] = "智能武器模组",
        ["ItemType.Prt_AR_SMG_LMGMod"] = "步枪 / 冲锋枪 / 机枪模组",
        ["ItemType.Prt_HandgunMod"] = "手枪模组",
        ["ItemType.Prt_ShotgunMod"] = "霰弹枪模组",
        ["ItemType.Prt_Precision_Sniper_RifleMod"] = "精确 / 狙击步枪模组",
        ["ItemType.Prt_MeleeMod"] = "近战武器模组",
        ["ItemType.Prt_BladeMod"] = "利刃武器模组",
        ["ItemType.Prt_BluntMod"] = "钝器模组",
        ["ItemType.Prt_ThrowableMod"] = "投掷武器模组",
        ["ItemType.Prt_ShortScope"] = "短程瞄具",
        ["ItemType.Prt_LongScope"] = "远程瞄具",
        ["ItemType.Prt_TechSniperScope"] = "狙击瞄具",
        ["ItemType.Prt_PowerSniperScope"] = "狙击瞄具",
        ["ItemType.Prt_Muzzle"] = "枪口装置",
        ["ItemType.Prt_RifleMuzzle"] = "枪口装置",
        ["ItemType.Prt_HandgunMuzzle"] = "枪口装置"
    };

    /// <summary>
    /// 手臂义体（螳螂刀、单分子线、大猩猩手臂、射弹发射系统，T022）：配件预设第一条是 CyberwareRoot 挂点上的根（如 Items.MantisBladesRoot）。
    /// 存档实测 200 / 208 字节：根 + 刃 / 线缆 / 指节 + 属性碎片，子配件按预设顺序。
    /// </summary>
    public static bool IsArmCyberwareShape(IReadOnlyList<TweakPresetPart>? parts) =>
        parts is { Count: > 0 } && parts[0].Slot == CyberwareRootSlot && parts[0].Item.Length > 0;

    /// <summary>手雷：配件预设第一条是 GrenadeCore 挂点上的弹芯（如 Items.w_frag_core），可再跟投掷方式（GrenadeDelivery）。</summary>
    public static bool IsGrenadeShape(IReadOnlyList<TweakPresetPart>? parts) =>
        parts is { Count: > 0 } && parts[0].Slot == GrenadeCoreSlot && parts[0].Item.Length > 0;

    /// <summary>
    /// 义体能按 GenericItemRoot 形态生成（存档实测 141 / 91 字节）：配件预设第一条是 GenericItemRoot 挂点上的 Items.GenericItemRoot，
    /// 其余至多 1 条、挂在 StatsShardSlot 上且有配件。手臂义体（CyberwareRoot + 刃 / 线缆等）、没有预设的基类都不算。
    /// </summary>
    public static bool IsCyberwareShape(IReadOnlyList<TweakPresetPart>? parts)
    {
        if (parts is null || parts.Count is < 1 or > 2)
            return false;
        if (parts[0].Slot != GenericItemRootSlot || parts[0].Item != GenericItemRoot)
            return false;
        return parts.Count == 1 || (parts[1].Slot == StatsShardSlot && parts[1].Item.Length > 0);
    }

    private static readonly string[] NonPlayerMarkers =
        ["NPC", "Boss", "Vehicle", "Sniper_Nest", "Hologram", "Left_Hand", "Dummy", "Turret", "Panzer", "Invisible", "Mounted"];

    [System.Text.RegularExpressions.GeneratedRegex("^(?:Weak|Avg|Normal|Hard|VHard)_[0-9]+_", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex NpcLoadoutPattern();

    [System.Text.RegularExpressions.GeneratedRegex("^(?:(?:q|mq|sq)[0-9]+_|Preset_Q[0-9]+_)", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex QuestCopyPattern();

    /// <summary>
    /// 存档里是不是 29 字节的堆叠物品（ManualSave-10 实测：玩家背包 332 个堆叠全部符合，背包和储物箱 631 件装备全部不符合）：
    /// 继承链不经过服装和武器基类，itemType（沿继承链，RTDB.Item 默认 Gen_Misc）是 Con_* 或 Gen_*，或者是空串
    /// （RTDB.ConsumableItem 把 itemType 置空，直接继承它的任务物品就是这样），
    /// 并且没有 Cyberware / itemPart / Weapon / Clothing / base_fists 标签。
    /// </summary>
    public static bool IsStackableRecord(TweakSourceIndex index, string record)
    {
        ArgumentNullException.ThrowIfNull(index);
        var chain = index.Ancestors(record);
        if (chain.Count == 0)
            return false;
        if (chain.Any(name => ClothingRoots.Contains(name, StringComparer.Ordinal) || WeaponRoots.Contains(name, StringComparer.Ordinal)))
            return false;
        var type = index.FieldOf(record, "itemType");
        if (type is null || !(type.Length == 0 || type.StartsWith("ItemType.Con_", StringComparison.Ordinal) || type.StartsWith("ItemType.Gen_", StringComparison.Ordinal)))
            return false;
        return !index.TagsOf(record).Any(tag => UniqueTags.Contains(tag, StringComparer.Ordinal));
    }

    private static string? Classify(TweakSourceIndex index, string record)
    {
        var chain = index.Ancestors(record);
        if (chain.Count < 2)
            return null;
        if (chain.Any(name => ClothingRoots.Contains(name, StringComparer.Ordinal)))
            return ClothingRoots.Contains(record, StringComparer.Ordinal) ? null : GearKinds.Clothing;
        if (chain.Any(name => CyberwareRoots.Contains(name, StringComparer.Ordinal)))
        {
            if (CyberwareRoots.Contains(record, StringComparer.Ordinal))
                return null;
            var preset = index.FindPresetParts(record);
            if (IsCyberwareShape(preset))
                return GearKinds.Cyberware;
            // 操作系统：没有配件预设，按 GenericItemRoot 根生成（见 SystemReplacementArea）。
            return preset is { Count: 0 } && index.FieldOf(record, "equipArea") == SystemReplacementArea ? GearKinds.Cyberware : null;
        }

        // 快速破解、模组和配件是自成一件的配件物品（继承链到 Items.Part / ItemWeaponChild，不经过 WeaponItem），按 itemType 认。
        var type = index.FieldOf(record, "itemType");
        if (type == ProgramType || (type is not null && ModTypes.ContainsKey(type)))
        {
            if (index.ListOf(record, "placementSlots").Count == 0)
                return null;
            return type == ProgramType ? GearKinds.Quickhack : GearKinds.Mod;
        }

        if (!chain.Any(name => WeaponRoots.Contains(name, StringComparer.Ordinal)) || WeaponRoots.Contains(record, StringComparer.Ordinal))
            return null;
        var parts = index.FindPresetParts(record);
        if (parts is null)
            return null;
        if (IsArmCyberwareShape(parts))
            return index.FieldOf(record, "equipArea") == ArmsArea ? GearKinds.ArmCyberware : null;
        if (type == GrenadeType)
            return IsGrenadeShape(parts) ? GearKinds.Grenade : null;
        if (parts.Any(part => part.Slot == ReceiverSlot))
            return GearKinds.Ranged;
        return parts.Any(part => part.Slot == GripSlot && part.Item.Length > 0) ? GearKinds.Melee : null;
    }

    private static string? ClassifyStackable(TweakSourceIndex index, string record)
    {
        var chain = index.Ancestors(record);
        if (chain.Count < 2 || !IsStackableRecord(index, record))
            return null;
        // 配方（T023）：存档里是 29 字节堆叠（ManualSave-8 实测 RecipeGrenadeIncendiaryLegendary、ShotgunMod3_Epic_Recipe 等）。
        if (chain.Any(name => RecipeRoots.Contains(name, StringComparer.Ordinal)))
            return RecipeRoots.Contains(record, StringComparer.Ordinal) ? null : GearKinds.Recipe;
        var type = index.FieldOf(record, "itemType");
        if (ConsumableTypes.Contains(type, StringComparer.Ordinal))
            return GearKinds.Consumable;
        if (type == "ItemType.Con_Ammo")
            return GearKinds.Ammo;
        if (type == "ItemType.Gen_CraftingMaterial")
            return GearKinds.Material;
        return index.TagsOf(record).Contains("Currency", StringComparer.Ordinal) ? GearKinds.Currency : null;
    }

    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.Ordinal)
    {
        ["ItemType.Clo_Head"] = "头部",
        ["ItemType.Clo_Face"] = "面部",
        ["ItemType.Clo_OuterChest"] = "外衣",
        ["ItemType.Clo_InnerChest"] = "内衣",
        ["ItemType.Clo_Legs"] = "下装",
        ["ItemType.Clo_Feet"] = "鞋履",
        ["ItemType.Clo_Outfit"] = "套装",
        ["ItemType.Wea_Handgun"] = "手枪",
        ["ItemType.Wea_Revolver"] = "左轮手枪",
        ["ItemType.Wea_Rifle"] = "步枪",
        ["ItemType.Wea_AssaultRifle"] = "突击步枪",
        ["ItemType.Wea_PrecisionRifle"] = "精确步枪",
        ["ItemType.Wea_SniperRifle"] = "狙击步枪",
        ["ItemType.Wea_SubmachineGun"] = "冲锋枪",
        ["ItemType.Wea_LightMachineGun"] = "轻机枪",
        ["ItemType.Wea_HeavyMachineGun"] = "重机枪",
        ["ItemType.Wea_Shotgun"] = "霰弹枪",
        ["ItemType.Wea_ShotgunDual"] = "双管霰弹枪",
        ["ItemType.Wea_GrenadeLauncher"] = "榴弹发射器",
        ["ItemType.Wea_Katana"] = "武士刀",
        ["ItemType.Wea_Sword"] = "剑",
        ["ItemType.Wea_LongBlade"] = "长刃",
        ["ItemType.Wea_ShortBlade"] = "短刃",
        ["ItemType.Wea_Knife"] = "刀",
        ["ItemType.Wea_Machete"] = "砍刀",
        ["ItemType.Wea_Axe"] = "斧",
        ["ItemType.Wea_Chainsword"] = "链锯剑",
        ["ItemType.Wea_Hammer"] = "锤",
        ["ItemType.Wea_OneHandedClub"] = "单手钝器",
        ["ItemType.Wea_TwoHandedClub"] = "双手钝器",
        ["ItemType.Wea_Fists"] = "拳套",
        ["ItemType.Wea_Melee"] = "近战武器",
        ["ItemType.Con_Inhaler"] = "吸入器",
        ["ItemType.Con_Injector"] = "注射器",
        ["ItemType.Con_LongLasting"] = "增益药剂",
        ["ItemType.Con_Ammo"] = "弹药",
        ["ItemType.Cyb_MantisBlades"] = "螳螂刀",
        ["ItemType.Cyb_NanoWires"] = "单分子线",
        ["ItemType.Cyb_StrongArms"] = "大猩猩手臂",
        ["ItemType.Cyb_Launcher"] = "射弹发射系统"
    };

    /// <summary>义体按安装部位（equipArea）细分。</summary>
    private static readonly Dictionary<string, string> CyberwareAreas = new(StringComparer.Ordinal)
    {
        ["EquipmentArea.FrontalCortexCW"] = "前额叶皮质",
        ["EquipmentArea.EyesCW"] = "视觉系统",
        ["EquipmentArea.CardiovascularSystemCW"] = "循环系统",
        ["EquipmentArea.ImmuneSystemCW"] = "免疫系统",
        ["EquipmentArea.NervousSystemCW"] = "神经系统",
        ["EquipmentArea.IntegumentarySystemCW"] = "皮肤系统",
        ["EquipmentArea.MusculoskeletalSystemCW"] = "骨骼系统",
        ["EquipmentArea.SystemReplacementCW"] = "操作系统",
        ["EquipmentArea.HandsCW"] = "手部",
        ["EquipmentArea.ArmsCW"] = "手臂",
        ["EquipmentArea.LegsCW"] = "腿部"
    };

    private static string? TypeLabelOf(string kind, string? itemType, IReadOnlyList<string> tags, string? equipArea)
    {
        if (kind == GearKinds.Cyberware)
        {
            if (tags.Contains(CyberdeckTag, StringComparer.Ordinal))
                return "网络接入仓";
            return equipArea is not null && CyberwareAreas.TryGetValue(equipArea, out var area) ? area : null;
        }
        if (kind == GearKinds.Mod)
            return itemType is not null && ModTypes.TryGetValue(itemType, out var mod) ? mod : null;
        if (kind is GearKinds.Grenade or GearKinds.Quickhack)
            return null;
        if (kind == GearKinds.Currency)
            return "货币";
        if (kind == GearKinds.Material)
            return tags.Contains("QuickhackCraftingMaterials", StringComparer.Ordinal) ? "快速破解组件" : "制作组件";
        if (itemType == "ItemType.Con_Edible")
        {
            if (tags.Contains("Alcohol", StringComparer.Ordinal))
                return "酒类";
            if (tags.Contains("Drink", StringComparer.Ordinal))
                return "饮料";
            return "食物";
        }

        return itemType is not null && TypeLabels.TryGetValue(itemType, out var label) ? label : null;
    }
}
