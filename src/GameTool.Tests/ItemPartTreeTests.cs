using System.Buffers.Binary;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Nodes;

namespace GameTool.Tests;

public class ItemPartTreeTests
{
    [Fact]
    public void RoundTrips_91_141_291()
    {
        var empty = Body("Items.MaskCWPlusPlus", 0x1111, Part(0, 0, 0, "None", 0));
        var generic = Body("Items.SQ031_Samurai_Jacket", 0x2222, Part(TweakDbId.FromName("Items.GenericItemRoot"), 2, 0, "None", TweakDbId.FromName("AttachmentSlots.GenericItemRoot")));
        var cyberware = Body("Items.StrongHolsteredFists", 0x3333,
            Part(TweakDbId.FromName("Items.StrongHolsteredFistsRoot"), 0x44, 0x10, "None", TweakDbId.FromName("AttachmentSlots.GenericItemRoot"),
                Part(TweakDbId.FromName("Items.AdvancedStrongArmsStatsShard"), 0x55, 0x11, "None", TweakDbId.FromName("AttachmentSlots.StatsShardSlot"))));
        var weapon = Body("Items.Preset_MA70_Default", 0x6666,
            Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__rcv1"), 0x70, 0x6FEE, "None", TweakDbId.FromName("AttachmentSlots.Receiver"),
                Part(TweakDbId.FromName("Items.DummyBlankMod5"), 0x71, 0x6FEF, "None", 1),
                Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_std"), 0x72, 0x6FF0, "None", 2),
                Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_stdr"), 0x73, 0x6FF1, "None", 3),
                Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__base1"), 0x74, 0x6FF2, "None", 4, trailerFloat: 1.5f)));

        Assert.Equal(91, empty.Length);
        Assert.Equal(91, generic.Length);
        Assert.Equal(141, cyberware.Length);
        Assert.Equal(291, weapon.Length);
        foreach (var bytes in new[] { empty, generic, cyberware, weapon })
        {
            var parsed = ItemPartTree.Parse(bytes);
            Assert.Equal(bytes, ItemPartTree.Write(parsed));
            Assert.Equal(bytes.AsSpan(4, 16).ToArray(), parsed.RootId);
        }

        var tree = ItemPartTree.Parse(weapon);
        Assert.Equal(5, tree.Walk().Count());
        Assert.Equal(4, tree.Root.Children.Count);
        Assert.Equal(TweakDbId.FromName("Items.w_lmg__midnight_ma70__rcv1"), tree.Root.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.Receiver"), tree.Root.Slot);
        Assert.Equal(0x74u, tree.Root.Children[3].Seed);
        Assert.Equal("None", tree.Root.Children[0].Appearance);
        Assert.Equal(1, tree.Walk().Last().Depth);
        Assert.Equal(7, tree.NodeIndex);
    }

    [Fact]
    public void Rejects_Truncated_Trailing_AndNonCanonical()
    {
        var bytes = Body("Items.Preset_MA70_Default", 0x6666,
            Part(1, 2, 3, "None", 4, Part(5, 6, 7, "None", 8)));
        Assert.Equal(141, bytes.Length);
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(bytes.AsSpan(0, 40)));
        Assert.Contains("没有正好读完", Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse([.. bytes, 0])).Message);

        var tooMany = bytes.ToArray();
        tooMany[41 + 16 + 5 + 8] = 2;
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(tooMany));

        var positiveName = bytes.ToArray();
        positiveName[41 + 16] = 0x04;
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(positiveName));

        // 子配件数 0 写成带续位的 0x40 0x00：值相同但不是最短编码，写回会变，必须拒绝。
        var leaf = Body("Items.X", 1, Part(1, 2, 3, "None", 4));
        var padded = leaf[..(41 + 16 + 5 + 8)].Concat(new byte[] { 0x40, 0x00 }).Concat(leaf[(41 + 16 + 5 + 8 + 1)..]).ToArray();
        Assert.Contains("最短编码", Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(padded)).Message);
    }

    internal static byte[] Body(string record, uint seed, byte[] root, int nodeIndex = 7)
    {
        var buffer = new List<byte>();
        var head = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(head, nodeIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(head.AsSpan(4), TweakDbId.FromName(record));
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), seed);
        head[17] = 0xE7;
        head[18] = 0x6F;
        buffer.AddRange(head);
        buffer.Add(0x01);
        buffer.AddRange([0xD1, 0x1E, 0x79, 0x00]);
        buffer.AddRange([0xD3, 0xE9, 0x0E, 0x02, 0x31, 0x00, 0x00, 0x00]);
        buffer.AddRange([0x1F, 0xA7, 0x94, 0x00]);
        buffer.AddRange([0xFF, 0xFF, 0x7F, 0x7F]);
        buffer.AddRange(root);
        return buffer.ToArray();
    }

    internal static byte[] Part(ulong tweak, uint seed, ushort counter, string name, ulong slot, params byte[][] children) =>
        Part(tweak, seed, counter, name, slot, null, children);

    internal static byte[] Part(ulong tweak, uint seed, ushort counter, string name, ulong slot, float? trailerFloat, params byte[][] children)
    {
        var buffer = new List<byte>();
        var id = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(id, tweak);
        BinaryPrimitives.WriteUInt32LittleEndian(id.AsSpan(8), seed);
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(13), counter);
        buffer.AddRange(id);
        buffer.Add((byte)(0x80 | name.Length));
        buffer.AddRange(Encoding.ASCII.GetBytes(name));
        var slotBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(slotBytes, slot);
        buffer.AddRange(slotBytes);
        buffer.Add((byte)children.Length);
        foreach (var child in children)
            buffer.AddRange(child);
        var trailer = new byte[20];
        if (trailerFloat is float value)
            BinaryPrimitives.WriteSingleLittleEndian(trailer.AsSpan(4), value);
        trailer[16] = 0xFF;
        trailer[17] = 0xFF;
        trailer[18] = 0x7F;
        trailer[19] = 0x7F;
        buffer.AddRange(trailer);
        return buffer.ToArray();
    }
}
