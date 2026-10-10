using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

public class ItemInserterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-insert-" + Guid.NewGuid().ToString("N"));
    private readonly Cp2077Adapter _adapter = new();

    public ItemInserterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void InsertStack_RewritesNodesCountsAndOffsets()
    {
        var before = WriteSave(Sample(), "before");
        var container = SaveContainer.Read(before);
        var insertion = ItemInserter.InsertStack(container, "Items.RareMaterial1", 5);
        var after = Path.Combine(_root, "after", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        using (var stream = File.Create(after))
            container.Write(stream);

        Assert.Equal(45, insertion.AddedBytes);
        var written = SaveContainer.Read(after);
        var nodes = NodeTable.Parse(written.CopyNodeTable());
        Assert.Equal(4, nodes.Count);
        Assert.Equal(2, insertion.NodeIndex);
        Assert.Equal(2, nodes[1].Next);
        Assert.Equal("itemData", nodes[2].Name);
        Assert.Equal(29, nodes[2].Length);
        Assert.Equal(-1, nodes[2].Child);
        Assert.Equal(3, nodes[2].Next);
        Assert.Equal(81, nodes[2].PayloadOffset);
        Assert.Equal(3, nodes[3].Index);
        Assert.Equal(138, nodes[3].PayloadOffset);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(written.Payload.Slice(nodes[3].PayloadOffset, 4)));
        Assert.Equal(SampleLength() + 45, nodes[0].Length);

        var inventory = InventoryResearch.Read(_adapter, after, 1);
        Assert.Equal(2, inventory.Items.Count);
        var added = inventory.Items[1];
        Assert.Equal("Items.RareMaterial1", added.Name);
        Assert.Equal(5u, added.Quantity);
        Assert.Equal(2, added.NodeIndex);
        _adapter.Load(after);
        ResearchService.RequireSingleItemInsertion(ResearchService.Diff(_adapter, before, after), 45);
    }

    [Fact]
    public void InsertClone_ReplacesSeedInHeaderAndBody()
    {
        var before = WriteSave(Sample(), "clone-before");
        var container = SaveContainer.Read(before);
        const uint seed = 0xA1B2C3D4;
        var insertion = ItemInserter.InsertClone(container, ordinal: 1, seed);
        var after = WriteContainer(container, "clone-after");
        var item = InventoryResearch.Read(_adapter, after, 1).Items[^1];
        var header = Convert.FromHexString(item.HeaderHex);
        Assert.Equal(seed, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)));
        var image = _adapter.OpenForResearch(after);
        var region = image.Regions.Single(node => node.Index == insertion.NodeIndex);
        Assert.Equal(seed, BinaryPrimitives.ReadUInt32LittleEndian(image.Payload.AsSpan(region.Offset + 12, 4)));
        Assert.Equal(seed, insertion.Seed);
    }

    [Fact]
    public void RequireSingleItemInsertion_RejectsAnExtraByte()
    {
        var before = WriteSave(Sample(), "diff-before");
        var container = SaveContainer.Read(before);
        ItemInserter.InsertStack(container, "Items.RareMaterial1", 5);
        var after = WriteContainer(container, "diff-after");
        var tweaked = SaveContainer.Read(after);
        var payload = tweaked.Payload.ToArray();
        var shifted = NodeTable.Parse(tweaked.CopyNodeTable())[3];
        payload[shifted.PayloadOffset + 11] = 0x7F;
        tweaked.ReplacePayload(payload);
        var dirty = WriteContainer(tweaked, "diff-dirty");
        var error = Assert.Throws<ResearchRejectedException>(() =>
            ResearchService.RequireSingleItemInsertion(ResearchService.Diff(_adapter, before, dirty), 45));
        Assert.Contains("自检失败", error.Message);
    }

    [Fact]
    public void ValidateInsertOutput_RejectsExistingInsideAndBadNames()
    {
        var slot = Path.Combine(_root, "ManualSave-0");
        Directory.CreateDirectory(slot);
        var save = Path.Combine(slot, "sav.dat");
        File.WriteAllBytes(save, [1]);
        var existing = Path.Combine(_root, "taken");
        Directory.CreateDirectory(existing);
        Assert.Contains("已经存在", Assert.Throws<ResearchRejectedException>(() =>
            ResearchService.ValidateInsertOutput(save, existing, intoSaveRoot: false)).Message);

        var inside = Path.Combine(_root, "ManualSave-90");
        Assert.Contains("into-save-root", Assert.Throws<ResearchRejectedException>(() =>
            ResearchService.ValidateInsertOutput(save, inside, intoSaveRoot: false)).Message);
        Assert.Contains("ManualSave-90", Assert.Throws<ResearchRejectedException>(() =>
            ResearchService.ValidateInsertOutput(save, Path.Combine(_root, "ManualSave-89"), intoSaveRoot: true)).Message);
        ResearchService.ValidateInsertOutput(save, inside, intoSaveRoot: true);
        var outside = Path.Combine(Path.GetTempPath(), "GameTool-insert-out-" + Guid.NewGuid().ToString("N"));
        ResearchService.ValidateInsertOutput(save, outside, intoSaveRoot: false);
    }

    [Fact]
    public void Patch_InsertsOneThenThreeMissingStacks()
    {
        var path = WriteSave(Sample(), "stacks");
        var one = PatchEngine.Patch(_adapter, path, [new ResourceChange(new ResourceId("craft.t3"), 4)], new PatchOptions(null));
        Assert.Equal(ChangeOutcome.Inserted, Assert.Single(one.Results).Outcome);
        Assert.Equal(4, _adapter.Load(path).GetQuantity(new ResourceId("craft.t3")));

        var mixed = PatchEngine.Patch(_adapter, path,
        [
            new ResourceChange(new ResourceId("money"), 80),
            new ResourceChange(new ResourceId("craft.t4"), 1),
            new ResourceChange(new ResourceId("craft.t5"), 2)
        ], new PatchOptions(null));
        Assert.Contains(mixed.Results, result => result.Outcome == ChangeOutcome.Changed);
        Assert.Equal(2, mixed.Results.Count(result => result.Outcome == ChangeOutcome.Inserted));
        var reloaded = _adapter.Load(path);
        Assert.Equal(80, reloaded.GetQuantity(new ResourceId("money")));
        Assert.Equal(1, reloaded.GetQuantity(new ResourceId("craft.t4")));
        Assert.Equal(2, reloaded.GetQuantity(new ResourceId("craft.t5")));
    }

    [Fact]
    public void InsertStack_SplitsWhenPayloadCrossesChunkBoundary()
    {
        var (payload, nodes) = Sample();
        var padded = new byte[Cp2077Format.DecompressedChunkSize - 10];
        payload.CopyTo(padded, 0);
        var path = WriteSave((padded, nodes), "chunk");
        var container = SaveContainer.Read(path);
        Assert.Equal(1, container.ChunkCount);
        ItemInserter.InsertStack(container, "Items.RareMaterial1", 1);
        Assert.Equal(2, container.ChunkCount);
        Assert.Equal(Cp2077Format.DecompressedChunkSize - 10 + 45, container.DecompressedLength);
    }

    [Fact]
    public void StrictVerify_RejectsAnExtraByteAfterInsert()
    {
        var path = WriteSave(Sample(), "strict");
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var working = (Cp2077SaveDocument)_adapter.Load(path);
        var change = new ResourceChange(new ResourceId("craft.t3"), 4);
        Assert.Equal(ChangeOutcome.Inserted, Assert.Single(working.Apply([change])).Outcome);
        working.VerifyOnlyTargetsChanged(before, [change]);

        var saved = Path.Combine(_root, "strict-out.dat");
        using (var stream = File.Create(saved))
            working.Save(stream);
        var tweaked = SaveContainer.Read(saved);
        var payload = tweaked.Payload.ToArray();
        var money = NodeTable.Parse(tweaked.CopyNodeTable())[1];
        payload[money.PayloadOffset + 20] = 0x7F;
        tweaked.ReplacePayload(payload);
        var dirty = WriteContainer(tweaked, "strict-dirty");
        var after = (Cp2077SaveDocument)_adapter.Load(dirty);
        var error = Assert.Throws<InvalidDataException>(() => after.VerifyOnlyTargetsChanged(before, [change]));
        Assert.Contains("严格校验失败", error.Message);
    }

    [Fact]
    public void StrictVerify_RejectsAnotherItemHeaderChangeAfterInsert()
    {
        // inventory 变长时 diff 不比对它自身的字节；另一件物品的 16 字节头被改必须被拦下。
        var path = WriteSave(Sample(), "strict-header");
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var working = (Cp2077SaveDocument)_adapter.Load(path);
        var change = new ResourceChange(new ResourceId("craft.t3"), 4);
        Assert.Equal(ChangeOutcome.Inserted, Assert.Single(working.Apply([change])).Outcome);
        var container = Container(working);
        // 插入后 [2] 是新物品，原来的 CommonMaterial1 移到 [3]。
        var common = NodeTable.Parse(container.CopyNodeTable())[3];
        container.WritePayload(common.PayloadOffset - 4, [0x7F]);
        var error = Assert.Throws<InvalidDataException>(() => working.VerifyOnlyTargetsChanged(before, [change]));
        Assert.Contains("inventory 自身字节", error.Message);

        var inserted = (Cp2077SaveDocument)_adapter.Load(path);
        inserted.Apply([change]);
        var added = NodeTable.Parse(Container(inserted).CopyNodeTable())[2];
        Container(inserted).WritePayload(added.PayloadOffset - 4, [0x7F]);
        Assert.Contains("物品头", Assert.Throws<InvalidDataException>(() => inserted.VerifyOnlyTargetsChanged(before, [change])).Message);
    }

    [Fact]
    public void StrictVerify_RejectsANonIndexInt32PlusOne()
    {
        var path = WriteSave(Sample(), "qty-plus");
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var working = (Cp2077SaveDocument)_adapter.Load(path);
        var change = new ResourceChange(new ResourceId("craft.t3"), 4);
        working.Apply([change]);
        var saved = Path.Combine(_root, "qty-plus-out.dat");
        using (var stream = File.Create(saved))
            working.Save(stream);
        var tweaked = SaveContainer.Read(saved);
        var payload = tweaked.Payload.ToArray();
        var common = NodeTable.Parse(tweaked.CopyNodeTable())[2];
        var quantity = common.PayloadOffset + 25;
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(quantity, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(quantity, 4), value + 1);
        tweaked.ReplacePayload(payload);
        var dirty = WriteContainer(tweaked, "qty-plus-dirty");
        var after = (Cp2077SaveDocument)_adapter.Load(dirty);
        var error = Assert.Throws<InvalidDataException>(() => after.VerifyOnlyTargetsChanged(before, [change]));
        Assert.Contains("严格校验失败", error.Message);
    }

    [Fact]
    public void StrictVerify_RejectsBumpingANodeBelowTheInsert()
    {
        var path = WriteSave(Sample(), "low-index");
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var working = (Cp2077SaveDocument)_adapter.Load(path);
        var change = new ResourceChange(new ResourceId("craft.t3"), 4);
        working.Apply([change]);
        var container = (SaveContainer)typeof(Cp2077SaveDocument)
            .GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(working)!;
        var payload = container.Payload.ToArray();
        var money = NodeTable.Parse(container.CopyNodeTable())[1];
        var index = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(money.PayloadOffset, 4));
        Assert.True(index < 3);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(money.PayloadOffset, 4), index + 1);
        container.ReplacePayload(payload);
        var error = Assert.Throws<InvalidDataException>(() => working.VerifyOnlyTargetsChanged(before, [change]));
        Assert.Contains("严格校验失败", error.Message);
    }

    [Fact]
    public void InsertStack_RejectsWhenNoStackTemplate()
    {
        var payload = new List<byte>();
        WriteInt32(payload, 0);
        WriteUInt32(payload, 1);
        WriteUInt64(payload, 1);
        WriteUInt32(payload, 0);
        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, 1);
        WriteNode(nodes, "inventory", -1, -1, 0, payload.Count);
        var path = WriteSave((payload.ToArray(), nodes.ToArray()), "empty");
        var error = Assert.Throws<InvalidDataException>(() =>
            ItemInserter.InsertStack(SaveContainer.Read(path), "Items.RareMaterial1", 1));
        Assert.Contains("没有 29 字节", error.Message);
    }

    [Fact]
    public void RemoveStack_DeletesMiddleAndLast_AndRoundTripsTheLast()
    {
        var original = WriteSave(Three(), "three");
        var middle = SaveContainer.Read(original);
        var removed = ItemInserter.RemoveStack(middle, "Items.CommonMaterial1");
        Assert.Equal(2, removed.NodeIndex);
        Assert.Equal(45, removed.RemovedBytes);
        var midPath = WriteContainer(middle, "middle");
        var nodes = NodeTable.Parse(SaveContainer.Read(midPath).CopyNodeTable());
        Assert.Equal(3, nodes.Count);
        Assert.Equal(2, nodes[1].Next);
        Assert.Equal(-1, nodes[2].Next);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(SaveContainer.Read(midPath).Payload.Slice(nodes[2].PayloadOffset, 4)));
        var bag = InventoryResearch.Read(_adapter, midPath, 1);
        Assert.Equal(2, bag.Items.Count);
        Assert.DoesNotContain(bag.Items, item => item.Name == "Items.CommonMaterial1");
        ResearchService.RequireSingleStackRemoval(
            _adapter.OpenForResearch(original),
            _adapter.OpenForResearch(midPath),
            45,
            removed.NodeIndex,
            Cp2077Format.DecompressedChunkSize);
        Assert.Null(_adapter.Load(midPath).GetQuantity(new ResourceId("craft.t1")));

        var round = SaveContainer.Read(original);
        ItemInserter.RemoveStack(round, "Items.RareMaterial1");
        ItemInserter.InsertStack(round, "Items.RareMaterial1", 9);
        var restored = WriteContainer(round, "round");
        var source = SaveContainer.Read(original);
        var back = SaveContainer.Read(restored);
        Assert.True(source.Payload.SequenceEqual(back.Payload));
        Assert.True(source.CopyNodeTable().AsSpan().SequenceEqual(back.CopyNodeTable()));
    }

    [Fact]
    public void RemoveStack_RejectsDuplicateAndMissing()
    {
        var path = WriteSave(TwoCommons(), "reject");
        var duplicate = SaveContainer.Read(path);
        Assert.Contains("不止一个", Assert.Throws<InvalidDataException>(() =>
            ItemInserter.RemoveStack(duplicate, "Items.CommonMaterial1")).Message);

        var missing = SaveContainer.Read(path);
        Assert.Contains("没有 29 字节", Assert.Throws<InvalidDataException>(() =>
            ItemInserter.RemoveStack(missing, "Items.EpicMaterial1")).Message);
    }

    [Fact]
    public void HeldQuantityResearch_PassesWhenOneResourceIsMissing()
    {
        var held = _adapter.Resources.Where(resource => resource.Id.Value != "craft.t5").ToArray();
        var path = WriteSave(Bag(held.Select(resource => (resource.Locator["tdb:".Length..], 3u)).ToArray()), "research");
        var before = Path.Combine(_root, "research-before", "sav.dat");
        var after = Path.Combine(_root, "research-after", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(before)!);
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        ResearchService.PrepareHeldQuantityPair(_adapter, path, before, after);
        Assert.Equal("pass", ResearchService.DescribeHeldQuantityResearch(_adapter, before, after));
        Assert.Null(_adapter.Load(after).GetQuantity(new ResourceId("craft.t5")));
        Assert.Equal(9999, _adapter.Load(after).GetQuantity(new ResourceId("craft.t1")));
    }

    [Fact]
    public void InsertClone_RejectsReservedSeeds()
    {
        var container = SaveContainer.Read(WriteSave(Sample(), "seed"));
        foreach (var seed in new uint[] { 0, 1, 2 })
            Assert.Contains("种子", Assert.Throws<InvalidDataException>(() => ItemInserter.InsertClone(container, 1, seed)).Message);
    }

    [Fact]
    public void CloneEquipment_RejectsHeaderMismatch_AndCopiesFromStash()
    {
        var path = WriteSave(Gear(), "stash");
        var before = Open(path);
        var listed = before.ListCloneable();
        var player = listed.Single(item => item.BagId == InventoryResearch.DefaultBagId);
        var bad = player.Key[..^2] + (player.Key.EndsWith("00", StringComparison.Ordinal) ? "ff" : "00");
        Assert.Contains("物品头", Assert.Throws<InvalidDataException>(() => before.Clone(new CloneRequest(bad, 1))).Message);

        var fresh = Open(path);
        var stash = fresh.ListCloneable().Single(item => item.BagId == ItemInserter.StashBagId);
        var applied = fresh.Clone(new CloneRequest(stash.Key, 1));
        Assert.Equal(before.PlayerItemCount + 1, fresh.PlayerItemCount);
        Assert.Single(applied.Seeds);
        fresh.VerifyOnlyCloned(Open(path), applied);
    }

    [Fact]
    public void CloneEquipment_ThreeCopies_ShiftLaterNodeIndexesByThree()
    {
        var path = WriteSave(Gear(), "three");
        var original = SaveContainer.Read(path);
        var stashIndex = NodeIndex(original, 0x11111111);
        var document = Open(path);
        var player = document.ListCloneable().Single(item => item.BagId == InventoryResearch.DefaultBagId);
        var applied = document.Clone(new CloneRequest(player.Key, 3));
        Assert.Equal(3, applied.Seeds.Count);
        Assert.Equal(stashIndex + 3, NodeIndex(Container(document), 0x11111111));
        document.VerifyOnlyCloned(Open(path), applied);
    }

    [Fact]
    public void VerifyOnlyCloned_RejectsSourceContent_AttachmentSeed_AndExtraByte()
    {
        var path = WriteSave(Gear(), "strict");
        var source = Open(path);
        var working = Open(path);
        var key = working.ListCloneable().Single(item => item.BagId == InventoryResearch.DefaultBagId).Key;
        var applied = working.Clone(new CloneRequest(key, 1));
        applied.SourceBody[20] ^= 0x7F;
        Assert.Contains("根种子", Assert.Throws<InvalidDataException>(() => working.VerifyOnlyCloned(source, applied)).Message);

        var attachment = Open(path);
        var attachmentResult = attachment.Clone(new CloneRequest(key, 1));
        FlipNewBody(attachment, attachmentResult.Seeds[0], 41);
        Assert.Contains("根种子", Assert.Throws<InvalidDataException>(() => attachment.VerifyOnlyCloned(Open(path), attachmentResult)).Message);

        var extra = Open(path);
        var extraResult = extra.Clone(new CloneRequest(key, 1));
        FlipNewBody(extra, extraResult.Seeds[0], 20);
        Assert.Contains("根种子", Assert.Throws<InvalidDataException>(() => extra.VerifyOnlyCloned(Open(path), extraResult)).Message);

        // 新增物品之外、另一件物品的 16 字节头被改：只有 inventory 自身字节的比对能拦下。
        var header = Open(path);
        var headerResult = header.Clone(new CloneRequest(key, 1));
        var stashBody = LastSeed(Container(header).Payload.ToArray(), 0x11111111) - 12;
        Container(header).WritePayload(stashBody - 4, [0x7F]);
        Assert.Contains("inventory 自身字节", Assert.Throws<InvalidDataException>(() => header.VerifyOnlyCloned(Open(path), headerResult)).Message);
    }

    private Cp2077SaveDocument Open(string path) =>
        Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);

    private static SaveContainer Container(Cp2077SaveDocument document) =>
        (SaveContainer)typeof(Cp2077SaveDocument).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;

    private static int NodeIndex(SaveContainer container, uint seed)
    {
        var payload = container.Payload.ToArray();
        var at = LastSeed(payload, seed);
        return BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(at - 12, 4));
    }

    private static void FlipNewBody(Cp2077SaveDocument document, uint seed, int bodyOffset)
    {
        var container = Container(document);
        var payload = container.Payload.ToArray();
        var at = LastSeed(payload, seed);
        container.WritePayload(at - 12 + bodyOffset, [0x7F]);
    }

    private static int LastSeed(byte[] payload, uint seed)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, seed);
        var found = -1;
        for (var i = 0; i <= payload.Length - 4; i++)
        {
            if (payload.AsSpan(i, 4).SequenceEqual(bytes))
                found = i;
        }

        return found;
    }

    private string WriteContainer(SaveContainer container, string name)
    {
        var path = Path.Combine(_root, name, "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        container.Write(stream);
        return path;
    }

    private string WriteSave((byte[] Payload, byte[] Nodes) save, string name)
    {
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [save.Payload],
            nodeTable: save.Nodes);
        return WriteContainer(container, name);
    }

    private static int SampleLength() => Sample().Payload.Length;

    private static (byte[] Payload, byte[] Nodes) Three() =>
        Bag(new (string Name, uint Quantity)[]
        {
            ("Items.money", 100),
            ("Items.CommonMaterial1", 7),
            ("Items.RareMaterial1", 9)
        });

    private static (byte[] Payload, byte[] Nodes) TwoCommons() =>
        Bag(new (string Name, uint Quantity)[]
        {
            ("Items.CommonMaterial1", 1),
            ("Items.CommonMaterial1", 2)
        });

    private static (byte[] Payload, byte[] Nodes) Bag((string Name, uint Quantity)[] items)
    {
        var payload = new List<byte>();
        WriteInt32(payload, 0);
        WriteUInt32(payload, 1);
        WriteUInt64(payload, 1);
        WriteUInt32(payload, (uint)items.Length);
        var offsets = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
            offsets[i] = AppendItem(payload, i + 1, TweakDbId.FromName(items[i].Name), items[i].Quantity);
        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, items.Length + 1);
        WriteNode(nodes, "inventory", -1, 1, 0, payload.Count);
        for (var i = 0; i < items.Length; i++)
            WriteNode(nodes, "itemData", i + 1 == items.Length ? -1 : i + 2, -1, offsets[i], 29);
        return (payload.ToArray(), nodes.ToArray());
    }

    private static (byte[] Payload, byte[] Nodes) Gear()
    {
        var playerId = TweakDbId.FromName("Items.Preset_MA70_Default");
        var stashId = TweakDbId.FromName("Items.Preset_Stash_Default");
        var payload = new List<byte>();
        WriteInt32(payload, 0);
        WriteUInt32(payload, 2);
        WriteUInt64(payload, 1);
        WriteUInt32(payload, 1);
        var playerOffset = AppendGear(payload, 1, playerId, 0x22222222);
        WriteUInt64(payload, ItemInserter.StashBagId);
        WriteUInt32(payload, 1);
        var stashOffset = AppendGear(payload, 2, stashId, 0x11111111);
        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, 3);
        WriteNode(nodes, "inventory", -1, 1, 0, payload.Count);
        WriteNode(nodes, "itemData", 2, -1, playerOffset, 91);
        WriteNode(nodes, "itemData", -1, -1, stashOffset, 91);
        return (payload.ToArray(), nodes.ToArray());
    }

    private static int AppendGear(List<byte> buffer, int nodeIndex, ulong tweakId, uint seed)
    {
        var header = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(header, tweakId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), seed);
        buffer.AddRange(header);
        var offset = buffer.Count;
        var body = new byte[91];
        BinaryPrimitives.WriteInt32LittleEndian(body, nodeIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(4), tweakId);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), seed);
        body[41] = 0x5A;
        buffer.AddRange(body);
        return offset;
    }

    private static (byte[] Payload, byte[] Nodes) Sample()
    {
        var money = TweakDbId.FromName("Items.money");
        var other = TweakDbId.FromName("Items.CommonMaterial1");
        var payload = new List<byte>();
        WriteInt32(payload, 0);
        WriteUInt32(payload, 2);
        WriteUInt64(payload, 1);
        WriteUInt32(payload, 1);
        var moneyOffset = AppendItem(payload, 1, money, 100);
        WriteUInt64(payload, 2);
        WriteUInt32(payload, 1);
        var otherOffset = AppendItem(payload, 2, other, 7);
        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, 3);
        WriteNode(nodes, "inventory", -1, 1, 0, payload.Count);
        WriteNode(nodes, "itemData", 2, -1, moneyOffset, 29);
        WriteNode(nodes, "itemData", -1, -1, otherOffset, 29);
        return (payload.ToArray(), nodes.ToArray());
    }

    private static int AppendItem(List<byte> buffer, int nodeIndex, ulong tweakId, uint quantity)
    {
        var header = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(header, tweakId);
        buffer.AddRange(header);
        var offset = buffer.Count;
        WriteInt32(buffer, nodeIndex);
        WriteUInt64(buffer, tweakId);
        buffer.AddRange(new byte[13]);
        WriteUInt32(buffer, quantity);
        return offset;
    }

    private static void WriteNode(List<byte> buffer, string name, int next, int child, int payloadOffset, int length)
    {
        WriteCompact(buffer, -Encoding.ASCII.GetByteCount(name));
        buffer.AddRange(Encoding.ASCII.GetBytes(name));
        WriteInt32(buffer, next);
        WriteInt32(buffer, child);
        WriteInt32(buffer, Cp2077Format.FirstChunkOffset + payloadOffset);
        WriteInt32(buffer, length);
    }

    private static void WriteCompact(List<byte> buffer, int value)
    {
        var negative = value < 0;
        var magnitude = (uint)(negative ? -value : value);
        var first = (byte)(magnitude & 0x3F);
        if (negative)
            first |= 0x80;
        magnitude >>= 6;
        if (magnitude != 0)
            first |= 0x40;
        buffer.Add(first);
        while (magnitude != 0)
        {
            var next = (byte)(magnitude & 0x7F);
            magnitude >>= 7;
            if (magnitude != 0)
                next |= 0x80;
            buffer.Add(next);
        }
    }

    private static void WriteInt32(List<byte> buffer, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    private static void WriteUInt64(List<byte> buffer, ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        buffer.AddRange(bytes);
    }
}
