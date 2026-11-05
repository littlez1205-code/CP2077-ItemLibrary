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
    long ElapsedMilliseconds,
    bool ToStash = false);

public sealed record BatchItemRequest(string Target, long? Quantity = null, bool? ToStash = null);

public sealed record BatchItemResult(
    string Target,
    string Label,
    bool Success,
    string? Error,
    ObtainReport? Report);

public sealed record BatchObtainReport(
    int TotalRequested,
    int Succeeded,
    int Failed,
    IReadOnlyList<BatchItemResult> Results,
    long ElapsedMilliseconds);

/// <summary>
/// 物品库：确认框里点「获得」的唯一入口。
/// 装备走 <see cref="GenerateService.Run"/>（每次 1 件），堆叠物品走 PatchEngine.Patch 的改数量 / 插入新堆叠路径，
/// 两条路都用原有的严格校验，桌面端不生成 .bak。
/// 获得不做任何备份（已取消自动备份）：备份只在用户手动「加入备份区」时进行。
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

    public ObtainReport Obtain(IGameAdapter adapter, string saveFilePath, string target, long? quantity, bool toStash = false)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (string.IsNullOrWhiteSpace(target))
            throw new PatchRejectedException(UiText.T("缺少要获得的物品。", "缺少要獲得的物品。", "No item to obtain was given."));
        // 页面消息并发处理；写入串行进行，避免两次获得互相把对方判成「存档已被改写」。
        lock (_writeGate)
        {
            return ObtainInternal(adapter, saveFilePath, target, quantity, toStash);
        }
    }

    /// <summary>单次批量获得的件数上限。</summary>
    public const int MaxBatchItems = 100;

    /// <summary>
    /// 批量获得：逐件写入（每件各自走严格校验与原子替换），一件失败不影响其他件，结果逐件返回。
    /// destinationMode：smart（默认）= 存档里有储藏箱时标志性装备放储藏箱、其余放背包；backpack = 全部放背包；stash = 能放储藏箱的全部放储藏箱。
    /// 单件的 ToStash 优先于 destinationMode；堆叠物品、手雷等不能放储藏箱的物品一律放背包。
    /// </summary>
    public BatchObtainReport BatchObtain(
        IGameAdapter adapter,
        string saveFilePath,
        IReadOnlyList<BatchItemRequest> items,
        string? destinationMode = "smart")
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (items is null || items.Count == 0)
            throw new PatchRejectedException(UiText.T("批量添加列表为空。", "批次新增列表為空。", "The batch list is empty."));
        if (items.Count > MaxBatchItems)
            throw new PatchRejectedException(UiText.T($"单次批量添加最多支持 {MaxBatchItems} 件物品。", $"單次批次新增最多支援 {MaxBatchItems} 件物品。", $"A batch can contain at most {MaxBatchItems} items."));
        var mode = ParseDestination(destinationMode);

        lock (_writeGate)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // 分类、是否堆叠、是否标志性、存档里有没有储藏箱，在这一批写入过程中都不会变：只读一次存档，
            // 用来决定每件放哪里、也用来查每件的目录信息（见 ObtainFrom）；每件真正写入时 PatchEngine 仍会读取最新存档。
            if (adapter.Load(saveFilePath) is not IItemGenerateCapable document)
                throw new PatchRejectedException(UiText.T("这个存档不能获得物品。", "這個存檔不能獲得物品。", "Items cannot be obtained in this save."));
            var stashAvailable = document.StashAvailable;
            var planned = items
                .Select(request => (Request: request, Item: string.IsNullOrWhiteSpace(request.Target) ? null : document.FindGeneratable(request.Target)))
                .ToArray();
            if (mode == BatchDestination.Stash
                && !stashAvailable
                && planned.Any(plan => plan.Request.ToStash is null && plan.Item?.CanStash == true))
            {
                throw new PatchRejectedException(NoStashMessage);
            }

            var results = new List<BatchItemResult>(planned.Length);
            var succeeded = 0;
            var failed = 0;
            foreach (var (request, item) in planned)
            {
                if (string.IsNullOrWhiteSpace(request.Target))
                {
                    results.Add(new BatchItemResult("", UiText.T("未知物品", "未知物品", "Unknown item"), false, UiText.T("缺少目标。", "缺少目標。", "Missing target."), null));
                    failed++;
                    continue;
                }

                try
                {
                    var toStash = ChooseDestination(item, request.ToStash, mode, stashAvailable);
                    var report = ObtainFrom(adapter, saveFilePath, document, request.Target, request.Quantity, toStash, System.Diagnostics.Stopwatch.StartNew());
                    results.Add(new BatchItemResult(request.Target, report.Label, true, null, report));
                    succeeded++;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    results.Add(new BatchItemResult(request.Target, item?.Label ?? request.Target, false, ex.Message, null));
                    failed++;
                }
            }

            return new BatchObtainReport(items.Count, succeeded, failed, results, clock.ElapsedMilliseconds);
        }
    }

    private static string NoStashMessage => UiText.T("存档中暂无储藏室记录，不能全部放入储藏室；请在游戏内先往载具后备箱或公寓储藏处放一件物品并存档，或改为放入背包。", "存檔中暫無儲藏室記錄，不能全部放入儲藏室；請在遊戲內先往載具後備箱或公寓儲藏處放一件物品並存檔，或改為放入背包。", "This save has no stash record yet, so the items cannot all go to the stash. Put any item into a vehicle trunk or apartment stash in game and save, or choose the backpack instead.");

    private enum BatchDestination
    {
        Smart,
        Backpack,
        Stash
    }

    private static BatchDestination ParseDestination(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "smart" => BatchDestination.Smart,
        "backpack" => BatchDestination.Backpack,
        "stash" => BatchDestination.Stash,
        _ => throw new PatchRejectedException(UiText.T($"未知的存放位置 {mode}。", $"未知的存放位置 {mode}。", $"Unknown destination {mode}."))
    };

    /// <summary>
    /// 批量里一件物品放哪里：不能放储藏箱的（堆叠物品、手雷、不在目录里的）一律放背包；
    /// 单件明确指定的优先；smart 只把标志性装备放进储藏箱，而且存档里还没有储藏箱时退回背包，不让整批失败。
    /// </summary>
    private static bool ChooseDestination(GenerateCatalogItem? item, bool? requested, BatchDestination mode, bool stashAvailable)
    {
        if (item is null || !item.CanStash)
            return false;
        if (requested is bool explicitChoice)
            return explicitChoice;
        return mode switch
        {
            BatchDestination.Stash => true,
            BatchDestination.Smart => item.Iconic && stashAvailable,
            _ => false
        };
    }

    private ObtainReport ObtainInternal(IGameAdapter adapter, string saveFilePath, string target, long? quantity, bool toStash)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (adapter.Load(saveFilePath) is not IItemGenerateCapable document)
            throw new PatchRejectedException(UiText.T("这个存档不能获得物品。", "這個存檔不能獲得物品。", "Items cannot be obtained in this save."));
        return ObtainFrom(adapter, saveFilePath, document, target, quantity, toStash, clock);
    }

    /// <summary>
    /// catalog 只用来查目录：物品是否存在、分类、是否堆叠、名字和堆叠资源描述，这些在一次批量里都不会变，
    /// 所以批量可以直接用规划时读的那份文档，每件少读一遍存档。持有数量和写入都在后面重新读取最新存档。
    /// </summary>
    private ObtainReport ObtainFrom(
        IGameAdapter adapter,
        string saveFilePath,
        IItemGenerateCapable document,
        string target,
        long? quantity,
        bool toStash,
        System.Diagnostics.Stopwatch clock)
    {
        if (!document.GenerateAvailable)
            throw new PatchRejectedException(document.GenerateMessage ?? UiText.T("物品库不可用。", "物品庫不可用。", "The item library is unavailable."));
        var item = document.FindGeneratable(target)
            ?? throw new PatchRejectedException(document.HiddenReason(target) ?? UiText.T($"物品库里没有 {target}。", $"物品庫裡沒有 {target}。", $"{target} is not in the item library."));
        if (item.Stackable)
        {
            if (toStash)
                throw new PatchRejectedException(UiText.T("堆叠物品不支持直接存入储藏箱。", "堆疊物品不支援直接存入儲藏箱。", "Stackable items cannot be put directly into the stash."));
            return ObtainStack(adapter, saveFilePath, document, item, quantity ?? 1, clock);
        }
        return ObtainGear(adapter, saveFilePath, item, quantity ?? 1, clock, toStash);
    }

    private ObtainReport ObtainGear(IGameAdapter adapter, string saveFilePath, GenerateCatalogItem item, long quantity, System.Diagnostics.Stopwatch clock, bool toStash = false)
    {
        var isMod = item.Kind == "mod";
        if (isMod)
        {
            if (quantity is < 1 or > 10)
                throw new PatchRejectedException(UiText.T("模组每次获得 1 到 10 件。", "模組每次獲得 1 到 10 件。", "Mods can be obtained 1 to 10 at a time."));
        }
        else if (quantity != 1)
        {
            throw new PatchRejectedException(UiText.T("装备每次获得 1 件。", "裝備每次獲得 1 件。", "Gear is obtained one at a time."));
        }

        var copies = (int)quantity;
        var report = GenerateService.Run(adapter, saveFilePath, new GenerateRequest(item.Target, copies, toStash), backupFirst: false, _backups, _enable);
        return new ObtainReport(item.Target, item.Label, false, copies, null, null, false, report.TemplateLabel, clock.ElapsedMilliseconds, toStash);
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
            ?? throw new PatchRejectedException(UiText.T($"{item.Label} 不能按数量获得。", $"{item.Label} 不能按數量獲得。", $"{item.Label} cannot be obtained by quantity."));
        if (quantity < 1)
            throw new PatchRejectedException(UiText.T("数量至少为 1。", "數量至少為 1。", "The quantity must be at least 1."));
        if (quantity > resource.Max)
            throw new PatchRejectedException(UiText.T($"数量不能超过 {resource.Max}。", $"數量不能超過 {resource.Max}。", $"The quantity cannot exceed {resource.Max}."));
        var working = Extend(adapter, resource);
        long? before;
        try
        {
            before = working.Load(saveFilePath).GetQuantity(resource.Id);
        }
        catch (AmbiguousResourceException)
        {
            throw new PatchRejectedException(UiText.T($"{item.Label} 在背包里有多个堆叠，无法确定加到哪一个，未写入。", $"{item.Label} 在背包裡有多個堆疊，無法確定加到哪一個，未寫入。", $"{item.Label} has several stacks in the backpack, so it is unclear which one to add to. Nothing was written."));
        }

        var after = (before ?? 0) + quantity;
        if (after > resource.Max)
            throw new PatchRejectedException(UiText.T($"{item.Label} 获得后为 {after}，超过上限 {resource.Max}（当前 {before ?? 0}）。", $"{item.Label} 獲得後為 {after}，超過上限 {resource.Max}（當前 {before ?? 0}）。", $"{item.Label} would become {after}, above the limit of {resource.Max} (currently {before ?? 0})."));
        // 桌面端不在存档目录生成 .bak；获得也不自动备份。
        var report = PatchEngine.Patch(working, saveFilePath, [new ResourceChange(resource.Id, after)], new PatchOptions(null));
        if (report.Outcome != PatchOutcome.Patched)
            throw new PatchRejectedException(UiText.T($"{item.Label} 没有写入（{report.Outcome}）。", $"{item.Label} 沒有寫入（{report.Outcome}）。", $"{item.Label} was not written ({report.Outcome})."));
        _enable.AcceptOwnWrite(saveFilePath, report.WrittenLastWriteTimeUtc, report.WrittenLength);
        var inserted = report.Results.Any(result => result.Outcome == ChangeOutcome.Inserted);
        return new ObtainReport(item.Target, item.Label, true, quantity, before, after, inserted, null, clock.ElapsedMilliseconds);
    }

    /// <summary>目标不在适配器内置资源表里时，追加一条临时资源描述，PatchEngine 的回读校验和严格校验照常覆盖它。</summary>
    private static IGameAdapter Extend(IGameAdapter adapter, ResourceDescriptor resource)
    {
        if (adapter.Resources.Any(item => item.Id == resource.Id))
            return adapter;
        if (adapter is not IResourceExtensible extensible)
            throw new PatchRejectedException(UiText.T("这个游戏不支持按数量获得目录里的物品。", "這個遊戲不支援按數量獲得目錄裡的物品。", "This game does not support obtaining catalog items by quantity."));
        return extensible.WithResources([resource]);
    }
}
