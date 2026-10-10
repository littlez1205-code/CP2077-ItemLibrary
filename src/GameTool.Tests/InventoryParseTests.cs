using System.Buffers.Binary;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.Core;

namespace GameTool.Tests;

public class InventoryParseTests
{
    [Fact]
    public void ReadsPlayerBagOnly_AndNullWhenMissing()
    {
        var money = TweakDbId.FromName("Items.money");
        var payload = BuildPayload(money, playerQuantity: 100, otherQuantity: 999);
        var table = BuildNodeTable(payload);
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [payload],
            nodeTable: table);

        var resources = new[]
        {
            new ResourceDescriptor(new ResourceId("money"), "欧元", "Eurodollar", "currency", 100, 1, "tdb:Items.money"),
            new ResourceDescriptor(new ResourceId("craft.t5"), "传奇制作组件", "Legendary", "material", 100, 1, "tdb:Items.LegendaryMaterial1")
        };
        var document = Cp2077SaveDocument.Open(container, resources);

        Assert.Equal(1, document.PlayerInventoryItemCount);
        Assert.Equal(100, document.GetQuantity(new ResourceId("money")));
        Assert.Null(document.GetQuantity(new ResourceId("craft.t5")));
    }

    [Fact]
    public void RejectsTruncatedNodeTable()
    {
        var bytes = new byte[] { 0x45, 0x44, 0x4F, 0x4E, 0x01 };
        Assert.Throws<InvalidDataException>(() => NodeTable.Parse(bytes));
    }

    private static byte[] BuildPayload(ulong money, uint playerQuantity, uint otherQuantity)
    {
        var buffer = new List<byte>();
        WriteInt32(buffer, 0);
        WriteUInt32(buffer, 2);
        AppendItem(buffer, bagId: 1, nodeIndex: 1, money, playerQuantity);
        AppendItem(buffer, bagId: 2, nodeIndex: 2, money, otherQuantity);
        return buffer.ToArray();
    }

    private static void AppendItem(List<byte> buffer, ulong bagId, int nodeIndex, ulong tweakId, uint quantity)
    {
        WriteUInt64(buffer, bagId);
        WriteUInt32(buffer, 1);
        var header = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(header, tweakId);
        buffer.AddRange(header);
        WriteInt32(buffer, nodeIndex);
        WriteUInt64(buffer, tweakId);
        buffer.AddRange(new byte[13]);
        WriteUInt32(buffer, quantity);
    }

    private static byte[] BuildNodeTable(byte[] payload)
    {
        const int body1 = 36;
        const int body2 = 93;
        var buffer = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(buffer, 3);
        WriteNode(buffer, "inventory", next: -1, child: 1, payloadOffset: 0, length: payload.Length);
        WriteNode(buffer, "itemData", next: 2, child: -1, payloadOffset: body1, length: 29);
        WriteNode(buffer, "itemData", next: -1, child: -1, payloadOffset: body2, length: 29);
        return buffer.ToArray();
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
