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

    /// <summary>因为游戏缺少资料片而隐藏的物品：各分类的卡片数和条目数；没有隐藏时为空。</summary>
    public IReadOnlyList<GenerateHiddenKind> Hidden { get; init; } = [];

    public string? HiddenNotice { get; init; }
}

/// <summary>
/// 物品库的卡片页（同名合并）：Total 是卡片数，ItemTotal 是这些卡片里的条目数。
/// Kind 是实际列出的分类（null 为全部分类）；KindCounts 只在有搜索词时给出：每个分类里命中的卡片数。
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

    /// <summary>
    /// 存档里已有储藏箱记录（至少放过一件东西），可以把装备直接放进储藏箱；
    /// 没有时界面不默认、也不允许选「放入储藏室」，否则写入必然失败。
    /// </summary>
    public bool StashAvailable { get; init; }

    /// <summary>因为游戏缺少资料片而隐藏的物品：各分类的卡片数和条目数；没有隐藏时为空。</summary>
    public IReadOnlyList<GenerateHiddenKind> Hidden { get; init; } = [];

    public string? HiddenNotice { get; init; }
}

public static class GenerateService
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    /// <summary>目录查询（只读）复用上一次解析好的存档，见 <see cref="CatalogDocumentCache"/>。</summary>
    private static readonly CatalogDocumentCache Documents = new();

    /// <summary>
    /// 物品库用的卡片目录：同名合并后按卡片搜索和分页。参数校验同 <see cref="Catalog"/>。
    /// kindFallback（界面没有「全部」标签）：kind 为空或不在这个存档的分类里时，改用第一个分类，不报错。
    /// ownership 支持 "all"、"unowned"、"owned"。
    /// </summary>
    public static LibraryCatalogView Groups(
        IGameAdapter adapter,
        string saveFilePath,
        string? kind,
        string? query,
        int skip,
        int take,
        bool kindFallback = false,
        string? ownership = null)
    {
        if (skip < 0)
            throw new PatchRejectedException(UiText.T("skip 不能是负数。", "skip 不能是負數。", "skip cannot be negative."));
        if (take is < 1 or > MaxTake)
            throw new PatchRejectedException(UiText.T($"take 必须在 1 到 {MaxTake} 之间。", $"take 必須在 1 到 {MaxTake} 之間。", $"take must be between 1 and {MaxTake}."));
        return Documents.Query(adapter, saveFilePath, document =>
        {
            var listed = kind;
            var kinds = document.GenerateKinds;
            var known = !string.IsNullOrEmpty(listed) && kinds.Any(item => item.Id == listed);
            if (!string.IsNullOrEmpty(listed) && !known && !kindFallback)
                throw new PatchRejectedException(UiText.T($"未知分类 {listed}。", $"未知分類 {listed}。", $"Unknown category {listed}."));
            if (kindFallback && !known)
                listed = kinds.Count > 0 ? kinds[0].Id : null;
            var page = document.ListGeneratableGroups(string.IsNullOrEmpty(listed) ? null : listed, query, skip, take, ownership);
            var showCounts = !string.IsNullOrWhiteSpace(query) || (!string.IsNullOrEmpty(ownership) && !ownership.Equals("all", StringComparison.OrdinalIgnoreCase));
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
                Kind = string.IsNullOrEmpty(listed) ? null : listed,
                KindCounts = showCounts ? document.CountGeneratableGroups(query, ownership) : null,
                StashAvailable = document.StashAvailable,
                Hidden = document.HiddenKinds,
                HiddenNotice = document.HiddenNotice
            };
        });
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
            throw new PatchRejectedException(UiText.T("skip 不能是负数。", "skip 不能是負數。", "skip cannot be negative."));
        if (take is < 1 or > MaxTake)
            throw new PatchRejectedException(UiText.T($"take 必须在 1 到 {MaxTake} 之间。", $"take 必須在 1 到 {MaxTake} 之間。", $"take must be between 1 and {MaxTake}."));
        return Documents.Query(adapter, saveFilePath, document =>
        {
            if (!string.IsNullOrEmpty(kind) && document.GenerateKinds.All(item => item.Id != kind))
                throw new PatchRejectedException(UiText.T($"未知分类 {kind}。", $"未知分類 {kind}。", $"Unknown category {kind}."));
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
        });
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
        if (backupFirst)
        {
            // 先确认能生成再备份，避免为一次注定失败的生成留下备份。不备份时直接交给 PatchEngine.Generate，
            // 它自己会检查能否生成，这里不必为了检查再完整读一遍存档。
            Require(adapter, saveFilePath);
            backups.Add(adapter, Path.GetDirectoryName(saveFilePath)!);
        }

        // 桌面端不在存档目录生成 .bak；备份走备份区。
        var report = PatchEngine.Generate(adapter, saveFilePath, request, new PatchOptions(null));
        enable.AcceptOwnWrite(saveFilePath, report.Patch.WrittenLastWriteTimeUtc, report.Patch.WrittenLength);
        return report;
    }

    private static IItemGenerateCapable Require(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (adapter.Load(saveFilePath) is not IItemGenerateCapable document)
            throw new PatchRejectedException(UiText.T("这个存档不能生成物品。", "這個存檔不能生成物品。", "This save does not support generating items."));
        return document;
    }
}
