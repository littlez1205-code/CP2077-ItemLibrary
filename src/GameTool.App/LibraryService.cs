using GameTool.Core;

namespace GameTool.App;

/// <summary>
/// 物品库一次「获得」的结果。装备：Quantity 为 1，Before / After 为 null。
/// 堆叠物品：Before 为获得前的数量（未持有为 null），After = Before + Quantity；Inserted 表示插入了新堆叠。
/// </summary>
public sealed record ObtainReport(
    string Target,
    string Label,
    bool Stackable,
    long Quantity,
    long? Before,
    long? After,
    bool Inserted,
    string? TemplateLabel,
    long ElapsedMilliseconds);

/// <summary>
/// 物品库（D013）：确认框里点「获得」的唯一入口。
/// 装备走 <see cref="GenerateService.Run"/>（每次 1 件），堆叠物品走 PatchEngine.Patch 的改数量 / 插入新堆叠路径，
/// 两条路都用原有的严格校验，桌面端按 D006 不生成 .bak。
/// 获得不做任何备份（D019 取消了自动备份）：备份只在用户手动「加入备份区」时进行。
/// </summary>
public sealed class LibraryService
{
    private readonly BackupStore _backups;
    private readonly EnableService _enable;
    private readonly Lock _writeGate = new();

    public LibraryService(BackupStore backups, EnableService enable)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(enable);
        _backups = backups;
        _enable = enable;
    }

    public ObtainReport Obtain(IGameAdapter adapter, string saveFilePath, string target, long? quantity)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (string.IsNullOrWhiteSpace(target))
            throw new PatchRejectedException("缺少要获得的物品。");
        // 页面消息并发处理；写入串行进行，避免两次获得互相把对方判成「存档已被改写」。
        lock (_writeGate)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (adapter.Load(saveFilePath) is not IItemGenerateCapable document)
                throw new PatchRejectedException("这个存档不能获得物品。");
            if (!document.GenerateAvailable)
                throw new PatchRejectedException(document.GenerateMessage ?? "物品库不可用。");
            var item = document.FindGeneratable(target)
                ?? throw new PatchRejectedException(document.HiddenReason(target) ?? $"物品库里没有 {target}。");
            return item.Stackable
                ? ObtainStack(adapter, saveFilePath, document, item, quantity ?? 1, clock)
                : ObtainGear(adapter, saveFilePath, item, quantity ?? 1, clock);
        }
    }

    private ObtainReport ObtainGear(IGameAdapter adapter, string saveFilePath, GenerateCatalogItem item, long quantity, System.Diagnostics.Stopwatch clock)
    {
        var isMod = item.Kind == "mod";
        if (isMod)
        {
            if (quantity is < 1 or > 10)
                throw new PatchRejectedException("模组每次获得 1 到 10 件。");
        }
        else if (quantity != 1)
        {
            throw new PatchRejectedException("装备每次获得 1 件。");
        }

        var copies = (int)quantity;
        var report = GenerateService.Run(adapter, saveFilePath, new GenerateRequest(item.Target, copies), backupFirst: false, _backups, _enable);
        return new ObtainReport(item.Target, item.Label, false, copies, null, null, false, report.TemplateLabel, clock.ElapsedMilliseconds);
    }

    private ObtainReport ObtainStack(
        IGameAdapter adapter,
        string saveFilePath,
        IItemGenerateCapable document,
        GenerateCatalogItem item,
        long quantity,
        System.Diagnostics.Stopwatch clock)
    {
        var resource = document.StackResource(item.Target)
            ?? throw new PatchRejectedException($"{item.Label} 不能按数量获得。");
        if (quantity < 1)
            throw new PatchRejectedException("数量至少为 1。");
        if (quantity > resource.Max)
            throw new PatchRejectedException($"数量不能超过 {resource.Max}。");
        var working = Extend(adapter, resource);
        long? before;
        try
        {
            before = working.Load(saveFilePath).GetQuantity(resource.Id);
        }
        catch (AmbiguousResourceException)
        {
            throw new PatchRejectedException($"{item.Label} 在背包里有多个堆叠，无法确定加到哪一个，未写入。");
        }

        var after = (before ?? 0) + quantity;
        if (after > resource.Max)
            throw new PatchRejectedException($"{item.Label} 获得后为 {after}，超过上限 {resource.Max}（当前 {before ?? 0}）。");
        // 桌面端按 D006 不在存档目录生成 .bak；获得也不自动备份（D019）。
        var report = PatchEngine.Patch(working, saveFilePath, [new ResourceChange(resource.Id, after)], new PatchOptions(null));
        if (report.Outcome != PatchOutcome.Patched)
            throw new PatchRejectedException($"{item.Label} 没有写入（{report.Outcome}）。");
        _enable.AcceptOwnWrite(saveFilePath, report.WrittenLastWriteTimeUtc);
        var inserted = report.Results.Any(result => result.Outcome == ChangeOutcome.Inserted);
        return new ObtainReport(item.Target, item.Label, true, quantity, before, after, inserted, null, clock.ElapsedMilliseconds);
    }

    /// <summary>目标不在适配器内置资源表里时，追加一条临时资源描述，PatchEngine 的回读校验和严格校验照常覆盖它。</summary>
    private static IGameAdapter Extend(IGameAdapter adapter, ResourceDescriptor resource)
    {
        if (adapter.Resources.Any(item => item.Id == resource.Id))
            return adapter;
        if (adapter is not IResourceExtensible extensible)
            throw new PatchRejectedException("这个游戏不支持按数量获得目录里的物品。");
        return extensible.WithResources([resource]);
    }
}
