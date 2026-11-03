using GameTool.Core;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "背包里的“物品堆叠”是游戏术语，不是 Stack<T> 集合。")]
public sealed record ItemStack(ulong TweakDbId, long Quantity, int QuantityOffset);

public sealed class InventorySnapshot
{
    public InventorySnapshot(int playerItemCount, IReadOnlyList<ItemStack> playerStacks, int stashItemCount = 0)
    {
        PlayerItemCount = playerItemCount;
        PlayerStacks = playerStacks;
        StashItemCount = stashItemCount;
    }

    public int PlayerItemCount { get; }
    public IReadOnlyList<ItemStack> PlayerStacks { get; }
    public int StashItemCount { get; }
}

public static class InventoryReader
{
    private const ulong PlayerBagId = 1;
    public const ulong StashBagId = 1_000_000;
    private const int StackItemLength = 29;
    private const int QuantityOffset = 25;
    private const int MaxSubInventories = 10_000;
    private const int MaxItemsPerBag = 100_000;

    public static InventorySnapshot Read(ReadOnlySpan<byte> payload, NodeTable nodes)
    {
        var inventory = nodes.RequireSingle("inventory");
        var end = checked(inventory.PayloadOffset + inventory.Length);
        if (end > payload.Length)
            throw new InvalidDataException(UiText.T("inventory 节点超出解压数据。", "inventory 節點超出解壓資料。", "The inventory node lies outside the decompressed data."));

        var cursor = inventory.PayloadOffset;
        var self = ReadInt32(payload, ref cursor);
        if (self != inventory.Index)
            throw new InvalidDataException(UiText.T($"inventory 自身节点号 {self} 与索引 {inventory.Index} 不一致。", $"inventory 自身節點號 {self} 與索引 {inventory.Index} 不一致。", $"The inventory node stores index {self}, which does not match its position {inventory.Index}."));

        var subCount = ReadUInt32(payload, ref cursor);
        if (subCount > MaxSubInventories)
            throw new InvalidDataException(UiText.T($"子背包数 {subCount} 超出安全范围 0..{MaxSubInventories}。", $"子背包數 {subCount} 超出安全範圍 0..{MaxSubInventories}。", $"Sub-inventory count {subCount} is outside the safe range 0..{MaxSubInventories}."));

        int? playerItemCount = null;
        int stashItemCount = 0;
        var stacks = new List<ItemStack>();
        var child = inventory.Child;
        for (var sub = 0; sub < subCount; sub++)
        {
            var bagId = ReadUInt64(payload, ref cursor);
            var itemCount = ReadUInt32(payload, ref cursor);
            if (itemCount > MaxItemsPerBag)
                throw new InvalidDataException(UiText.T($"子背包 {bagId} 的物品数 {itemCount} 超出安全范围 0..{MaxItemsPerBag}。", $"子背包 {bagId} 的物品數 {itemCount} 超出安全範圍 0..{MaxItemsPerBag}。", $"Sub-inventory {bagId} has {itemCount} items, outside the safe range 0..{MaxItemsPerBag}."));
            if (bagId == PlayerBagId)
            {
                if (playerItemCount is not null)
                    throw new InvalidDataException(UiText.T("玩家背包 ID=1 出现了多次。", "玩家背包 ID=1 出現了多次。", "The player backpack (ID=1) appears more than once."));
                playerItemCount = checked((int)itemCount);
            }
            else if (bagId == StashBagId)
            {
                stashItemCount = checked((int)itemCount);
            }

            for (var item = 0; item < itemCount; item++)
            {
                if (child < 0 || child >= nodes.Count)
                    throw new InvalidDataException(UiText.T($"itemData 链在第 {item} 项断开（索引 {child}）。", $"itemData 鏈在第 {item} 項斷開（索引 {child}）。", $"The itemData chain breaks at entry {item} (index {child})."));
                var node = nodes[child];
                if (node.Name != "itemData")
                    throw new InvalidDataException(UiText.T($"子节点 {node.Index} 的名字是 {node.Name}，不是 itemData。", $"子節點 {node.Index} 的名字是 {node.Name}，不是 itemData。", $"Child node {node.Index} is named {node.Name}, not itemData."));
                if (node.Length < 12)
                    throw new InvalidDataException(UiText.T($"itemData {node.Index} 长度 {node.Length} 不足以容纳节点号和 TweakDBID。", $"itemData {node.Index} 長度 {node.Length} 不足以容納節點號和 TweakDBID。", $"itemData {node.Index} has length {node.Length}, too short for a node index and a TweakDBID."));
                if (node.PayloadOffset != cursor + 16)
                {
                    throw new InvalidDataException(
                        UiText.T($"itemData {node.Index} 的解压偏移 {node.PayloadOffset} 不等于游标 {cursor} + 16。", $"itemData {node.Index} 的解壓偏移 {node.PayloadOffset} 不等於遊標 {cursor} + 16。", $"itemData {node.Index} starts at {node.PayloadOffset} instead of {cursor} + 16."));
                }

                if (cursor + 16 + node.Length > payload.Length)
                    throw new InvalidDataException(UiText.T($"itemData {node.Index} 超出解压数据。", $"itemData {node.Index} 超出解壓資料。", $"itemData {node.Index} lies outside the decompressed data."));

                var headerId = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(cursor, 8));
                var body = payload.Slice(node.PayloadOffset, node.Length);
                var bodyIndex = BinaryPrimitives.ReadInt32LittleEndian(body);
                if (bodyIndex != node.Index)
                    throw new InvalidDataException(UiText.T($"itemData {node.Index} 数据里的节点号是 {bodyIndex}。", $"itemData {node.Index} 資料裡的節點號是 {bodyIndex}。", $"itemData {node.Index} stores node index {bodyIndex}."));
                var bodyId = BinaryPrimitives.ReadUInt64LittleEndian(body.Slice(4, 8));
                if (bodyId != headerId)
                    throw new InvalidDataException(UiText.T($"itemData {node.Index} 的 TweakDBID 与 16 字节头不一致。", $"itemData {node.Index} 的 TweakDBID 與 16 位元組頭不一致。", $"The TweakDBID of itemData {node.Index} does not match its 16-byte header."));

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
            throw new InvalidDataException(UiText.T("没有玩家背包 ID=1。", "沒有玩家背包 ID=1。", "There is no player backpack (ID=1)."));
        if (cursor != end)
        {
            throw new InvalidDataException(
                UiText.T($"inventory 游标 {cursor} 与节点末尾 {end} 相差 {cursor - end}。", $"inventory 遊標 {cursor} 與節點末尾 {end} 相差 {cursor - end}。", $"The inventory cursor {cursor} differs from the node end {end} by {cursor - end}."));
        }

        return new InventorySnapshot(playerItemCount.Value, stacks, stashItemCount);
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 4 > payload.Length)
            throw new InvalidDataException(UiText.T("inventory 在读 i32 时被截断。", "inventory 在讀 i32 時被截斷。", "The inventory is truncated while reading an i32."));
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(cursor, 4));
        cursor += 4;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 4 > payload.Length)
            throw new InvalidDataException(UiText.T("inventory 在读 u32 时被截断。", "inventory 在讀 u32 時被截斷。", "The inventory is truncated while reading a u32."));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, 4));
        cursor += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> payload, ref int cursor)
    {
        if (cursor + 8 > payload.Length)
            throw new InvalidDataException(UiText.T("inventory 在读 u64 时被截断。", "inventory 在讀 u64 時被截斷。", "The inventory is truncated while reading a u64."));
        var value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(cursor, 8));
        cursor += 8;
        return value;
    }
}
