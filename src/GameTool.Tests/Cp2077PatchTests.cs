using System.Buffers.Binary;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Core;

namespace GameTool.Tests;

public class Cp2077PatchTests
{
    [Fact]
    public void ApplySaveReload_ReadsNewQuantity()
    {
        var (bytes, resources) = Sample();
        var document = Cp2077SaveDocument.Open(SaveContainer.Read(bytes), resources);
        document.Apply([new ResourceChange(new ResourceId("money"), 80)]);
        var written = new MemoryStream();
        document.Save(written);
        var reloaded = Cp2077SaveDocument.Open(SaveContainer.Read(written.ToArray()), resources);
        Assert.Equal(80, reloaded.GetQuantity(new ResourceId("money")));
    }

    [Fact]
    public void Apply_MarksOnlyTheTouchedChunkDirty()
    {
        var (bytes, resources) = Sample();
        var container = SaveContainer.Read(bytes);
        var document = Cp2077SaveDocument.Open(container, resources);
        document.Apply([new ResourceChange(new ResourceId("money"), 80)]);
        Assert.True(container.IsChunkDirty(0));
        Assert.False(container.IsChunkDirty(1));
    }

    [Fact]
    public void StrictVerifier_RejectsAnExtraByte()
    {
        var (bytes, resources) = Sample();
        var original = Cp2077SaveDocument.Open(SaveContainer.Read(bytes), resources);
        var container = SaveContainer.Read(bytes);
        var working = Cp2077SaveDocument.Open(container, resources);
        var changes = new[] { new ResourceChange(new ResourceId("money"), 80) };
        working.Apply(changes);
        container.WritePayload(0, [0xFF]);
        Assert.Throws<InvalidDataException>(() => working.VerifyOnlyTargetsChanged(original, changes));
    }

    [Fact]
    public void StrictVerifier_RejectsTimestampChange()
    {
        var (bytes, resources) = Sample();
        var original = Cp2077SaveDocument.Open(SaveContainer.Read(bytes), resources);
        var mutated = bytes.ToArray();
        mutated[13] ^= 0x01;
        var working = Cp2077SaveDocument.Open(SaveContainer.Read(mutated), resources);
        var changes = new[] { new ResourceChange(new ResourceId("money"), 80) };
        working.Apply(changes);
        var error = Assert.Throws<InvalidDataException>(() => working.VerifyOnlyTargetsChanged(original, changes));
        Assert.Contains("时间戳或归档版本", error.Message);
    }

    private static (byte[] File, IReadOnlyList<ResourceDescriptor> Resources) Sample()
    {
        var money = TweakDbId.FromName("Items.money");
        var payload = BuildPayload(money);
        var split = payload.Length - 40;
        var chunks = new[] { payload[..split], payload[split..] };
        var file = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            1,
            1,
            chunks,
            BuildNodeTable(payload)).ToArray();
        var resources = new ResourceDescriptor[]
        {
            new(new ResourceId("money"), "欧元", "Eurodollar", "currency", 1000, 10, "tdb:Items.money")
        };
        return (file, resources);
    }

    private static byte[] BuildPayload(ulong money)
    {
        var buffer = new List<byte>();
        WriteInt32(buffer, 0);
        WriteUInt32(buffer, 2);
        AppendItem(buffer, 1, 1, money, 100);
        AppendItem(buffer, 2, 2, money, 999);
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
        var buffer = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(buffer, 3);
        WriteNode(buffer, "inventory", -1, 1, 0, payload.Length);
        WriteNode(buffer, "itemData", 2, -1, 36, 29);
        WriteNode(buffer, "itemData", -1, -1, 93, 29);
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
