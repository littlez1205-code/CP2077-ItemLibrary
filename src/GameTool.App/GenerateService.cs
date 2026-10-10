using GameTool.Core;

namespace GameTool.App;

public sealed record GenerateCatalogView(
    bool NamesAvailable,
    string? Message,
    IReadOnlyList<GenerateKind> Kinds,
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<GenerateCatalogItem> Items)
{
    /// <summary>物品图标版本（拼进图标地址做长缓存）；适配器没有图标或游戏资源不可用时为 null，界面只显示占位图。</summary>
    public string? IconVersion { get; init; }

    /// <summary>因为游戏缺少资料片而隐藏的物品（D023）：各分类的卡片数和条目数；没有隐藏时为空。</summary>
    public IReadOnlyList<GenerateHiddenKind> Hidden { get; init; } = [];

    public string? HiddenNotice { get; init; }
}

/// <summary>
/// 物品库的卡片页（T023，同名合并）：Total 是卡片数，ItemTotal 是这些卡片里的条目数。
/// Kind 是实际列出的分类（null 为全部分类）；KindCounts 只在有搜索词时给出：每个分类里命中的卡片数（T024）。
/// </summary>
public sealed record LibraryCatalogView(
    bool NamesAvailable,
    string? Message,
    IReadOnlyList<GenerateKind> Kinds,
    int Total,
    int ItemTotal,
    int Skip,
    int Take,
    IReadOnlyList<GenerateCatalogGroup> Groups)
{
    public string? IconVersion { get; init; }

    public string? Kind { get; init; }

    public IReadOnlyDictionary<string, int>? KindCounts { get; init; }

    /// <summary>因为游戏缺少资料片而隐藏的物品（D023）：各分类的卡片数和条目数；没有隐藏时为空。</summary>
    public IReadOnlyList<GenerateHiddenKind> Hidden { get; init; } = [];

    public string? HiddenNotice { get; init; }
}

public static class GenerateService
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    /// <summary>
    /// 物品库用的卡片目录：同名合并后按卡片搜索和分页（T023）。参数校验同 <see cref="Catalog"/>。
    /// kindFallback（T024，界面没有「全部」标签）：kind 为空或不在这个存档的分类里时，改用第一个分类，不报错。
    /// </summary>
    public static LibraryCatalogView Groups(
        IGameAdapter adapter,
        string saveFilePath,
        string? kind,
        string? query,
        int skip,
        int take,
        bool kindFallback = false)
    {
        if (skip < 0)
            throw new PatchRejectedException("skip 不能是负数。");
        if (take is < 1 or > MaxTake)
            throw new PatchRejectedException($"take 必须在 1 到 {MaxTake} 之间。");
        var document = Require(adapter, saveFilePath);
        var kinds = document.GenerateKinds;
        var known = !string.IsNullOrEmpty(kind) && kinds.Any(item => item.Id == kind);
        if (!string.IsNullOrEmpty(kind) && !known && !kindFallback)
            throw new PatchRejectedException($"未知分类 {kind}。");
        if (kindFallback && !known)
            kind = kinds.Count > 0 ? kinds[0].Id : null;
        var page = document.ListGeneratableGroups(string.IsNullOrEmpty(kind) ? null : kind, query, skip, take);
        return new LibraryCatalogView(
            document.GenerateAvailable,
            document.GenerateMessage,
            kinds,
            page.Total,
            page.ItemTotal,
            skip,
            take,
            page.Groups)
        {
            IconVersion = (adapter as IItemIconProvider)?.IconVersion,
            Kind = string.IsNullOrEmpty(kind) ? null : kind,
            KindCounts = string.IsNullOrWhiteSpace(query) ? null : document.CountGeneratableGroups(query),
            Hidden = document.HiddenKinds,
            HiddenNotice = document.HiddenNotice
        };
    }

    public static GenerateCatalogView Catalog(
        IGameAdapter adapter,
        string saveFilePath,
        string? kind,
        string? query,
        int skip,
        int take)
    {
        if (skip < 0)
            throw new PatchRejectedException("skip 不能是负数。");
        if (take is < 1 or > MaxTake)
            throw new PatchRejectedException($"take 必须在 1 到 {MaxTake} 之间。");
        var document = Require(adapter, saveFilePath);
        if (!string.IsNullOrEmpty(kind) && document.GenerateKinds.All(item => item.Id != kind))
            throw new PatchRejectedException($"未知分类 {kind}。");
        var page = document.ListGeneratable(kind, query, skip, take);
        return new GenerateCatalogView(
            document.GenerateAvailable,
            document.GenerateMessage,
            document.GenerateKinds,
            page.Total,
            skip,
            take,
            page.Items)
        {
            IconVersion = (adapter as IItemIconProvider)?.IconVersion,
            Hidden = document.HiddenKinds,
            HiddenNotice = document.HiddenNotice
        };
    }

    public static GenerateReport Run(
        IGameAdapter adapter,
        string saveFilePath,
        GenerateRequest request,
        bool backupFirst,
        BackupStore backups,
        EnableService enable)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(enable);
        Require(adapter, saveFilePath);
        if (backupFirst)
            backups.Add(adapter, Path.GetDirectoryName(saveFilePath)!);
        // 桌面端按 D006 不在存档目录生成 .bak；备份走备份区。
        var report = PatchEngine.Generate(adapter, saveFilePath, request, new PatchOptions(null));
        enable.AcceptOwnWrite(saveFilePath, report.Patch.WrittenLastWriteTimeUtc);
        return report;
    }

    private static IItemGenerateCapable Require(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (adapter.Load(saveFilePath) is not IItemGenerateCapable document)
            throw new PatchRejectedException("这个存档不能生成物品。");
        return document;
    }
}
