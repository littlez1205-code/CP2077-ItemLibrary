using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077.Container;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public sealed record ResearchInventoryItem(
    int Ordinal,
    int NodeIndex,
    string HeaderHex,
    string TweakDbId,
    string? Name,
    int Length,
    bool Stack,
    uint? Quantity);

public sealed record ResearchBagSummary(ulong Id, int Items);

public sealed record ResearchInventory(ulong BagId, IReadOnlyList<ResearchInventoryItem> Items);

public sealed record InventoryItemDelta(
    string Kind,
    int? LeftOrdinal,
    int? RightOrdinal,
    int? LeftNode,
    int? RightNode,
    string HeaderHex,
    string TweakDbId,
    string? Name,
    int? LeftLength,
    int? RightLength);

public sealed record InventoryCompare(ulong BagId, IReadOnlyList<InventoryItemDelta> Changes);

public static class InventoryResearch
{
    public const ulong DefaultBagId = 1;
    private const int StackLength = 29;
    private const int QuantityOffset = 25;
    private const int MaxSubInventories = 10_000;
    private const int MaxItemsPerBag = 100_000;

    public static ResearchInventory Read(Cp2077Adapter adapter, string saveFilePath, ulong bagId)
    {
        var bag = ReadBags(saveFilePath, Names(adapter)).FirstOrDefault(item => item.Id == bagId);
        if (bag is null)
            throw new InvalidDataException($"没有子背包 {bagId}。");
        return new ResearchInventory(bagId, bag.Items.Select(Publish).ToArray());
    }

    public static IReadOnlyList<ResearchBagSummary> Bags(string saveFilePath) =>
        ReadBags(saveFilePath, new Dictionary<ulong, string>()).Select(bag => new ResearchBagSummary(bag.Id, bag.Items.Count)).ToArray();

    public static InventoryCompare Compare(Cp2077Adapter adapter, string leftPath, string rightPath, ulong bagId)
    {
        var names = Names(adapter);
        var left = RequireBag(ReadBags(leftPath, names), bagId);
        var right = RequireBag(ReadBags(rightPath, names), bagId);
        var used = new bool[right.Items.Count];
        var changes = new List<InventoryItemDelta>();
        foreach (var item in left.Items)
        {
            var match = -1;
            for (var i = 0; i < right.Items.Count; i++)
            {
                if (used[i] || right.Items[i].HeaderHex != item.HeaderHex)
                    continue;
                match = i;
                break;
            }

            if (match < 0)
            {
                changes.Add(Delta("removed", item, null));
                continue;
            }

            used[match] = true;
            var other = right.Items[match];
            if (ContentDiffers(item, other))
                changes.Add(Delta("changed", item, other));
        }

        for (var i = 0; i < right.Items.Count; i++)
        {
            if (!used[i])
                changes.Add(Delta("added", null, right.Items[i]));
        }

        return new InventoryCompare(bagId, changes);
    }

    private static BagView RequireBag(IReadOnlyList<BagView> bags, ulong bagId) =>
        bags.FirstOrDefault(bag => bag.Id == bagId) ?? throw new InvalidDataException($"没有子背包 {bagId}。");

    private static bool ContentDiffers(ItemView left, ItemView right)
    {
        if (left.Body.Length != right.Body.Length)
            return true;
        var start = Math.Min(4, left.Body.Length);
        return !left.Body.AsSpan(start).SequenceEqual(right.Body.AsSpan(start));
    }

    private static InventoryItemDelta Delta(string kind, ItemView? left, ItemView? right)
    {
        var shown = right ?? left ?? throw new InvalidOperationException("对比项为空。");
        return new InventoryItemDelta(
            kind,
            left?.Ordinal,
            right?.Ordinal,
            left?.NodeIndex,
            right?.NodeIndex,
            shown.HeaderHex,
            shown.TweakHex,
            shown.Name,
            left?.Length,
            right?.Length);
    }

    private static ResearchInventoryItem Publish(ItemView item) =>
        new(item.Ordinal, item.NodeIndex, item.HeaderHex, item.TweakHex, item.Name, item.Length, item.Stack, item.Quantity);

    private static Dictionary<ulong, string> Names(Cp2077Adapter adapter)
    {
        var map = new Dictionary<ulong, string>();
        foreach (var resource in adapter.Resources)
        {
            const string prefix = "tdb:";
            if (!resource.Locator.StartsWith(prefix, StringComparison.Ordinal) || resource.Locator.Length == prefix.Length)
                continue;
            var name = resource.Locator[prefix.Length..];
            map[TweakDbId.FromName(name)] = name;
        }

        map[TweakDbId.FromName("Items.money")] = "Items.money";
        return map;
    }

    private static List<BagView> ReadBags(string saveFilePath, IReadOnlyDictionary<ulong, string> names)
    {
        var container = SaveContainer.Read(saveFilePath);
        var payload = container.Payload;
        var nodes = NodeTable.Parse(container.CopyNodeTable());
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

        var bags = new List<BagView>();
        var seen = new HashSet<ulong>();
        var child = inventory.Child;
        for (var sub = 0; sub < subCount; sub++)
        {
            var bagId = ReadUInt64(payload, ref cursor);
            var itemCount = ReadUInt32(payload, ref cursor);
            if (itemCount > MaxItemsPerBag)
                throw new InvalidDataException($"子背包 {bagId} 的物品数 {itemCount} 超出安全范围 0..{MaxItemsPerBag}。");
            if (!seen.Add(bagId))
                throw new InvalidDataException($"子背包 {bagId} 出现了多次。");

            var items = new List<ItemView>();
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
                    throw new InvalidDataException($"itemData {node.Index} 的解压偏移 {node.PayloadOffset} 不等于游标 {cursor} + 16。");
                if (cursor + 16 + node.Length > payload.Length)
                    throw new InvalidDataException($"itemData {node.Index} 超出解压数据。");

                var header = payload.Slice(cursor, 16).ToArray();
                var body = payload.Slice(node.PayloadOffset, node.Length).ToArray();
                var tweak = BinaryPrimitives.ReadUInt64LittleEndian(header);
                uint? quantity = null;
                var stack = node.Length == StackLength;
                if (stack)
                    quantity = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(QuantityOffset, 4));
                names.TryGetValue(tweak, out var name);
                items.Add(new ItemView(
                    item + 1,
                    node.Index,
                    Convert.ToHexString(header).ToLowerInvariant(),
                    Convert.ToHexString(BitConverter.GetBytes(tweak)).ToLowerInvariant(),
                    name,
                    node.Length,
                    stack,
                    quantity,
                    body));
                cursor += 16 + node.Length;
                child = node.Next;
            }

            bags.Add(new BagView(bagId, items));
        }

        if (cursor != end)
            throw new InvalidDataException($"inventory 游标 {cursor} 与节点末尾 {end} 相差 {cursor - end}。");
        return bags;
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

    private sealed record ItemView(
        int Ordinal,
        int NodeIndex,
        string HeaderHex,
        string TweakHex,
        string? Name,
        int Length,
        bool Stack,
        uint? Quantity,
        byte[] Body);

    private sealed record BagView(ulong Id, List<ItemView> Items);
}
