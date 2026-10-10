using System.Buffers.Binary;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public sealed record ItemStack(ulong TweakDbId, long Quantity, int QuantityOffset);

public sealed class InventorySnapshot
{
    public InventorySnapshot(int playerItemCount, IReadOnlyList<ItemStack> playerStacks)
    {
        PlayerItemCount = playerItemCount;
        PlayerStacks = playerStacks;
    }

    public int PlayerItemCount { get; }
    public IReadOnlyList<ItemStack> PlayerStacks { get; }
}

public static class InventoryReader
{
    private const ulong PlayerBagId = 1;
    private const int StackItemLength = 29;
    private const int QuantityOffset = 25;
    private const int MaxSubInventories = 10_000;
    private const int MaxItemsPerBag = 100_000;

    public static InventorySnapshot Read(ReadOnlySpan<byte> payload, NodeTable nodes)
    {
        var inventory = nodes.RequireSingle("inventory");
        var end = checked(inventory.PayloadOffset + inventory.Length);
        if (end > payload.Length)
            throw new InvalidDataException("inventory 节点超出解压数据。");

        var cursor = inventory.PayloadOffset;
        var self = ReadInt32(payload, ref cursor);
        if (self != inventory.Index)
            throw new InvalidDataException($"inventory 自身节点号 {self} 与索引 {inventory.Index} 不一致。");

        var subCount = ReadUInt32(payload, ref cursor);
        if (subCount > MaxSubInventories)
            throw new InvalidDataException($"子背包数 {subCount} 超出安全范围 0..{MaxSubInventories}。");

        int? playerItemCount = null;
        var stacks = new List<ItemStack>();
        var child = inventory.Child;
        for (var sub = 0; sub < subCount; sub++)
        {
            var bagId = ReadUInt64(payload, ref cursor);
            var itemCount = ReadUInt32(payload, ref cursor);
            if (itemCount > MaxItemsPerBag)
                throw new InvalidDataException($"子背包 {bagId} 的物品数 {itemCount} 超出安全范围 0..{MaxItemsPerBag}。");
            if (bagId == PlayerBagId)
            {
                if (playerItemCount is not null)
                    throw new InvalidDataException("玩家背包 ID=1 出现了多次。");
                playerItemCount = checked((int)itemCount);
            }

            for (var item = 0; item < itemCount; item++)
            {
                if (child < 0 || child >= nodes.Count)
                    throw new InvalidDataException($"itemData 链在第 {item} 项断开（索引 {child}）。");
                var node = nodes[child];
                if (node.Name != "itemData")
                    throw new InvalidDataException($"子节点 {node.Index} 的名字是 {node.Name}，不是 itemData。");
                if (node.Length < 12)
                    throw new InvalidDataException($"itemData {node.Index} 长度 {node.Length} 不足以容纳节点号和 TweakDBID。");
                if (node.PayloadOffset != cursor + 16)
                {
                    throw new InvalidDataException(
                        $"itemData {node.Index} 的解压偏移 {node.PayloadOffset} 不等于游标 {cursor} + 16。");
                }

                if (cursor + 16 + node.Length > payload.Length)
                    throw new InvalidDataException($"itemData {node.Index} 超出解压数据。");

                var headerId = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(cursor, 8));
                var body = payload.Slice(node.PayloadOffset, node.Length);
                var bodyIndex = BinaryPrimitives.ReadInt32LittleEndian(body);
                if (bodyIndex != node.Index)
                    throw new InvalidDataException($"itemData {node.Index} 数据里的节点号是 {bodyIndex}。");
                var bodyId = BinaryPrimitives.ReadUInt64LittleEndian(body.Slice(4, 8));
                if (bodyId != headerId)
                    throw new InvalidDataException($"itemData {node.Index} 的 TweakDBID 与 16 字节头不一致。");

                if (bagId == PlayerBagId && node.Length == StackItemLength)
                {
                    var quantity = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(QuantityOffset, 4));
                    stacks.Add(new ItemStack(bodyId, quantity, node.PayloadOffset + QuantityOffset));
                }

                cursor += 16 + node.Length;
                child = node.Next;
            }
        }

        if (playerItemCount is null)
            throw new InvalidDataException("没有玩家背包 ID=1。");
        if (cursor != end)
        {
            throw new InvalidDataException(
                $"inventory 游标 {cursor} 与节点末尾 {end} 相差 {cursor - end}。");
        }

        return new InventorySnapshot(playerItemCount.Value, stacks);
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 4 > payload.Length)
            throw new InvalidDataException("inventory 在读 i32 时被截断。");
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(cursor, 4));
        cursor += 4;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 4 > payload.Length)
            throw new InvalidDataException("inventory 在读 u32 时被截断。");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, 4));
        cursor += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 8 > payload.Length)
            throw new InvalidDataException("inventory 在读 u64 时被截断。");
        var value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(cursor, 8));
        cursor += 8;
        return value;
    }
}
