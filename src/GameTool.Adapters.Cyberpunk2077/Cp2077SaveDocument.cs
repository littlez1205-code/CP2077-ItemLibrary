using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077;

public sealed record ResourceView(
    string Id,
    string NameZh,
    long? Quantity,
    IReadOnlyList<long> Stacks,
    bool Ambiguous);

/// <summary>
/// 玩家背包可原地改数量。写回走容器的脏块重压缩。
/// </summary>
public sealed class Cp2077SaveDocument : ISaveDocument, IStrictVerifier, ISaveDiagnostics, IStackInspector, IItemCloneCapable, IItemGenerateCapable
{
    private readonly SaveContainer _container;
    private readonly IReadOnlyList<ResourceDescriptor> _resources;
    private readonly List<ItemStack> _stacks;
    private readonly Func<ResourceId, bool> _canInsert;
    private readonly Func<IItemNameProvider>? _namesFactory;
    private IItemNameProvider? _names;
    private readonly Func<IGearCatalog>? _gearFactory;
    private IGearCatalog? _gear;

    private Cp2077SaveDocument(
        SaveContainer container,
        SaveVersionInfo version,
        IReadOnlyList<ResourceDescriptor> resources,
        InventorySnapshot inventory,
        Func<ResourceId, bool> canInsert,
        Func<IItemNameProvider>? names,
        Func<IGearCatalog>? gear)
    {
        _container = container;
        Version = version;
        _resources = resources;
        _stacks = inventory.PlayerStacks.ToList();
        PlayerInventoryItemCount = inventory.PlayerItemCount;
        _canInsert = canInsert;
        _namesFactory = names;
        _gearFactory = gear;
    }

    private IGearCatalog? Gear
    {
        get
        {
            if (_gear is null && _gearFactory is not null)
                _gear = _gearFactory();
            return _gear;
        }
    }

    private IItemNameProvider? Names
    {
        get
        {
            if (_names is null && _namesFactory is not null)
                _names = _namesFactory();
            return _names;
        }
    }

    public SaveVersionInfo Version { get; }
    public int PlayerInventoryItemCount { get; private set; }
    public int PlayerItemCount => PlayerInventoryItemCount;

    public bool NamesAvailable => Names?.Available == true;

    public string? NamesMessage => NamesAvailable ? null : Names?.UnavailableMessage ?? ItemNameCatalog.RedmodUnavailable;

    public IReadOnlyList<DiagnosticField> Fields =>
    [
        new("chunkCount", _container.ChunkCount),
        new("decompressedLength", _container.DecompressedLength),
        new("nodeTableOffset", _container.NodeTableOffset),
        new("playerInventoryItemCount", PlayerInventoryItemCount)
    ];

    public static Cp2077SaveDocument Open(
        SaveContainer container,
        IReadOnlyList<ResourceDescriptor> resources,
        Func<ResourceId, bool>? canInsert = null,
        Func<IItemNameProvider>? names = null,
        Func<IGearCatalog>? gear = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(resources);
        var nodes = NodeTable.Parse(container.CopyNodeTable());
        var inventory = InventoryReader.Read(container.Payload, nodes);
        return new Cp2077SaveDocument(
            container,
            new SaveVersionInfo(container.SaveVersion, container.GameVersion),
            resources,
            inventory,
            canInsert ?? (_ => true),
            names,
            gear);
    }

    public IReadOnlyList<long> GetStacks(ResourceId id)
    {
        var resource = RequireResource(id);
        var tweakId = TweakDbId.FromLocator(resource.Locator);
        return _stacks.Where(stack => stack.TweakDbId == tweakId).Select(stack => stack.Quantity).ToArray();
    }

    public ResourceView Describe(ResourceDescriptor resource)
    {
        var stacks = GetStacks(resource.Id);
        return new ResourceView(
            resource.Id.Value,
            resource.NameZh,
            stacks.Count == 1 ? stacks[0] : null,
            stacks,
            stacks.Count > 1);
    }

    public long? GetQuantity(ResourceId id)
    {
        var resource = RequireResource(id);
        var tweakId = TweakDbId.FromLocator(resource.Locator);
        var stacks = _stacks.Where(stack => stack.TweakDbId == tweakId).ToArray();
        if (stacks.Length == 0)
            return null;
        if (stacks.Length > 1)
            throw new AmbiguousResourceException(id);
        return stacks[0].Quantity;
    }

    public IReadOnlyList<ResourceChangeResult> Apply(IReadOnlyList<ResourceChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var results = new List<ResourceChangeResult>(changes.Count);
        foreach (var change in changes)
        {
            var resource = RequireResource(change.Id);
            if (change.TargetQuantity is < 0 or > uint.MaxValue)
                throw new InvalidDataException($"资源 {change.Id.Value} 的目标值不能写成 u32。");

            var tweakId = TweakDbId.FromLocator(resource.Locator);
            var matches = _stacks.Select((stack, index) => (stack, index)).Where(item => item.stack.TweakDbId == tweakId).ToArray();
            if (matches.Length == 0)
            {
                if (!_canInsert(change.Id))
                {
                    results.Add(new ResourceChangeResult(change.Id, null, null, ChangeOutcome.NotHeld));
                    continue;
                }

                const string prefix = "tdb:";
                if (!resource.Locator.StartsWith(prefix, StringComparison.Ordinal) || resource.Locator.Length == prefix.Length)
                    throw new InvalidDataException($"资源 {change.Id.Value} 没有可插入的 TweakDB 名字。");
                ItemInserter.InsertStack(_container, resource.Locator[prefix.Length..], (uint)change.TargetQuantity);
                ReloadInventory();
                results.Add(new ResourceChangeResult(change.Id, null, change.TargetQuantity, ChangeOutcome.Inserted));
                continue;
            }

            if (matches.Length > 1)
            {
                results.Add(new ResourceChangeResult(change.Id, null, null, ChangeOutcome.Ambiguous));
                continue;
            }

            var current = matches[0].stack.Quantity;
            if (current == change.TargetQuantity)
            {
                results.Add(new ResourceChangeResult(change.Id, current, current, ChangeOutcome.Unchanged));
                continue;
            }

            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)change.TargetQuantity);
            _container.WritePayload(matches[0].stack.QuantityOffset, bytes);
            _stacks[matches[0].index] = matches[0].stack with { Quantity = change.TargetQuantity };
            results.Add(new ResourceChangeResult(change.Id, current, change.TargetQuantity, ChangeOutcome.Changed));
        }

        return results;
    }

    public void Save(Stream output) => _container.Write(output);

    public void VerifyOnlyTargetsChanged(ISaveDocument original, IReadOnlyList<ResourceChange> changes)
    {
        if (original is not Cp2077SaveDocument source)
            throw new InvalidDataException("严格校验要求原文档也是 2077 存档。");
        var inserts = changes.Where(change => source.GetQuantity(change.Id) is null).ToArray();
        if (inserts.Length == 0)
        {
            VerifyQuantityEdits(source, changes);
            return;
        }

        VerifyInsertion(source, changes, inserts);
    }

    private void ReloadInventory()
    {
        var nodes = NodeTable.Parse(_container.CopyNodeTable());
        var inventory = InventoryReader.Read(_container.Payload, nodes);
        _stacks.Clear();
        _stacks.AddRange(inventory.PlayerStacks);
        PlayerInventoryItemCount = inventory.PlayerItemCount;
    }

    private void VerifyInsertion(Cp2077SaveDocument source, IReadOnlyList<ResourceChange> changes, IReadOnlyList<ResourceChange> inserts)
    {
        var addedBytes = 45 * inserts.Count;
        var before = Cp2077Adapter.ToImage(source._container);
        var after = Cp2077Adapter.ToImage(_container);
        var diff = ResearchDiff.Compare(before, after);
        var added = AddedItems(diff, inserts.Count);
        var inventoryIndex = before.Regions.Single(region => region.Name == "inventory").Index;
        var stackTails = before.Regions
            .Where(region => region.Parent == inventoryIndex && region.Length == 29 && region.Offset >= 16)
            .Select(region => BinaryPrimitives.ReadUInt64LittleEndian(before.Payload.AsSpan(region.Offset - 8, 8)))
            .ToHashSet();
        var expected = inserts.Select(change => (Tweak: TweakDbId.FromLocator(source.RequireResource(change.Id).Locator), Quantity: (uint)change.TargetQuantity)).ToList();
        foreach (var region in added)
        {
            var body = after.Regions.First(item => item.Index == region.RightIndex && item.Name == region.Name);
            if (body.Length != 29)
                throw new InvalidDataException("严格校验失败：插入的物品不是 29 字节堆叠。");
            var tweak = BinaryPrimitives.ReadUInt64LittleEndian(after.Payload.AsSpan(body.Offset + 4, 8));
            var quantity = BinaryPrimitives.ReadUInt32LittleEndian(after.Payload.AsSpan(body.Offset + 25, 4));
            var match = expected.FindIndex(item => item.Tweak == tweak && item.Quantity == quantity);
            if (match < 0)
                throw new InvalidDataException("严格校验失败：新增物品与目标资源不一致。");
            // 物品头：TweakDBID 必须与 itemData 一致，后 8 字节（种子 + 保留）必须照抄某个已有的 29 字节堆叠。
            var header = after.Payload.AsSpan(body.Offset - 16, 16);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) != tweak || !stackTails.Contains(BinaryPrimitives.ReadUInt64LittleEndian(header[8..])))
                throw new InvalidDataException("严格校验失败：新增物品头与堆叠模板不一致。");
            expected.RemoveAt(match);
        }

        var windows = QuantityWindows(source, changes.Where(change => source.GetQuantity(change.Id) is not null));
        VerifyInsertedStructure(before, after, diff, added, addedBytes, windows);
    }

    public void InsertTweak(string tweakName, uint quantity)
    {
        ItemInserter.InsertStack(_container, tweakName, quantity);
        ReloadInventory();
    }

    public void VerifyNamedInsertion(ISaveDocument original, string tweakName, uint quantity)
    {
        if (original is not Cp2077SaveDocument source)
            throw new InvalidDataException("严格校验要求原文档也是 2077 存档。");
        var descriptor = new ResourceDescriptor(
            new ResourceId(tweakName), tweakName, tweakName, "item", uint.MaxValue, quantity, "tdb:" + tweakName);
        var before = Open(source._container, [.. source._resources, descriptor], source._canInsert);
        VerifyOnlyTargetsChanged(before, [new ResourceChange(descriptor.Id, quantity)]);
    }

    public IReadOnlyList<CloneListItem> ListCloneable()
    {
        var names = Names;
        return ItemInserter.ListNonStacks(_container).Select(slot =>
        {
            var hex = Convert.ToHexString(BitConverter.GetBytes(slot.Tweak)).ToLowerInvariant();
            var header = Convert.ToHexString(slot.Header).ToLowerInvariant();
            string? record = null;
            var label = hex;
            if (names?.Available == true)
            {
                var resolved = names.Resolve(hex);
                record = resolved.RecordName;
                if (names.IsInternal(record))
                    return null;
                label = resolved.Label;
            }

            return new CloneListItem(
                $"{slot.BagId}:{slot.Ordinal}:{header}",
                slot.BagId,
                slot.Ordinal,
                header,
                hex,
                slot.Length,
                label,
                record);
        }).Where(item => item is not null).Cast<CloneListItem>().ToArray();
    }

    public CloneApplyResult Clone(CloneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var applied = ItemInserter.CloneEquipment(_container, request.Key, request.Copies);
        ReloadInventory();
        return applied;
    }

    public void VerifyOnlyCloned(ISaveDocument original, CloneApplyResult applied)
    {
        if (original is not Cp2077SaveDocument source)
            throw new InvalidDataException("严格校验要求原文档也是 2077 存档。");
        var before = Cp2077Adapter.ToImage(source._container);
        var after = Cp2077Adapter.ToImage(_container);
        var diff = ResearchDiff.Compare(before, after);
        var added = AddedItems(diff, applied.Copies);
        var seen = new HashSet<uint>();
        foreach (var region in added)
        {
            var body = after.Regions.First(item => item.Index == region.RightIndex && item.Name == region.Name);
            if (body.Length != applied.SourceLength)
                throw new InvalidDataException("严格校验失败：新增物品的长度和来源不一致。");
            var copy = after.Payload.AsSpan(body.Offset, body.Length);
            if (!SameExcept(applied.SourceBody, copy, BodySeed, 4, ignoreFirst: true))
                throw new InvalidDataException("严格校验失败：新增物品除节点号和根种子外还有差异。");
            var seed = BinaryPrimitives.ReadUInt32LittleEndian(copy.Slice(BodySeed, 4));
            if (!applied.Seeds.Contains(seed))
                throw new InvalidDataException("严格校验失败：新增物品的种子不在本次生成的集合里。");
            if (body.Offset < 16)
                throw new InvalidDataException("严格校验失败：新增物品缺少物品头。");
            var header = after.Payload.AsSpan(body.Offset - 16, 16);
            if (!SameExcept(applied.SourceHeader, header, HeaderSeed, 4))
                throw new InvalidDataException("严格校验失败：新增物品头除根种子外还有差异。");
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(HeaderSeed, 4)) != seed)
                throw new InvalidDataException("严格校验失败：新增物品头和 itemData 的根种子不一致。");
            if (!seen.Add(seed))
                throw new InvalidDataException("严格校验失败：新增物品的根种子重复。");
        }

        VerifyInsertedStructure(before, after, diff, added, applied.AddedBytes, []);
    }

    private static readonly GenerateKind[] Kinds = GearKinds.Order
        .Select(kind => new GenerateKind(kind, GearKinds.Label(kind), GearKinds.IsStackable(kind)))
        .ToArray();

    private static readonly Dictionary<string, string> QualityWords = new(StringComparer.Ordinal)
    {
        [GearQuality.Common] = "普通",
        [GearQuality.Uncommon] = "罕见",
        [GearQuality.Rare] = "稀有",
        [GearQuality.Epic] = "史诗",
        [GearQuality.Legendary] = "传说 传奇"
    };

    /// <summary>堆叠物品不在 resources.json 里时的数量上限（与制作材料一致）。</summary>
    public const long DefaultStackMax = 99999;

    public bool GenerateAvailable => Gear?.Available == true;

    public string? GenerateMessage => GenerateAvailable ? null : Gear?.UnavailableMessage ?? ItemNameCatalog.RedmodUnavailable;

    /// <summary>只列出目录里确实有条目的分类；名字表不可用时为空。</summary>
    public IReadOnlyList<GenerateKind> GenerateKinds
    {
        get
        {
            if (!GenerateAvailable)
                return [];
            var present = Catalog().Select(entry => entry.Kind).ToHashSet(StringComparer.Ordinal);
            return Kinds.Where(kind => present.Contains(kind.Id)).ToArray();
        }
    }

    /// <summary>
    /// 分页列目录。query 按空白拆成多个词，每个词都要命中中文名、记录名、类型、分类或品质（如「传说 组件」）。
    /// </summary>
    public GenerateCatalogPage ListGeneratable(string? kind, string? query, int skip, int take)
    {
        if (skip < 0)
            throw new InvalidDataException("skip 不能是负数。");
        if (take is < 1 or > 200)
            throw new InvalidDataException("take 必须在 1 到 200 之间。");
        if (!GenerateAvailable)
            return new GenerateCatalogPage(0, []);
        var words = SearchWords(query);
        var matches = Catalog().Where(entry =>
                (string.IsNullOrEmpty(kind) || entry.Kind == kind)
                && words.All(word => Haystack(entry).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var owned = ItemInserter.ListNonStacks(_container).Select(slot => slot.Tweak).ToHashSet();
        var items = matches.Skip(skip).Take(take).Select(entry => ToItem(entry, owned)).ToArray();
        return new GenerateCatalogPage(matches.Length, items);
    }

    /// <summary>
    /// 同名合并后按卡片分页（T023）。卡片里任一条目命中全部搜索词就列出整张卡（列出全部条目，确认框里可切换），
    /// 默认条目是命中条目里品阶最高的一条；没有搜索词时就是整组的默认条目。
    /// </summary>
    public GenerateGroupPage ListGeneratableGroups(string? kind, string? query, int skip, int take)
    {
        if (skip < 0)
            throw new InvalidDataException("skip 不能是负数。");
        if (take is < 1 or > 200)
            throw new InvalidDataException("take 必须在 1 到 200 之间。");
        if (!GenerateAvailable)
            return new GenerateGroupPage(0, 0, []);
        var words = SearchWords(query);
        var matches = new List<(GearGroup Group, int Default)>();
        foreach (var group in Gear!.Groups)
        {
            if (!string.IsNullOrEmpty(kind) && group.Kind != kind)
                continue;
            var best = MatchGroup(group, words);
            if (best >= 0)
                matches.Add((group, best));
        }

        var owned = ItemInserter.ListNonStacks(_container).Select(slot => slot.Tweak).ToHashSet();
        var groups = matches.Skip(skip).Take(take).Select(match => ToGroup(match.Group, match.Default, owned)).ToArray();
        return new GenerateGroupPage(matches.Count, matches.Sum(match => match.Group.Variants.Count), groups);
    }

    /// <summary>每个分类里命中 query 的卡片数（T024）：一次遍历合并后的目录，规则与 <see cref="ListGeneratableGroups"/> 相同。</summary>
    public IReadOnlyDictionary<string, int> CountGeneratableGroups(string? query)
    {
        var counts = GenerateKinds.ToDictionary(kind => kind.Id, _ => 0, StringComparer.Ordinal);
        if (!GenerateAvailable)
            return counts;
        var words = SearchWords(query);
        foreach (var group in Gear!.Groups)
        {
            if (counts.TryGetValue(group.Kind, out var count) && MatchGroup(group, words) >= 0)
                counts[group.Kind] = count + 1;
        }

        return counts;
    }

    /// <summary>
    /// 卡片里任一条目命中全部搜索词就算命中，返回卡片的默认条目：默认条目命中就用它，否则用命中条目里品阶最高的一条；
    /// 没有搜索词时就是整组的默认条目。不命中返回 -1。
    /// </summary>
    private static int MatchGroup(GearGroup group, string[] words)
    {
        if (words.Length == 0)
            return group.DefaultIndex;
        var hits = Enumerable.Range(0, group.Variants.Count)
            .Where(i => words.All(word => Haystack(group.Variants[i]).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (hits.Length == 0)
            return -1;
        return hits.Contains(group.DefaultIndex)
            ? group.DefaultIndex
            : hits.OrderByDescending(i => group.Variants[i].Rank).First();
    }

    private GenerateCatalogGroup ToGroup(GearGroup group, int defaultIndex, HashSet<ulong> owned)
    {
        var variants = group.Variants.Select(entry => ToItem(entry, owned)).ToArray();
        var chosen = group.Variants[defaultIndex];
        return new GenerateCatalogGroup(group.Key, group.Label, group.Kind, group.Stackable, variants, defaultIndex)
        {
            VariantNoun = group.Noun,
            Quality = chosen.Quality,
            Plus = chosen.Plus,
            Iconic = chosen.Iconic,
            TypeLabel = chosen.TypeLabel
        };
    }

    public GenerateCatalogItem? FindGeneratable(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!GenerateAvailable)
            return null;
        var entry = Catalog().FirstOrDefault(item => item.RecordName == target);
        if (entry is null)
            return null;
        var owned = entry.Stackable ? new HashSet<ulong>() : ItemInserter.ListNonStacks(_container).Select(slot => slot.Tweak).ToHashSet();
        return ToItem(entry, owned);
    }

    public string? HiddenReason(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return GenerateAvailable && Gear!.IsHidden(target) ? GearCatalog.ExpansionHiddenMessage : null;
    }

    public IReadOnlyList<GenerateHiddenKind> HiddenKinds
    {
        get
        {
            if (!GenerateAvailable)
                return [];
            var hidden = Gear!.HiddenGroups;
            return GearKinds.Order
                .Select(kind => (Kind: kind, Groups: hidden.Where(group => group.Kind == kind).ToArray()))
                .Where(item => item.Groups.Length > 0)
                .Select(item => new GenerateHiddenKind(item.Kind, GearKinds.Label(item.Kind), item.Groups.Length, item.Groups.Sum(group => group.Variants.Count)))
                .ToArray();
        }
    }

    public string? HiddenNotice
    {
        get
        {
            var kinds = HiddenKinds;
            if (kinds.Count == 0)
                return null;
            return $"未检测到往日之影，物品库已隐藏 {kinds.Sum(kind => kind.Groups)} 种往日之影物品（{kinds.Sum(kind => kind.Items)} 个条目）。";
        }
    }

    public ResourceDescriptor? StackResource(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!GenerateAvailable)
            return null;
        var entry = Gear!.Stackables.FirstOrDefault(item => item.RecordName == target);
        if (entry is null)
            return null;
        var locator = "tdb:" + entry.RecordName;
        return _resources.FirstOrDefault(resource => resource.Locator == locator)
            ?? new ResourceDescriptor(new ResourceId(locator), entry.Label, entry.RecordName, entry.Kind, DefaultStackMax, null, locator);
    }

    private IEnumerable<GearEntry> Catalog()
    {
        var gear = Gear;
        return gear is null ? [] : gear.Gear.Concat(gear.Stackables);
    }

    /// <summary>按空白拆词；词首是品质词（如「传说组件」）时再拆成「传说」「组件」两个词。</summary>
    public static string[] SearchWords(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        var prefixes = QualityWords.Values.SelectMany(value => value.Split(' ')).Append("标志性").ToArray();
        var words = new List<string>();
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var prefix = prefixes.FirstOrDefault(item => word.Length > item.Length && word.StartsWith(item, StringComparison.Ordinal));
            if (prefix is null)
            {
                words.Add(word);
                continue;
            }

            words.Add(prefix);
            words.Add(word[prefix.Length..]);
        }

        return [.. words];
    }

    private static string Haystack(GearEntry entry)
    {
        var kind = GearKinds.Label(entry.Kind);
        var quality = entry.Quality is not null && QualityWords.TryGetValue(entry.Quality, out var word) ? word : "";
        return string.Join(' ', entry.Label, entry.RecordName, entry.TypeLabel, kind, quality, entry.Iconic ? "标志性" : "", entry.VariantLabel);
    }

    private GenerateCatalogItem ToItem(GearEntry entry, HashSet<ulong> ownedGear)
    {
        long? held = null;
        long? max = null;
        if (entry.Stackable)
        {
            var stacks = _stacks.Where(stack => stack.TweakDbId == entry.TweakDbId).ToArray();
            held = stacks.Length == 0 ? null : stacks.Sum(stack => stack.Quantity);
            var locator = "tdb:" + entry.RecordName;
            max = _resources.FirstOrDefault(resource => resource.Locator == locator)?.Max ?? DefaultStackMax;
        }

        return new GenerateCatalogItem(
            entry.RecordName,
            entry.Label,
            entry.RecordName,
            entry.Kind,
            entry.Stackable,
            entry.TweakDbIdHex,
            entry.LabelShared,
            entry.Stackable ? held is not null : ownedGear.Contains(entry.TweakDbId))
        {
            Quality = entry.Quality,
            Iconic = entry.Iconic,
            TypeLabel = entry.TypeLabel,
            Held = held,
            MaxQuantity = max,
            Plus = entry.Plus,
            VariantLabel = entry.VariantLabel
        };
    }

    public GenerateApplyResult Generate(GenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gear = Gear ?? throw new InvalidDataException(ItemNameCatalog.RedmodUnavailable);
        var applied = ItemGenerator.Generate(_container, request.Target, request.Copies, gear);
        ReloadInventory();
        // 物品键 = 子背包:序号:物品头；物品头前 8 字节就是模板的 TweakDBID（小端十六进制）。
        var templateHex = applied.TemplateKey.Split(':')[2][..16];
        var names = Names;
        var label = names?.Available == true ? names.Resolve(templateHex).Label : templateHex;
        return applied with { TemplateLabel = label };
    }

    /// <summary>
    /// 严格校验：新增节点恰好是本次生成的件数，逐件与生成结果的物品头和 itemData 逐字节相同，
    /// 每件的种子在原存档里都没有出现过、彼此不重复；其余按 VerifyInsertedStructure。
    /// </summary>
    public void VerifyOnlyGenerated(ISaveDocument original, GenerateApplyResult applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        if (original is not Cp2077SaveDocument source)
            throw new InvalidDataException("严格校验要求原文档也是 2077 存档。");
        if (applied.Records.Count != applied.Copies)
            throw new InvalidDataException("严格校验失败：生成结果的件数不一致。");
        var before = Cp2077Adapter.ToImage(source._container);
        var after = Cp2077Adapter.ToImage(_container);
        var diff = ResearchDiff.Compare(before, after);
        var added = AddedItems(diff, applied.Copies).OrderBy(region => region.RightIndex).ToArray();
        var seen = new HashSet<uint>();
        for (var i = 0; i < added.Length; i++)
        {
            var expected = applied.Records[i];
            var body = after.Regions.First(item => item.Index == added[i].RightIndex && item.Name == added[i].Name);
            if (body.Offset < ItemInserter.HeaderLength)
                throw new InvalidDataException("严格校验失败：新增物品缺少物品头。");
            if (!after.Payload.AsSpan(body.Offset, body.Length).SequenceEqual(expected.Body))
                throw new InvalidDataException("严格校验失败：新增物品的 itemData 与生成结果不一致。");
            if (!after.Payload.AsSpan(body.Offset - ItemInserter.HeaderLength, ItemInserter.HeaderLength).SequenceEqual(expected.Header))
                throw new InvalidDataException("严格校验失败：新增物品头与生成结果不一致。");
            if (ItemPartTree.IsQuantityStructure(expected.Header))
                VerifyQuantityItem(source._container, expected);
            else if (!expected.Seeds.Contains(expected.Seed))
                throw new InvalidDataException("严格校验失败：根种子不在本件的种子列表里。");
            foreach (var seed in expected.Seeds)
            {
                if (seed is 0 or 1 or 2 || ItemInserter.Contains(before.Payload, seed))
                    throw new InvalidDataException("严格校验失败：新增物品用了原存档里已有的种子。");
                if (!seen.Add(seed))
                    throw new InvalidDataException("严格校验失败：新增物品的种子重复。");
            }
        }

        VerifyInsertedStructure(before, after, diff, added, applied.AddedBytes, []);
    }

    /// <summary>
    /// 结构 3 的数量型物品（手雷，T022）：物品头种子固定为 2（不算新种子，也不能出现在 Seeds 里），
    /// itemData 是 45 字节前缀、数量为 1，并且原存档的玩家背包里没有同一个 16 字节 ItemID（同一种手雷只能有一份）。
    /// </summary>
    private static void VerifyQuantityItem(SaveContainer original, GeneratedRecord expected)
    {
        if (expected.Seed != ItemGenerator.GenericRootSeed
            || BinaryPrimitives.ReadUInt32LittleEndian(expected.Header.AsSpan(8)) != ItemGenerator.GenericRootSeed)
            throw new InvalidDataException("严格校验失败：手雷的根种子必须是 2。");
        if (expected.Seeds.Contains(expected.Seed))
            throw new InvalidDataException("严格校验失败：手雷的根种子 2 不能算作新种子。");
        if (ItemPartTree.Parse(expected.Body).Quantity != ItemGenerator.GrenadeQuantity)
            throw new InvalidDataException("严格校验失败：手雷的数量必须是 1。");
        if (!expected.Header.AsSpan().SequenceEqual(ItemGenerator.QuantityHeader(BinaryPrimitives.ReadUInt64LittleEndian(expected.Header))))
            throw new InvalidDataException("严格校验失败：手雷的物品头不是「记录 + 种子 2 + 03 00 00 00」。");
        if (ItemGenerator.HoldsQuantityItem(original, BinaryPrimitives.ReadUInt64LittleEndian(expected.Header)))
            throw new InvalidDataException("严格校验失败：原存档的背包里已经有同一 ID 的手雷。");
    }

    internal static RegionChange[] AddedItems(ResearchDiffResult diff, int count)
    {
        var added = diff.Regions.Where(region => region.Kind == "added").ToArray();
        if (added.Length != count || added.Any(region => region.Name != "itemData"))
            throw new InvalidDataException("严格校验失败：新增的物品节点数量不对。");
        return added;
    }

    /// <summary>
    /// 插入的结构规则（新增节点的内容由调用方各自校验）：inventory 只变长 addedBytes，自身字节只有玩家物品数变化；
    /// 其余节点只平移编号，windows 内允许改数量；头部只允许 nodeCount、解压长度和块数变化。
    /// </summary>
    internal static void VerifyInsertedStructure(
        ResearchImage before,
        ResearchImage after,
        ResearchDiffResult diff,
        IReadOnlyList<RegionChange> added,
        long addedBytes,
        IReadOnlyList<(int Start, int End)> windows)
    {
        var count = added.Count;
        var inventory = diff.Regions.SingleOrDefault(region => region.Kind == "resized" && region.Name == "inventory");
        if (inventory is null || inventory.RightLength - inventory.LeftLength != addedBytes)
            throw new InvalidDataException("严格校验失败：inventory 的长度增量不对。");
        if (diff.Regions.Any(region => region.Kind == "resized" && region.Name != "inventory"))
            throw new InvalidDataException("严格校验失败：除 inventory 外还有长度变化。");
        if (diff.Regions.Any(region => region.Kind is "removed"))
            throw new InvalidDataException("严格校验失败：有节点被删除。");
        VerifyInventoryOwnBytes(before, after, added, count);

        var firstInserted = added.Min(region => region.RightIndex ?? int.MaxValue);
        foreach (var region in diff.Regions)
        {
            if (region.Kind is "added" or "resized")
                continue;
            if (region.Kind == "indexShiftOnly")
            {
                if (!ShiftedBy(before, after, region, count, firstInserted))
                    throw new InvalidDataException($"严格校验失败：{region.Name} 的节点号增量不是 {count}。");
                continue;
            }

            if (region.Kind != "changed" || region.Spans.Any(span => !AllowedSpan(before, region, span, count, firstInserted, windows)))
                throw new InvalidDataException($"严格校验失败：{region.Name} 出现了预期之外的字节变化。");
        }

        RequireHeader(diff, "nodeCount", count);
        RequireHeader(diff, "decompressedLength", addedBytes);
        foreach (var field in diff.Header)
        {
            if (field.Name is "nodeCount" or "decompressedLength")
                continue;
            if (field.Name == "chunkCount" && ChunkCountMatches(after, field.Right))
                continue;
            throw new InvalidDataException("严格校验失败：头部还有其他字段变化。");
        }
    }

    private const int HeaderSeed = 8;
    private const int BodySeed = 12;

    /// <summary>
    /// inventory 变长时 diff 只报 resized，不比对它自己的字节（子背包 ID、计数、每件物品的 16 字节头）。
    /// 这里去掉新增的物品头和 itemData 后逐字节比对；子节点内部由各自的区域校验负责，
    /// 自身字节只允许玩家物品数这一个 u32 增加 count。
    /// </summary>
    private static void VerifyInventoryOwnBytes(ResearchImage before, ResearchImage after, IReadOnlyList<RegionChange> added, int count)
    {
        var left = before.Regions.Single(region => region.Name == "inventory");
        var right = after.Regions.Single(region => region.Name == "inventory");
        if (left.Offset != right.Offset)
            throw new InvalidDataException("严格校验失败：inventory 的位置变了。");
        var inserted = added
            .Select(region => after.Regions.First(item => item.Index == region.RightIndex))
            .Select(body => (Start: body.Offset - 16, End: body.Offset + body.Length))
            .OrderBy(range => range.Start)
            .ToArray();
        var kept = new List<byte>(left.Length);
        var cursor = right.Offset;
        foreach (var (start, end) in inserted)
        {
            if (start < cursor || end > right.Offset + right.Length)
                throw new InvalidDataException("严格校验失败：新增物品不在 inventory 内。");
            kept.AddRange(after.Payload.AsSpan(cursor, start - cursor).ToArray());
            cursor = end;
        }

        kept.AddRange(after.Payload.AsSpan(cursor, right.Offset + right.Length - cursor).ToArray());
        if (kept.Count != left.Length)
            throw new InvalidDataException("严格校验失败：去掉新增物品后 inventory 长度对不上。");

        var owned = new bool[left.Length];
        foreach (var child in before.Regions.Where(region => region.Parent == left.Index))
        {
            for (var i = child.Offset - left.Offset; i < child.Offset - left.Offset + child.Length; i++)
                owned[i] = true;
        }

        var diffs = Enumerable.Range(0, left.Length)
            .Where(i => !owned[i] && kept[i] != before.Payload[left.Offset + i])
            .ToArray();
        if (diffs.Length == 0)
            throw new InvalidDataException("严格校验失败：玩家物品数没有变化。");
        // 所有差异必须落在同一个 u32 里，且新值 - 旧值 = count。进位时差异可能从它的第 2~4 个字节开始。
        var keptBytes = kept.ToArray();
        for (var start = Math.Max(0, diffs[0] - 3); start <= diffs[0]; start++)
        {
            if (start + 4 > left.Length || diffs[^1] >= start + 4 || Enumerable.Range(start, 4).Any(i => owned[i]))
                continue;
            var old = BinaryPrimitives.ReadUInt32LittleEndian(before.Payload.AsSpan(left.Offset + start, 4));
            var now = BinaryPrimitives.ReadUInt32LittleEndian(keptBytes.AsSpan(start, 4));
            if (now - old == (uint)count)
                return;
        }
        throw new InvalidDataException("严格校验失败：inventory 自身字节的变化不是玩家物品数 +" + count + "。");
    }

    private static bool SameExcept(byte[] source, ReadOnlySpan<byte> copy, int skip, int length, bool ignoreFirst = false)
    {
        if (source.Length != copy.Length)
            return false;
        for (var i = 0; i < source.Length; i++)
        {
            if (ignoreFirst && i < 4)
                continue;
            if (i >= skip && i < skip + length)
                continue;
            if (source[i] != copy[i])
                return false;
        }

        return true;
    }

    private static bool ChunkCountMatches(ResearchImage image, string? actual)
    {
        var length = image.Header.First(field => field.Name == "decompressedLength").Value;
        var expected = (long.Parse(length, System.Globalization.CultureInfo.InvariantCulture) + Cp2077Format.DecompressedChunkSize - 1)
            / Cp2077Format.DecompressedChunkSize;
        return actual == expected.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool ShiftedBy(ResearchImage before, ResearchImage after, RegionChange region, int count, int firstInserted)
    {
        var left = before.Regions.First(item => item.Index == region.LeftIndex && item.Name == region.Name);
        var right = after.Regions.First(item => item.Index == region.RightIndex && item.Name == region.Name);
        var leftValue = BinaryPrimitives.ReadInt32LittleEndian(before.Payload.AsSpan(left.Offset, 4));
        var rightValue = BinaryPrimitives.ReadInt32LittleEndian(after.Payload.AsSpan(right.Offset, 4));
        return leftValue == left.Index && leftValue >= firstInserted && rightValue - leftValue == count;
    }

    private static bool AllowedSpan(
        ResearchImage before,
        RegionChange region,
        DiffSpan span,
        int count,
        int firstInserted,
        IReadOnlyList<(int Start, int End)> windows)
    {
        var owner = before.Regions.FirstOrDefault(item => item.Index == region.LeftIndex && item.Name == region.Name);
        if (owner is null)
            return false;
        var start = owner.Offset + span.Start;
        var end = owner.Offset + span.End;
        if (span.End - span.Start == 4
            && IsBumpedNodeIndex(before, start, ReadHex(span.Left), ReadHex(span.Right), count, firstInserted))
            return true;
        return windows.Any(window => start >= window.Start && end <= window.End);
    }

    private static bool IsBumpedNodeIndex(ResearchImage before, int absoluteStart, int oldValue, int newValue, int count, int firstInserted)
    {
        if (oldValue < firstInserted || newValue - oldValue != count)
            return false;
        return before.Regions.Any(region => region.Offset == absoluteStart && region.Index == oldValue);
    }

    private static int ReadHex(string hex)
    {
        var bytes = Convert.FromHexString(hex[..8]);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static List<(int Start, int End)> QuantityWindows(Cp2077SaveDocument source, IEnumerable<ResourceChange> edits)
    {
        var windows = new List<(int Start, int End)>();
        foreach (var change in edits)
        {
            var resource = source.RequireResource(change.Id);
            var tweakId = TweakDbId.FromLocator(resource.Locator);
            var stacks = source._stacks.Where(stack => stack.TweakDbId == tweakId).ToArray();
            if (stacks.Length != 1)
                throw new InvalidDataException($"严格校验失败：{change.Id.Value} 不是单一堆叠。");
            windows.Add((stacks[0].QuantityOffset, stacks[0].QuantityOffset + 4));
        }

        return windows;
    }

    private static void RequireHeader(ResearchDiffResult diff, string name, long delta)
    {
        var field = diff.Header.SingleOrDefault(item => item.Name == name);
        if (field?.Left is null || field.Right is null
            || !long.TryParse(field.Left, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var left)
            || !long.TryParse(field.Right, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var right)
            || right - left != delta)
            throw new InvalidDataException($"严格校验失败：头部 {name} 的增量不是 {delta}。");
    }

    private void VerifyQuantityEdits(Cp2077SaveDocument source, IReadOnlyList<ResourceChange> changes)
    {
        if (Version != source.Version)
            throw new InvalidDataException("严格校验失败：版本不一致。");
        if (_container.Timestamp != source._container.Timestamp || _container.ArchiveVersion != source._container.ArchiveVersion)
            throw new InvalidDataException("严格校验失败：时间戳或归档版本不一致。");
        if (!CopyNodeTable().AsSpan().SequenceEqual(source.CopyNodeTable()))
            throw new InvalidDataException("严格校验失败：节点表不一致。");

        var left = source._container.Payload;
        var right = _container.Payload;
        if (left.Length != right.Length)
            throw new InvalidDataException("严格校验失败：解压长度不一致。");

        var windows = new List<(int Start, int End)>();
        foreach (var change in changes)
        {
            var resource = source.RequireResource(change.Id);
            var tweakId = TweakDbId.FromLocator(resource.Locator);
            var stacks = source._stacks.Where(stack => stack.TweakDbId == tweakId).ToArray();
            if (stacks.Length != 1)
                throw new InvalidDataException($"严格校验失败：{change.Id.Value} 不是单一堆叠。");
            windows.Add((stacks[0].QuantityOffset, stacks[0].QuantityOffset + 4));
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i])
                continue;
            if (windows.Any(window => i >= window.Start && i < window.End))
                continue;
            throw new InvalidDataException($"严格校验失败：偏移 {i} 的字节被改动，但不在目标数量字段内。");
        }
    }

    private byte[] CopyNodeTable() => _container.CopyNodeTable();

    private ResourceDescriptor RequireResource(ResourceId id) =>
        _resources.FirstOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException($"未知资源 {id.Value}。");
}
