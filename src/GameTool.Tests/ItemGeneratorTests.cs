using System.Buffers.Binary;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;

namespace GameTool.Tests;

public class ItemGeneratorTests : IDisposable
{
    private static readonly TweakPresetPart[] Ajax =
    [
        new("AttachmentSlots.Receiver", "Items.w_rifle_assault__militech_ajax__rcv1"),
        new("AttachmentSlots.Barrel", "Items.w_rifle_assault__militech_ajax__base1"),
        new("AttachmentSlots.Magazine", "Items.w_rifle_assault__militech_ajax__mag_std"),
        new("AttachmentSlots.MagazineEmpty", "Items.w_rifle_assault__militech_ajax__mag_stdr")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-generate-" + Guid.NewGuid().ToString("N"));
    private readonly Cp2077Adapter _adapter = new();

    public ItemGeneratorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Keep_ReplacesRootIdAndSeed_KeepsPartTree()
    {
        var path = WriteSave("keep");
        var container = SaveContainer.Read(path);
        var template = WeaponKey(container);
        var source = TemplateBody(container);
        var generated = ItemGenerator.GenerateFromTemplate(container, template, "Items.Preset_MA70_Pimp", PartsMode.Keep, null);

        Assert.Equal(291, generated.BodyLength);
        Assert.Equal(16 + 291, generated.AddedBytes);
        Assert.Equal(3, generated.NodeIndex);
        var body = NewBody(container, generated.NodeIndex);
        var header = Convert.FromHexString(generated.HeaderHex);
        Assert.Equal(TweakDbId.FromName("Items.Preset_MA70_Pimp"), BinaryPrimitives.ReadUInt64LittleEndian(header));
        Assert.Equal(generated.Seed, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)));
        Assert.NotEqual(0x6666u, generated.Seed);
        Assert.Equal(header, body.AsSpan(4, 16).ToArray());
        Assert.Equal(source.AsSpan(20).ToArray(), body.AsSpan(20).ToArray());
        Assert.Equal(generated.NodeIndex, BinaryPrimitives.ReadInt32LittleEndian(body));
        Assert.All(generated.PartList, part => Assert.False(part.FreshSeed));

        var after = WriteContainer(container, "keep-after");
        var inventory = InventoryResearch.Read(_adapter, after, 1);
        Assert.Equal(3, inventory.Items.Count);
        Assert.Equal(generated.HeaderHex, inventory.Items[^1].HeaderHex);
        ResearchService.RequireSingleItemInsertion(ResearchService.Diff(_adapter, path, after), generated.AddedBytes);
    }

    [Fact]
    public void Auto_RebuildsPartsFromPreset()
    {
        var path = WriteSave("auto");
        var container = SaveContainer.Read(path);
        var source = ItemPartTree.Parse(TemplateBody(container));
        var generated = ItemGenerator.GenerateFromTemplate(container, WeaponKey(container), "Items.Preset_Ajax_Default", PartsMode.Auto, Ajax);

        Assert.Equal(241, generated.BodyLength);
        var tree = ItemPartTree.Parse(NewBody(container, generated.NodeIndex));
        Assert.Equal(TweakDbId.FromName("Items.w_rifle_assault__militech_ajax__rcv1"), tree.Root.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.Receiver"), tree.Root.Slot);
        Assert.Equal(source.Root.Trailer, tree.Root.Trailer);
        Assert.Equal(source.Root.ItemId.AsSpan(12).ToArray(), tree.Root.ItemId.AsSpan(12).ToArray());
        Assert.Equal(
            Ajax.Skip(1).Select(part => (TweakDbId.FromName(part.Item), TweakDbId.FromName(part.Slot))),
            tree.Root.Children.Select(part => (part.Tweak, part.Slot)));
        var model = source.Root.Children[0];
        Assert.All(tree.Root.Children, part =>
        {
            Assert.Empty(part.Children);
            Assert.Equal(model.Trailer, part.Trailer);
            Assert.Equal(model.Appearance, part.Appearance);
            Assert.Equal(model.ItemId.AsSpan(12).ToArray(), part.ItemId.AsSpan(12).ToArray());
        });

        var seeds = tree.Walk().Select(entry => entry.Part.Seed).Append(generated.Seed).ToArray();
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        var old = source.Walk().Select(entry => entry.Part.Seed).ToHashSet();
        Assert.DoesNotContain(seeds, old.Contains);
        Assert.All(generated.PartList, part => Assert.True(part.FreshSeed));
        Assert.Equal(4, generated.PartList.Count);
    }

    [Fact]
    public void ZeroPrefixIds_ClearsBytes25To37Only()
    {
        var path = WriteSave("zero");
        var plain = SaveContainer.Read(path);
        var zeroed = SaveContainer.Read(path);
        var key = WeaponKey(plain);
        var source = TemplateBody(plain);
        Assert.Contains(source.AsSpan(25, 12).ToArray(), value => value != 0);
        var a = ItemGenerator.GenerateFromTemplate(plain, key, "Items.Preset_Ajax_Default", PartsMode.Auto, Ajax);
        var b = ItemGenerator.GenerateFromTemplate(zeroed, key, "Items.Preset_Ajax_Default", PartsMode.Auto, Ajax, zeroPrefixIds: true);
        var left = NewBody(plain, a.NodeIndex);
        var right = NewBody(zeroed, b.NodeIndex);
        Assert.True(b.ZeroPrefixIds);
        Assert.Equal(left.Length, right.Length);
        Assert.All(right.AsSpan(25, 12).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(source.AsSpan(20, 5).ToArray(), right.AsSpan(20, 5).ToArray());
        Assert.Equal(source.AsSpan(37, 4).ToArray(), right.AsSpan(37, 4).ToArray());
        ItemPartTree.Parse(right);
    }

    [Fact]
    public void Rejects_BadTemplatesAndPresets()
    {
        var path = WriteSave("reject");
        var container = SaveContainer.Read(path);
        var weapon = WeaponKey(container);
        var scanned = ItemGenerator.ScanParts(container).Count;
        Assert.Equal(2, scanned);
        Assert.Contains("同一个记录", Assert.Throws<InvalidDataException>(() =>
            ItemGenerator.GenerateFromTemplate(container, weapon, "Items.Preset_MA70_Default", PartsMode.Keep, null)).Message);
        Assert.Contains("Receiver", Assert.Throws<InvalidDataException>(() =>
            ItemGenerator.GenerateFromTemplate(container, weapon, "Items.Preset_Ajax_Default", PartsMode.Auto, Ajax.Skip(1).ToArray())).Message);
        Assert.Contains("slotPartListPreset", Assert.Throws<InvalidDataException>(() =>
            ItemGenerator.GenerateFromTemplate(container, weapon, "Items.Preset_Ajax_Default", PartsMode.Auto, null)).Message);
        var jacket = ItemGenerator.ScanParts(container).Single(scan => scan.Key.StartsWith("1000000:", StringComparison.Ordinal)).Key;
        Assert.Contains("没有子配件", Assert.Throws<InvalidDataException>(() =>
            ItemGenerator.GenerateFromTemplate(container, jacket, "Items.Preset_Ajax_Default", PartsMode.Auto, Ajax)).Message);
        var money = "1:1:" + Convert.ToHexString(Header(TweakDbId.FromName("Items.money"), 2)).ToLowerInvariant();
        Assert.Contains("堆叠", Assert.Throws<InvalidDataException>(() =>
            ItemGenerator.GenerateFromTemplate(container, money, "Items.Preset_Ajax_Default", PartsMode.Keep, null)).Message);
        Assert.Equal(SaveContainer.Read(path).Payload.ToArray(), container.Payload.ToArray());

        var jacketGenerated = ItemGenerator.GenerateFromTemplate(container, jacket, "Items.Jacket_01_basic_01", PartsMode.Keep, null);
        Assert.Equal(91, jacketGenerated.BodyLength);
        Assert.Equal(3, jacketGenerated.NodeIndex);
    }

    private static string WeaponKey(SaveContainer container) =>
        ItemGenerator.ScanParts(container).Single(scan => scan.Key.StartsWith("1:", StringComparison.Ordinal)).Key;

    private static byte[] TemplateBody(SaveContainer container)
    {
        var nodes = NodeTable.Parse(container.CopyNodeTable());
        return container.Payload.Slice(nodes[2].PayloadOffset, nodes[2].Length).ToArray();
    }

    private static byte[] NewBody(SaveContainer container, int index)
    {
        var node = NodeTable.Parse(container.CopyNodeTable())[index];
        return container.Payload.Slice(node.PayloadOffset, node.Length).ToArray();
    }

    private string WriteSave(string name)
    {
        var money = TweakDbId.FromName("Items.money");
        var payload = new List<byte>();
        payload.AddRange(new byte[4]);
        payload.AddRange(U32(2));
        payload.AddRange(U64(1));
        payload.AddRange(U32(2));
        payload.AddRange(Header(money, 2));
        var moneyOffset = payload.Count;
        payload.AddRange(U32(1));
        payload.AddRange(U64(money));
        payload.AddRange(U32(2));
        payload.AddRange(new byte[9]);
        payload.AddRange(U32(100));
        var weapon = ItemPartTreeTests.Body("Items.Preset_MA70_Default", 0x6666,
            ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__rcv1"), 0x70, 0x6FEE, "None", TweakDbId.FromName("AttachmentSlots.Receiver"),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.DummyBlankMod5"), 0x71, 0x6FEF, "None", TweakDbId.FromName("AttachmentSlots.Power_AR_SMG_LMG_WeaponMod2")),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_std"), 0x72, 0x6FF0, "None", TweakDbId.FromName("AttachmentSlots.Magazine")),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_stdr"), 0x73, 0x6FF1, "None", TweakDbId.FromName("AttachmentSlots.MagazineEmpty")),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__base1"), 0x74, 0x6FF2, "None", TweakDbId.FromName("AttachmentSlots.Barrel"))),
            nodeIndex: 2);
        payload.AddRange(weapon.AsSpan(4, 16).ToArray());
        var weaponOffset = payload.Count;
        payload.AddRange(weapon);
        payload.AddRange(U64(1_000_000));
        payload.AddRange(U32(1));
        var jacket = ItemPartTreeTests.Body("Items.SQ031_Samurai_Jacket", 0x8888,
            ItemPartTreeTests.Part(TweakDbId.FromName("Items.GenericItemRoot"), 2, 0, "None", TweakDbId.FromName("AttachmentSlots.GenericItemRoot")),
            nodeIndex: 3);
        payload.AddRange(jacket.AsSpan(4, 16).ToArray());
        var jacketOffset = payload.Count;
        payload.AddRange(jacket);

        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E, 4 };
        Node(nodes, "inventory", -1, 1, 0, payload.Count);
        Node(nodes, "itemData", 2, -1, moneyOffset, 29);
        Node(nodes, "itemData", 3, -1, weaponOffset, weapon.Length);
        Node(nodes, "itemData", -1, -1, jacketOffset, jacket.Length);
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [payload.ToArray()],
            nodeTable: nodes.ToArray());
        return WriteContainer(container, name);
    }

    private string WriteContainer(SaveContainer container, string name)
    {
        var path = Path.Combine(_root, name, "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        container.Write(stream);
        return path;
    }

    private static byte[] Header(ulong tweak, uint seed) => [.. U64(tweak), .. U32(seed), 0, 0, 0, 0];

    private static void Node(List<byte> buffer, string name, int next, int child, int payloadOffset, int length)
    {
        buffer.Add((byte)(0x80 | name.Length));
        buffer.AddRange(Encoding.ASCII.GetBytes(name));
        buffer.AddRange(U32((uint)next));
        buffer.AddRange(U32((uint)child));
        buffer.AddRange(U32((uint)(Cp2077Format.FirstChunkOffset + payloadOffset)));
        buffer.AddRange(U32((uint)length));
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }
}
