using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public enum PartsMode
{
    Keep,
    Auto
}

public sealed record GeneratedPart(int Depth, string TweakDbIdHex, string SlotHex, uint Seed, bool FreshSeed, string? Record, string? SlotName);

public sealed record PartScan(
    string Key,
    string TweakDbIdHex,
    int Length,
    bool RoundTrip,
    bool RootMatchesHeader,
    string? Error,
    IReadOnlyList<GeneratedPart> Parts);

public sealed record ItemGeneration(
    int NodeIndex,
    int AddedBytes,
    int PreviousPlayerCount,
    string TemplateKey,
    string Target,
    string Parts,
    bool ZeroPrefixIds,
    int TemplateLength,
    int BodyLength,
    string HeaderHex,
    uint Seed,
    IReadOnlyList<GeneratedPart> PartList);

/// <summary>
/// C2b 研究：以现有非堆叠物品为模板，换根 TweakDBID 和种子生成新物品，追加到玩家背包末尾。
/// keep 照抄配件树；auto 按目标的 slotPartListPreset 重建（机匣为根，其余为子配件）。
/// </summary>
public static class ItemGenerator
{
    private const string ReceiverSlot = "AttachmentSlots.Receiver";
    private const string GenericItemRoot = "Items.GenericItemRoot";
    private const int ClothingLength = 91;
    private const string NoneName = "None";

    /// <summary>存档里 GenericItemRoot 配件的种子恒为 2；手雷物品头的种子也是 2。</summary>
    public const uint GenericRootSeed = 2;

    /// <summary>生成的手雷数量。</summary>
    public const uint GrenadeQuantity = 1;
    private const int SeedOffset = 8;
    private const int PrefixIdsStart = 25;
    private const int PrefixIdsEnd = 37;

    public static ItemGeneration GenerateFromTemplate(
        SaveContainer container,
        string templateKey,
        string targetRecord,
        PartsMode mode,
        IReadOnlyList<TweakPresetPart>? presetParts,
        bool zeroPrefixIds = false)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRecord);
        var layout = ItemInserter.Layout.Read(container);
        if (layout.PlayerItems.Count == 0)
            throw new InvalidDataException("玩家背包是空的。");
        var template = ItemInserter.Find(layout, templateKey);
        if (template.Length == ItemInserter.StackLength)
            throw new InvalidDataException("堆叠物品不能当装备模板。");
        if (template.Child >= 0)
            throw new InvalidDataException("模板带子节点，暂不支持。");
        var target = TweakDbId.FromName(targetRecord);
        if (target == template.Tweak)
            throw new InvalidDataException("目标和模板是同一个记录，请改用 clone。");
        var parsed = ItemPartTree.Parse(template.Body);
        if (!parsed.RootId.AsSpan().SequenceEqual(template.Header))
            throw new InvalidDataException("模板 itemData 的根 ItemID 与物品头不一致。");

        var taken = new HashSet<uint>();
        var built = Build(
            template,
            parsed,
            target,
            mode == PartsMode.Keep ? (root, _, _) => root : (root, _, fresh) => BuildParts(root, presetParts, fresh),
            zeroPrefixIds,
            () => FreshSeed(container, taken),
            quantityItem: false);
        var header = built.Header;
        var body = built.Body;
        var seed = built.Seed;
        var check = built.Parsed;

        var before = Cp2077Adapter.ToImage(container);
        var insertion = ItemInserter.Apply(container, layout, template, header, body, targetRecord, quantity: null, seed);
        var after = Cp2077Adapter.ToImage(container);
        var diff = ResearchDiff.Compare(before, after);
        var added = Cp2077SaveDocument.AddedItems(diff, 1);
        var region = after.Regions.First(item => item.Index == added[0].RightIndex && item.Name == added[0].Name);
        if (region.Offset < ItemInserter.HeaderLength
            || !after.Payload.AsSpan(region.Offset, region.Length).SequenceEqual(body)
            || !after.Payload.AsSpan(region.Offset - ItemInserter.HeaderLength, ItemInserter.HeaderLength).SequenceEqual(header))
            throw new InvalidDataException("严格校验失败：写入的物品头或 itemData 与生成结果不一致。");
        Cp2077SaveDocument.VerifyInsertedStructure(before, after, diff, added, insertion.AddedBytes, []);

        var fresh = mode == PartsMode.Auto;
        var parts = check.Walk()
            .Select(entry => new GeneratedPart(entry.Depth, Hex(entry.Part.Tweak), Hex(entry.Part.Slot), entry.Part.Seed, fresh, null, null))
            .ToArray();
        return new ItemGeneration(
            insertion.NodeIndex,
            insertion.AddedBytes,
            insertion.PreviousPlayerCount,
            templateKey,
            targetRecord,
            mode == PartsMode.Keep ? "keep" : "auto",
            zeroPrefixIds,
            template.Length,
            body.Length,
            insertion.HeaderHex,
            seed,
            parts);
    }

    public static IReadOnlyList<PartScan> ScanParts(SaveContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var layout = ItemInserter.Layout.Read(container);
        return Scan(layout.PlayerItems, InventoryResearch.DefaultBagId)
            .Concat(Scan(layout.StashItems, ItemInserter.StashBagId))
            .ToArray();
    }

    /// <summary>
    /// C2b 正式功能：在可生成目录里按目标记录生成 copies 件，自动从背包或储物箱选模板，追加到玩家背包末尾。
    /// 服装：keep 模式，只换根 ItemID（E3a）；远程武器：auto 模式按预设重建配件树，并清零 +25..+37（E3d）。
    /// 近战武器、义体（T021）：不要求同类模板，前缀借任意可解析装备并清零 +25..+37，配件树按预设生成（见 BuildMelee / BuildCyberware）。
    /// 第三批（T022，同样借前缀）：手雷（物品头种子 2、结构 3，45 字节前缀、数量 1，弹芯为根 + 投掷方式子配件）；
    /// 手臂义体（CyberwareRoot 根 + 预设里其余配件）；快速破解与模组（物品自己就是根配件，挂点取 placementSlots 第一项）。
    /// 每件的根种子和新配件的种子都是新的：不是 0/1/2，不在存档里出现，彼此不重复
    /// （义体的 GenericItemRoot 种子固定为 2、手雷的根种子固定为 2，都不算新种子）。
    /// </summary>
    public static GenerateApplyResult Generate(SaveContainer container, string targetRecord, int copies, IGearCatalog gear)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRecord);
        ArgumentNullException.ThrowIfNull(gear);
        if (copies is < 1 or > 10)
            throw new InvalidDataException("生成件数必须在 1 到 10 之间。");
        if (!gear.Available)
            throw new InvalidDataException(gear.UnavailableMessage ?? "名字表不可用，无法生成。");
        if (gear.IsHidden(targetRecord))
            throw new InvalidDataException(GearCatalog.ExpansionHiddenMessage);
        var entry = gear.Gear.FirstOrDefault(item => item.RecordName == targetRecord)
            ?? throw new InvalidDataException($"{targetRecord} 不在可生成目录里。");
        var kind = entry.Kind;
        var ranged = kind == GearKinds.Ranged;
        var loose = GearKinds.UsesPrefixSource(kind);
        var grenade = kind == GearKinds.Grenade;
        var preset = ranged || loose ? gear.PresetParts(targetRecord) : null;
        RootBuilder rootBuilder = kind switch
        {
            GearKinds.Ranged => (root, _, fresh) => BuildParts(root, preset, fresh),
            GearKinds.Melee => (root, _, fresh) => BuildMelee(root, preset, fresh),
            GearKinds.Cyberware => (root, _, fresh) => BuildCyberware(root, preset, fresh),
            GearKinds.Grenade => (root, _, fresh) => BuildPresetTree(root, preset, GearCatalog.GrenadeCoreSlot, fresh),
            GearKinds.ArmCyberware => (root, _, fresh) => BuildPresetTree(root, preset, GearCatalog.CyberwareRootSlot, fresh),
            GearKinds.Quickhack or GearKinds.Mod => (_, header, _) => BuildSelfRoot(header, entry.RootSlot),
            _ => (root, _, _) => root
        };
        var layout = ItemInserter.Layout.Read(container);
        if (layout.PlayerItems.Count == 0)
            throw new InvalidDataException("玩家背包是空的。");
        if (grenade)
        {
            // 手雷的 ItemID 固定为「记录 + 种子 2 + 结构 3」，玩家背包里同一种手雷只有一份（按数量叠加），不能重复插入同一 ID。
            // 世界里的容器（ContainerManager）也会用同一个 ID 记同种手雷，那不算重复。
            if (copies != 1)
                throw new InvalidDataException("手雷每次只能生成 1 件（同一种手雷的 ID 固定，按数量叠加）。");
            if (HoldsQuantityItem(layout, entry.TweakDbId))
                throw new InvalidDataException("背包里已经有这种手雷，不能再插入一份（同一种手雷按数量叠加）。");
        }

        var choice = (loose ? ChoosePrefixSource(layout, kind, gear.KindOf) : ChooseTemplate(layout, kind, TemplateFilter(kind, gear)))
            ?? throw new InvalidDataException(NoTemplateMessage(kind));

        var taken = new HashSet<uint>();
        var records = new List<GeneratedRecord>(copies);
        var previous = layout.PlayerItems.Count;
        var firstNode = -1;
        var added = 0;
        for (var i = 0; i < copies; i++)
        {
            var current = i == 0 ? layout : ItemInserter.Layout.Read(container);
            var seeds = new List<uint>();
            var built = Build(
                choice.Item,
                choice.Parsed,
                entry.TweakDbId,
                rootBuilder,
                zeroPrefixIds: ranged || loose,
                () =>
                {
                    var value = FreshSeed(container, taken);
                    seeds.Add(value);
                    return value;
                },
                quantityItem: grenade);
            var insertion = ItemInserter.Apply(container, current, choice.Item, built.Header, built.Body, targetRecord, quantity: null, built.Seed);
            if (firstNode < 0)
                firstNode = insertion.NodeIndex;
            added += insertion.AddedBytes;
            // Apply 已把节点号写进 body，这就是实际写入的字节。
            records.Add(new GeneratedRecord(built.Header, built.Body, built.Seed, seeds));
        }

        return new GenerateApplyResult(targetRecord, copies, added, firstNode, previous, choice.Key, null, records);
    }

    public static string NoTemplateMessage(string kind) => kind switch
    {
        GearKinds.Ranged => "背包或储物箱里至少要有一件远程武器作为模板。",
        _ when GearKinds.UsesPrefixSource(kind) => "背包或储物箱里至少要有一件能解析的装备（提供物品前缀）。",
        _ => "背包或储物箱里至少要有一件服装作为模板。"
    };

    /// <summary>按生成规则选模板（近战武器、义体及第三批各类是前缀来源），返回物品键；没有可用模板返回 null。</summary>
    public static string? ChooseTemplateKey(SaveContainer container, string kind, IGearCatalog gear)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(gear);
        var layout = ItemInserter.Layout.Read(container);
        return GearKinds.UsesPrefixSource(kind)
            ? ChoosePrefixSource(layout, kind, gear.KindOf)?.Key
            : ChooseTemplate(layout, kind, TemplateFilter(kind, gear))?.Key;
    }

    /// <summary>玩家背包里是否已有这一记录的手雷（物品头与 <see cref="QuantityHeader"/> 相同）。</summary>
    public static bool HoldsQuantityItem(SaveContainer container, ulong tweakDbId)
    {
        ArgumentNullException.ThrowIfNull(container);
        return HoldsQuantityItem(ItemInserter.Layout.Read(container), tweakDbId);
    }

    private static bool HoldsQuantityItem(ItemInserter.Layout layout, ulong tweakDbId)
    {
        var header = QuantityHeader(tweakDbId);
        return layout.PlayerItems.Any(item => item.Header.AsSpan().SequenceEqual(header));
    }

    /// <summary>手雷的物品头：记录 + 种子 2 + <c>03 00 00 00</c>（ManualSave-8 的 8 件手雷全是这样）。</summary>
    public static byte[] QuantityHeader(ulong tweakDbId)
    {
        var header = new byte[ItemInserter.HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(header, tweakDbId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(SeedOffset), GenericRootSeed);
        header[ItemPartTree.StructureOffset] = ItemPartTree.QuantityStructure;
        return header;
    }

    /// <summary>
    /// 服装模板：继承链是服装即可（E3a 的模板就是任务服装）。
    /// 远程武器模板：记录必须在可生成目录的远程武器里且不是标志性武器，避开 Preset_Masamune_Rogue_Stash 这类非标准结构的特殊武器。
    /// </summary>
    private static Func<ulong, bool> TemplateFilter(string kind, IGearCatalog gear)
    {
        if (kind != GearKinds.Ranged)
            return tweak => gear.KindOf(tweak) == kind;
        // T020 起标志性武器也进了目录，但仍不拿它们当模板（同 T019 以前的行为）。
        var ranged = gear.Gear.Where(entry => entry.Kind == GearKinds.Ranged && !entry.Iconic).Select(entry => entry.TweakDbId).ToHashSet();
        return ranged.Contains;
    }

    internal sealed record TemplateChoice(string Key, ItemInserter.PlacedItem Item, ItemDataBody Parsed);

    /// <summary>
    /// 候选：玩家背包在前、储物箱在后，按序号；非堆叠、无子节点、分类相符、配件树能原样解析且根 ItemID 与物品头一致。
    /// 服装优先 91 字节且根配件是 Items.GenericItemRoot 的，其次任意一件；远程武器要求根配件至少有 1 个子配件。
    /// </summary>
    internal static TemplateChoice? ChooseTemplate(ItemInserter.Layout layout, string kind, Func<ulong, bool> eligible)
    {
        var candidates = Candidates(layout, eligible);
        if (kind == GearKinds.Ranged)
            return candidates.FirstOrDefault(choice => choice.Parsed.Root.Children.Count >= 1);
        if (kind != GearKinds.Clothing)
            return null;
        var generic = TweakDbId.FromName(GenericItemRoot);
        return candidates.FirstOrDefault(choice => choice.Item.Length == ClothingLength && choice.Parsed.Root.Tweak == generic)
            ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// 近战武器、义体（T021，E4 形态）及第三批各类不要求同类模板，只借一件装备的 41 字节前缀（+25..+37 随后清零）、配件的 CName 和尾部。
    /// 候选同 <see cref="ChooseTemplate"/>，另要求根配件和第一个子配件（近战、义体照抄它的 CName）的 CName 都是 None
    /// （手臂义体的子配件带外观名，如 PhysicalKnuckles&amp;Male，不能借给别的物品）；同类在前，其次是目录里认得的装备，最后是其他非堆叠物品。
    /// </summary>
    internal static TemplateChoice? ChoosePrefixSource(ItemInserter.Layout layout, string kind, Func<ulong, string?> kindOf) =>
        Candidates(layout, _ => true)
            .Where(choice => choice.Parsed.Root.Appearance == NoneName
                && (choice.Parsed.Root.Children.FirstOrDefault()?.Appearance ?? NoneName) == NoneName)
            .Select((choice, order) => (Choice: choice, Order: order, Kind: kindOf(choice.Item.Tweak)))
            .OrderBy(candidate => candidate.Kind == kind ? 0 : candidate.Kind is not null ? 1 : 2)
            .ThenBy(candidate => candidate.Order)
            .Select(candidate => candidate.Choice)
            .FirstOrDefault();

    /// <summary>
    /// 候选：玩家背包在前、储物箱在后，按序号；非堆叠、无子节点、物品头结构字节为 0（手雷的结构 3 / 45 字节前缀、个别标志性武器的结构 1 都不当模板，免得把结构字节抄给新物品）、
    /// 符合 eligible、配件树能原样解析且根 ItemID 与物品头一致。
    /// </summary>
    private static TemplateChoice[] Candidates(ItemInserter.Layout layout, Func<ulong, bool> eligible) =>
        layout.PlayerItems.Select(item => (Item: item, Bag: InventoryResearch.DefaultBagId))
            .Concat(layout.StashItems.Select(item => (Item: item, Bag: ItemInserter.StashBagId)))
            .Where(candidate => candidate.Item.Length != ItemInserter.StackLength
                && candidate.Item.Child < 0
                && candidate.Item.Header[ItemPartTree.StructureOffset] == 0
                && eligible(candidate.Item.Tweak))
            .Select(candidate =>
            {
                var parsed = TryParseExact(candidate.Item);
                return parsed is null
                    ? null
                    : new TemplateChoice(
                        $"{candidate.Bag}:{candidate.Item.Ordinal}:{Convert.ToHexString(candidate.Item.Header).ToLowerInvariant()}",
                        candidate.Item,
                        parsed);
            })
            .Where(choice => choice is not null)
            .Cast<TemplateChoice>()
            .ToArray();

    private static ItemDataBody? TryParseExact(ItemInserter.PlacedItem item)
    {
        try
        {
            var parsed = ItemPartTree.Parse(item.Body);
            if (!ItemPartTree.Write(parsed).AsSpan().SequenceEqual(item.Body) || !parsed.RootId.AsSpan().SequenceEqual(item.Header))
                return null;
            return parsed;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private sealed record BuiltItem(byte[] Header, byte[] Body, uint Seed, ItemDataBody Parsed);

    /// <summary>按来源的根配件、新物品头和取新种子的方法拼出新配件树。</summary>
    private delegate ItemPart RootBuilder(ItemPart sourceRoot, byte[] header, Func<uint> fresh);

    /// <summary>
    /// 研究命令和正式功能共用：按模板和目标拼出新物品头与 itemData，并自检能按配件树语法原样解析。
    /// quantityItem（手雷）：物品头种子固定为 2、+12..+16 为 <c>03 00 00 00</c>；前缀在来源的 41 字节基础上于 +25 插入 u32 数量 1，
    /// 清零范围随之后移到 +29..+41。
    /// </summary>
    private static BuiltItem Build(
        ItemInserter.PlacedItem template,
        ItemDataBody parsed,
        ulong target,
        RootBuilder rootBuilder,
        bool zeroPrefixIds,
        Func<uint> fresh,
        bool quantityItem)
    {
        var header = template.Header.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(header, target);
        uint seed;
        if (quantityItem)
        {
            seed = GenericRootSeed;
            header.AsSpan(ItemPartTree.StructureOffset).Clear();
            header[ItemPartTree.StructureOffset] = ItemPartTree.QuantityStructure;
        }
        else
        {
            seed = fresh();
            if (ItemPartTree.IsQuantityStructure(header))
                header.AsSpan(ItemPartTree.StructureOffset).Clear();
        }

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(SeedOffset), seed);
        // 来源若是 45 字节前缀，先去掉数量回到 41 字节的通用形态。
        var basePrefix = parsed.Prefix.Length == ItemPartTree.QuantityPrefixLength
            ? [.. parsed.Prefix[..ItemPartTree.QuantityOffset], .. parsed.Prefix[(ItemPartTree.QuantityOffset + 4)..]]
            : parsed.Prefix.ToArray();
        var prefix = basePrefix;
        var shift = 0;
        if (quantityItem)
        {
            var quantity = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(quantity, GrenadeQuantity);
            prefix = [.. basePrefix[..ItemPartTree.QuantityOffset], .. quantity, .. basePrefix[ItemPartTree.QuantityOffset..]];
            shift = 4;
        }

        header.CopyTo(prefix, 4);
        if (zeroPrefixIds)
            prefix.AsSpan(PrefixIdsStart + shift, PrefixIdsEnd - PrefixIdsStart).Clear();
        var root = rootBuilder(parsed.Root, header, fresh);

        var body = ItemPartTree.Write(new ItemDataBody(prefix, root));
        var check = ItemPartTree.Parse(body);
        if (!ItemPartTree.Write(check).AsSpan().SequenceEqual(body) || !check.RootId.AsSpan().SequenceEqual(header))
            throw new InvalidDataException("自检失败：新 itemData 无法按配件树语法原样解析。");
        if (quantityItem != (check.Quantity is not null))
            throw new InvalidDataException("自检失败：新 itemData 的前缀形态与结构字节不符。");
        return new BuiltItem(header, body, seed, check);
    }

    private static uint FreshSeed(SaveContainer container, HashSet<uint> taken)
    {
        var value = ItemInserter.NewSeed(container.Payload, taken);
        taken.Add(value);
        return value;
    }

    private static IEnumerable<PartScan> Scan(List<ItemInserter.PlacedItem> items, ulong bag) =>
        items.Where(item => item.Length != ItemInserter.StackLength).Select(item =>
        {
            var key = $"{bag}:{item.Ordinal}:{Convert.ToHexString(item.Header).ToLowerInvariant()}";
            try
            {
                var parsed = ItemPartTree.Parse(item.Body);
                var roundTrip = ItemPartTree.Write(parsed).AsSpan().SequenceEqual(item.Body);
                var parts = parsed.Walk()
                    .Select(entry => new GeneratedPart(entry.Depth, Hex(entry.Part.Tweak), Hex(entry.Part.Slot), entry.Part.Seed, false, null, null))
                    .ToArray();
                return new PartScan(key, Hex(item.Tweak), item.Length, roundTrip, parsed.RootId.AsSpan().SequenceEqual(item.Header), null, parts);
            }
            catch (InvalidDataException error)
            {
                return new PartScan(key, Hex(item.Tweak), item.Length, false, false, error.Message, []);
            }
        });

    private static ItemPart BuildParts(ItemPart templateRoot, IReadOnlyList<TweakPresetPart>? preset, Func<uint> fresh)
    {
        if (preset is null || preset.Count == 0)
            throw new InvalidDataException("目标没有带 itemPartPreset 的 slotPartListPreset 条目。");
        var receiver = preset.FirstOrDefault(part => part.Slot == ReceiverSlot)
            ?? throw new InvalidDataException("目标的 slotPartListPreset 没有 AttachmentSlots.Receiver。");
        var model = templateRoot.Children.FirstOrDefault()
            ?? throw new InvalidDataException("模板的配件树没有子配件，无法照抄子配件的尾部。");
        var rootId = PartId(receiver.Item, fresh(), templateRoot.ItemId);
        var children = preset
            .Where(part => !ReferenceEquals(part, receiver))
            .Select(part => new ItemPart(
                PartId(part.Item, fresh(), model.ItemId),
                model.Appearance,
                TweakDbId.FromName(part.Slot),
                [],
                model.Trailer.ToArray()))
            .ToArray();
        return new ItemPart(rootId, templateRoot.Appearance, TweakDbId.FromName(receiver.Slot), children, templateRoot.Trailer.ToArray());
    }

    /// <summary>
    /// 近战武器：握柄为根（新种子），预设里其余有配件的条目为子配件（新种子），如标志性近战武器的 IconicMeleeWeaponMod1；
    /// 普通近战武器的模组槽没有配件，于是没有子节点（91 字节，同存档里的徒手）。ItemID 后 4 字节、CName、尾部照抄来源的配件。
    /// </summary>
    private static ItemPart BuildMelee(ItemPart sourceRoot, IReadOnlyList<TweakPresetPart>? preset, Func<uint> fresh)
    {
        var grip = preset?.FirstOrDefault(part => part.Slot == GearCatalog.GripSlot && part.Item.Length > 0)
            ?? throw new InvalidDataException("目标的 slotPartListPreset 没有 AttachmentSlots.Grip。");
        var model = sourceRoot.Children.FirstOrDefault() ?? sourceRoot;
        var rootId = PartId(grip.Item, fresh(), sourceRoot.ItemId);
        var children = preset
            .Where(part => !ReferenceEquals(part, grip) && part.Item.Length > 0)
            .Select(part => new ItemPart(PartId(part.Item, fresh(), model.ItemId), model.Appearance, TweakDbId.FromName(part.Slot), [], model.Trailer.ToArray()))
            .ToArray();
        return new ItemPart(rootId, sourceRoot.Appearance, TweakDbId.FromName(grip.Slot), children, sourceRoot.Trailer.ToArray());
    }

    /// <summary>
    /// 义体：根是 Items.GenericItemRoot，种子固定为 2、ItemID 后 4 字节为 0（存档实测）；属性碎片为子配件（新种子）。
    /// 没有属性碎片的义体只有根（91 字节，同存档里的 AdvancedNetwatchNetdriverMKLegendaryPlus）。CName 和尾部照抄来源的配件。
    /// </summary>
    private static ItemPart BuildCyberware(ItemPart sourceRoot, IReadOnlyList<TweakPresetPart>? preset, Func<uint> fresh)
    {
        // 义体操作系统（T022）没有配件预设，存档里是只有 GenericItemRoot 根的 91 字节。
        if (preset is { Count: 0 })
            preset = [new TweakPresetPart(GearCatalog.GenericItemRootSlot, GearCatalog.GenericItemRoot)];
        if (!GearCatalog.IsCyberwareShape(preset))
            throw new InvalidDataException("目标的 slotPartListPreset 不是 GenericItemRoot + 属性碎片的形态。");
        var model = sourceRoot.Children.FirstOrDefault() ?? sourceRoot;
        var rootId = new byte[ItemPartTree.ItemIdLength];
        BinaryPrimitives.WriteUInt64LittleEndian(rootId, TweakDbId.FromName(GearCatalog.GenericItemRoot));
        BinaryPrimitives.WriteUInt32LittleEndian(rootId.AsSpan(SeedOffset), GenericRootSeed);
        var children = preset!
            .Skip(1)
            .Select(part => new ItemPart(PartId(part.Item, fresh(), model.ItemId), model.Appearance, TweakDbId.FromName(part.Slot), [], model.Trailer.ToArray()))
            .ToArray();
        return new ItemPart(rootId, sourceRoot.Appearance, TweakDbId.FromName(GearCatalog.GenericItemRootSlot), children, sourceRoot.Trailer.ToArray());
    }

    /// <summary>
    /// 手雷、手臂义体（T022）：预设第一条（GrenadeCore 的弹芯 / CyberwareRoot 的根）为根，其余有配件的条目按预设顺序为子配件，全部新种子。
    /// 存档实测：CuttingGrenadeLegendaryPlus = w_cutting_core + w_homing_delivery（151 字节），其他手雷只有弹芯（95 字节）；
    /// AdvancedNanoWiresChemicalLegendaryPlusPlus = NanoWiresRoot + ChemicalDamageCable + 属性碎片（200 字节）。
    /// ItemID 后 4 字节照抄来源的配件；CName 一律写 None、尾部写 16 个 0 + FLT_MAX：存档里这些配件的外观名（如 default、ChemicalCable）
    /// 是游戏装上后填的，按 E3 的经验写 None 由游戏自己解析，也不借来源配件可能带的外观名或尾部数值。
    /// </summary>
    private static ItemPart BuildPresetTree(ItemPart sourceRoot, IReadOnlyList<TweakPresetPart>? preset, string rootSlot, Func<uint> fresh)
    {
        if (preset is not { Count: > 0 } || preset[0].Slot != rootSlot || preset[0].Item.Length == 0)
            throw new InvalidDataException($"目标的 slotPartListPreset 第一条不是 {rootSlot} 上的配件。");
        var model = sourceRoot.Children.FirstOrDefault() ?? sourceRoot;
        var rootId = PartId(preset[0].Item, fresh(), sourceRoot.ItemId);
        var children = preset
            .Skip(1)
            .Where(part => part.Item.Length > 0)
            .Select(part => new ItemPart(PartId(part.Item, fresh(), model.ItemId), NoneName, TweakDbId.FromName(part.Slot), [], BlankTrailer()))
            .ToArray();
        return new ItemPart(rootId, NoneName, TweakDbId.FromName(preset[0].Slot), children, BlankTrailer());
    }

    /// <summary>
    /// 快速破解、模组（T022）：物品自己就是唯一的配件，ItemID 与物品头完全相同（存档实测 BlindLvl4PlusPlusProgram、ShotgunMod1_Legendary、
    /// w_att_scope_short_07_legendary 都是 91 字节），挂点取 placementSlots 第一项，没有子配件；CName 为 None，尾部 16 个 0 + FLT_MAX。
    /// </summary>
    private static ItemPart BuildSelfRoot(byte[] header, string? slot)
    {
        if (string.IsNullOrEmpty(slot))
            throw new InvalidDataException("目标没有 placementSlots，不知道根配件的挂点。");
        return new ItemPart(header.ToArray(), NoneName, TweakDbId.FromName(slot), [], BlankTrailer());
    }

    /// <summary>存档里最常见的配件尾部：16 个 0 + f32 FLT_MAX。</summary>
    public static byte[] BlankTrailer()
    {
        var trailer = new byte[ItemPartTree.TrailerLength];
        BinaryPrimitives.WriteSingleLittleEndian(trailer.AsSpan(ItemPartTree.TrailerLength - 4), float.MaxValue);
        return trailer;
    }

    private static byte[] PartId(string record, uint seed, byte[] template)
    {
        var id = template.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(id, TweakDbId.FromName(record));
        BinaryPrimitives.WriteUInt32LittleEndian(id.AsSpan(SeedOffset), seed);
        return id;
    }

    private static string Hex(ulong value) => Convert.ToHexString(BitConverter.GetBytes(value)).ToLowerInvariant();
}
