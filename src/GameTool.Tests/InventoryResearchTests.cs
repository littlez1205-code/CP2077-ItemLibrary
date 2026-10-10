using System.Buffers.Binary;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Nodes;

namespace GameTool.Tests;

public class InventoryResearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-inventory-" + Guid.NewGuid().ToString("N"));
    private readonly Cp2077Adapter _adapter = new();

    public InventoryResearchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_ListsStackNameAndEveryBag()
    {
        var money = TweakDbId.FromName("Items.money");
        var other = TweakDbId.FromName("Items.CommonMaterial1");
        var path = WriteSave(Build(money, 100, other, 7, extra: null));
        var inventory = InventoryResearch.Read(_adapter, path, 1);
        var item = Assert.Single(inventory.Items);
        Assert.Equal(1, item.Ordinal);
        Assert.Equal(1, item.NodeIndex);
        Assert.Equal(29, item.Length);
        Assert.True(item.Stack);
        Assert.Equal(100u, item.Quantity);
        Assert.Equal("Items.money", item.Name);
        Assert.StartsWith(Convert.ToHexString(BitConverter.GetBytes(money)).ToLowerInvariant(), item.HeaderHex);
        Assert.Equal(32, item.HeaderHex.Length);

        var bags = InventoryResearch.Bags(path);
        Assert.Equal(2, bags.Count);
        Assert.Equal((1ul, 1), (bags[0].Id, bags[0].Items));
        Assert.Equal((2ul, 1), (bags[1].Id, bags[1].Items));
    }

    [Fact]
    public void Compare_ReportsAnAddedItem()
    {
        var money = TweakDbId.FromName("Items.money");
        var other = TweakDbId.FromName("Items.CommonMaterial1");
        var added = TweakDbId.FromName("Items.RareMaterial1");
        var before = WriteSave(Build(money, 100, other, 7, extra: null), "before");
        var after = WriteSave(Build(money, 100, other, 7, extra: added), "after");
        var compare = InventoryResearch.Compare(_adapter, before, after, InventoryResearch.DefaultBagId);
        var change = Assert.Single(compare.Changes);
        Assert.Equal("added", change.Kind);
        Assert.Equal("Items.RareMaterial1", change.Name);
        Assert.Equal(2, change.RightOrdinal);
        Assert.Null(change.LeftNode);
    }

    private string WriteSave((byte[] Payload, byte[] Nodes) save, string name = "slot")
    {
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [save.Payload],
            nodeTable: save.Nodes);
        var path = Path.Combine(_root, name, "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        container.Write(stream);
        return path;
    }

    private static (byte[] Payload, byte[] Nodes) Build(ulong money, uint moneyQuantity, ulong other, uint otherQuantity, ulong? extra)
    {
        var payload = new List<byte>();
        WriteInt32(payload, 0);
        WriteUInt32(payload, 2);
        WriteUInt64(payload, 1);
        WriteUInt32(payload, extra is null ? 1u : 2u);
        var moneyOffset = AppendItem(payload, 1, money, moneyQuantity);
        int? extraOffset = null;
        if (extra is ulong added)
            extraOffset = AppendItem(payload, 2, added, 1);
        WriteUInt64(payload, 2);
        WriteUInt32(payload, 1);
        var otherOffset = AppendItem(payload, extra is null ? 2 : 3, other, otherQuantity);

        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, extra is null ? 3 : 4);
        WriteNode(nodes, "inventory", -1, 1, 0, payload.Count);
        WriteNode(nodes, "itemData", extra is null ? 2 : 2, -1, moneyOffset, 29);
        if (extraOffset is int extraAt)
            WriteNode(nodes, "itemData", 3, -1, extraAt, 29);
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
