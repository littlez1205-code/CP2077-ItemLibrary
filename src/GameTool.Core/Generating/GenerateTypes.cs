namespace GameTool.Core;

/// <summary>凭目录里的目标 ID 生成新物品。Target 是适配器目录里的键，内核不解析。ToStash 为 true 时追加到储藏室，否则追加到玩家背包。</summary>
public sealed record GenerateRequest(string Target, int Copies, bool ToStash = false);

/// <summary>
/// 可生成目录的分类。Id 由适配器定义（如 clothing），Label 给界面显示。
/// Stackable 表示这一类是堆叠物品（按数量获得：已持有就加数量，没有就插入新堆叠）。
/// </summary>
public sealed record GenerateKind(string Id, string Label, bool Stackable = false);

/// <summary>
/// 一个分类里因为游戏缺少资料片而隐藏的物品（D023，如没装往日之影）：Groups 是卡片数，Items 是条目数。
/// </summary>
public sealed record GenerateHiddenKind(string Kind, string Label, int Groups, int Items);

/// <summary>
/// 目录中的一条。Kind 对应 <see cref="GenerateKind.Id"/>；Stackable 与所属分类一致。
/// LabelShared 表示同一分类里还有别的条目同名（界面据此显示 RecordName）；Owned 表示存档里已经有这件物品。
/// </summary>
public sealed record GenerateCatalogItem(
    string Target,
    string Label,
    string RecordName,
    string Kind,
    bool Stackable,
    string TweakDbIdHex,
    bool LabelShared,
    bool Owned)
{
    /// <summary>品质：Common / Uncommon / Rare / Epic / Legendary；不是固定品质时为 null。</summary>
    public string? Quality { get; init; }

    /// <summary>标志性物品。</summary>
    public bool Iconic { get; init; }

    /// <summary>更细的中文类型（如 外衣、突击步枪、饮料），推不出来时为 null。</summary>
    public string? TypeLabel { get; init; }

    /// <summary>堆叠物品：玩家背包里现有的数量（多个堆叠时为总和），未持有为 null。装备恒为 null。</summary>
    public long? Held { get; init; }

    /// <summary>堆叠物品：获得后的数量上限。装备为 null。</summary>
    public long? MaxQuantity { get; init; }

    /// <summary>品阶里的 Plus 档（0 / 1 / 2），界面在品质名后加 + / ++。</summary>
    public int Plus { get; init; }

    /// <summary>同名合并后，这一条在确认框里的选项标签（如 传说+、原版、Neon）；所在卡片只有一条时为 null。</summary>
    public string? VariantLabel { get; init; }

    /// <summary>物品的背景文本说明（官方本地化文本），没有时为 null。</summary>
    public string? Description { get; init; }

    /// <summary>物品的特殊效果/词条（如不朽武器特效、义体技能），没有时为 null。</summary>
    public string? EffectDesc { get; init; }

    /// <summary>物品的关键常数标签（如 [全自动]、[手枪弹药]、[义体容量 20]）。</summary>
    public IReadOnlyList<string> StatBadges { get; init; } = [];
}

public sealed record GenerateCatalogPage(int Total, IReadOnlyList<GenerateCatalogItem> Items);

/// <summary>
/// 同名合并后的一张卡（T023）：同一分类里中文名相同的条目。Variants 按品阶从低到高（或按款式）排列，
/// DefaultIndex 是默认选中的一条（最高品阶）。Quality / Iconic 取默认条目的，用于卡片边框颜色。
/// VariantNoun 是「品阶」或「款式」，只有一条时为 null。
/// </summary>
public sealed record GenerateCatalogGroup(
    string Key,
    string Label,
    string Kind,
    bool Stackable,
    IReadOnlyList<GenerateCatalogItem> Variants,
    int DefaultIndex)
{
    public string? VariantNoun { get; init; }

    public string? Quality { get; init; }

    public int Plus { get; init; }

    public bool Iconic { get; init; }

    public string? TypeLabel { get; init; }

    public string? Description { get; init; }

    public string? EffectDesc { get; init; }

    public IReadOnlyList<string> StatBadges { get; init; } = [];

    /// <summary>每条目录项单独成卡（不支持合并的适配器用）。</summary>
    public static GenerateCatalogGroup Single(GenerateCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new GenerateCatalogGroup(item.Kind + ":" + item.Target, item.Label, item.Kind, item.Stackable, [item], 0)
        {
            Quality = item.Quality,
            Plus = item.Plus,
            Iconic = item.Iconic,
            TypeLabel = item.TypeLabel,
            Description = item.Description,
            EffectDesc = item.EffectDesc,
            StatBadges = item.StatBadges
        };
    }
}

/// <summary>按卡片分页：Total 是匹配的卡片数，ItemTotal 是这些卡片里的条目总数。</summary>
public sealed record GenerateGroupPage(int Total, int ItemTotal, IReadOnlyList<GenerateCatalogGroup> Groups);

/// <summary>一件新物品实际写入的字节。Seeds 包含根种子和这件物品里所有新换的种子。</summary>
public sealed record GeneratedRecord(byte[] Header, byte[] Body, uint Seed, IReadOnlyList<uint> Seeds);

public sealed record GenerateApplyResult(
    string Target,
    int Copies,
    int AddedBytes,
    int FirstNodeIndex,
    int PreviousPlayerCount,
    string TemplateKey,
    string? TemplateLabel,
    IReadOnlyList<GeneratedRecord> Records,
    bool ToStash = false)
{
    public IReadOnlyList<uint> Seeds => Records.Select(record => record.Seed).ToArray();
}

public sealed record GenerateReport(
    PatchReport Patch,
    IReadOnlyList<uint> Seeds,
    int AddedBytes,
    string TemplateKey,
    string? TemplateLabel);

/// <summary>文档级可选：列出可生成目录，并以存档里的现有物品为模板生成新物品。</summary>
public interface IItemGenerateCapable
{
    int PlayerItemCount { get; }

    int StashItemCount => 0;

    bool GenerateAvailable { get; }

    string? GenerateMessage { get; }

    IReadOnlyList<GenerateKind> GenerateKinds { get; }

    GenerateCatalogPage ListGeneratable(string? kind, string? query, int skip, int take);

    /// <summary>
    /// 同名合并后按卡片分页（T023）：卡片里任一条目命中搜索就列出整张卡，计数和分页都按卡片。
    /// 默认实现把每条目录项当成一张卡。
    /// </summary>
    GenerateGroupPage ListGeneratableGroups(string? kind, string? query, int skip, int take)
    {
        var page = ListGeneratable(kind, query, skip, take);
        return new GenerateGroupPage(page.Total, page.Total, page.Items.Select(GenerateCatalogGroup.Single).ToArray());
    }

    /// <summary>
    /// 每个分类里命中 query 的卡片数（T024，搜索时分类标签上的数字），键是 <see cref="GenerateKinds"/> 里的分类，没有命中的为 0。
    /// 默认实现逐个分类取一页；适配器可以一次遍历目录算出全部计数。
    /// </summary>
    IReadOnlyDictionary<string, int> CountGeneratableGroups(string? query) =>
        GenerateKinds.ToDictionary(kind => kind.Id, kind => ListGeneratableGroups(kind.Id, query, 0, 1).Total, StringComparer.Ordinal);

    /// <summary>按目标精确查找目录中的一条（含持有信息），不在目录里返回 null。</summary>
    GenerateCatalogItem? FindGeneratable(string target);

    /// <summary>
    /// 目标因为游戏缺少资料片而被隐藏时，拒绝生成的说明（D023）；没有被隐藏返回 null。
    /// </summary>
    string? HiddenReason(string target) => null;

    /// <summary>各分类里因为游戏缺少资料片而隐藏的物品（D023），没有隐藏的分类不列出。</summary>
    IReadOnlyList<GenerateHiddenKind> HiddenKinds => [];

    /// <summary>隐藏物品的总说明（如「未检测到往日之影，已隐藏 …」），没有隐藏时为 null。</summary>
    string? HiddenNotice => null;

    /// <summary>
    /// 堆叠目标对应的资源描述，用于走 PatchEngine.Patch 的改数量 / 插入路径。
    /// 与适配器内置资源同一物品时返回内置那条（保证回读校验一致）；不是堆叠目标返回 null。
    /// </summary>
    ResourceDescriptor? StackResource(string target);

    GenerateApplyResult Generate(GenerateRequest request);

    void VerifyOnlyGenerated(ISaveDocument original, GenerateApplyResult applied);
}
