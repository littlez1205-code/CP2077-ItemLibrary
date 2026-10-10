using System.Buffers.Binary;
using System.Security.Cryptography;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public sealed record GearSlot(ulong BagId, int Ordinal, byte[] Header, ulong Tweak, int Length);

public sealed record ItemRemoval(int NodeIndex, int RemovedBytes, int PreviousPlayerCount, string TweakName);

public sealed record ItemInsertion(
    int NodeIndex,
    int AddedBytes,
    int PreviousPlayerCount,
    string HeaderHex,
    string? TweakName,
    uint? Quantity,
    uint? Seed);

public static class ItemInserter
{
    internal const int HeaderLength = 16;
    internal const int StackLength = 29;
    private const int QuantityOffset = 25;
    private const int SeedOffset = 8;
    private const int BodySeedOffset = 12;
    public const ulong StashBagId = 1_000_000;

    public static ItemInsertion InsertStack(SaveContainer container, string tweakName, uint quantity)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(tweakName);
        var tweak = TweakDbId.FromName(tweakName);
        var layout = Layout.Read(container);
        if (layout.PlayerItems.Count == 0 || layout.PlayerItems.All(item => item.Length != StackLength))
            throw new InvalidDataException("玩家背包里没有 29 字节堆叠物品可以当模板。");
        if (layout.PlayerItems.Any(item => item.Tweak == tweak))
            throw new InvalidDataException($"玩家背包里已经有 {tweakName}，请改用 patch。");

        var template = layout.PlayerItems.First(item => item.Length == StackLength);
        var header = template.Header.ToArray();
        var body = template.Body.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(header, tweak);
        BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(4), tweak);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(QuantityOffset), quantity);
        var seed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(SeedOffset));
        return Apply(container, layout, template, header, body, tweakName, quantity, seed);
    }

    public static ItemInsertion InsertClone(SaveContainer container, int ordinal, uint? seed)
    {
        ArgumentNullException.ThrowIfNull(container);
        var layout = Layout.Read(container);
        var source = layout.PlayerItems.FirstOrDefault(item => item.Ordinal == ordinal);
        if (source is null)
            throw new InvalidDataException($"玩家背包没有序号 {ordinal}。");
        if (source.Length < BodySeedOffset + 4)
            throw new InvalidDataException($"序号 {ordinal} 的物品太短，放不下种子。");

        var header = source.Header.ToArray();
        var body = source.Body.ToArray();
        var chosen = seed ?? NewSeed(container.Payload);
        RejectSeed(container.Payload, chosen);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(SeedOffset), chosen);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(BodySeedOffset), chosen);
        return Apply(container, layout, source, header, body, tweakName: null, quantity: null, chosen);
    }

    public static IReadOnlyList<GearSlot> ListNonStacks(SaveContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var layout = Layout.Read(container);
        return Map(layout.PlayerItems, InventoryResearch.DefaultBagId)
            .Concat(Map(layout.StashItems, StashBagId))
            .ToArray();
    }

    private static IEnumerable<GearSlot> Map(List<PlacedItem> items, ulong bag) =>
        items.Where(item => item.Length != StackLength)
            .Select(item => new GearSlot(bag, item.Ordinal, item.Header, item.Tweak, item.Length));

    public static CloneApplyResult CloneEquipment(SaveContainer container, string key, int copies)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (copies is < 1 or > 10)
            throw new InvalidDataException("复制件数必须在 1 到 10 之间。");
        var layout = Layout.Read(container);
        if (layout.PlayerItems.Count == 0)
            throw new InvalidDataException("玩家背包是空的。");
        var source = Find(layout, key);
        if (source.Length == StackLength)
            throw new InvalidDataException("堆叠物品不能按装备复制。");
        if (source.Length < BodySeedOffset + 4)
            throw new InvalidDataException("这件物品太短，放不下种子。");
        if (source.Child >= 0)
            throw new InvalidDataException("这件物品带子节点，暂不支持复制。");

        var sourceHeader = source.Header.ToArray();
        var sourceBody = source.Body.ToArray();
        var previous = layout.PlayerItems.Count;
        var seeds = new uint[copies];
        ItemInsertion? first = null;
        for (var i = 0; i < copies; i++)
        {
            var current = i == 0 ? layout : Layout.Read(container);
            var again = Find(current, key);
            if (!again.Header.AsSpan().SequenceEqual(sourceHeader))
                throw new InvalidDataException("物品头已经变化，存档可能被改过。");
            var header = sourceHeader.ToArray();
            var body = sourceBody.ToArray();
            var chosen = NewSeed(container.Payload);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(SeedOffset), chosen);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(BodySeedOffset), chosen);
            var insertion = Apply(container, current, again, header, body, tweakName: null, quantity: null, chosen);
            seeds[i] = chosen;
            first ??= insertion;
        }

        return new CloneApplyResult(
            key,
            copies,
            (HeaderLength + sourceBody.Length) * copies,
            first!.NodeIndex,
            previous,
            sourceBody.Length,
            sourceHeader,
            sourceBody,
            seeds);
    }

    public static ItemRemoval RemoveStack(SaveContainer container, string tweakName)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(tweakName);
        var tweak = TweakDbId.FromName(tweakName);
        var layout = Layout.Read(container);
        var matches = layout.PlayerItems.Where(item => item.Tweak == tweak && item.Length == StackLength).ToArray();
        if (matches.Length == 0)
            throw new InvalidDataException($"玩家背包里没有 29 字节的 {tweakName}。");
        if (matches.Length != 1)
            throw new InvalidDataException($"玩家背包里的 {tweakName} 不止一个 29 字节堆叠。");

        var target = matches[0];
        var removed = HeaderLength + target.Length;
        var removeStart = target.BodyOffset - HeaderLength;
        var removeEnd = removeStart + removed;
        var old = container.Payload.ToArray();
        var payload = new byte[old.Length - removed];
        old.AsSpan(0, removeStart).CopyTo(payload);
        old.AsSpan(removeEnd).CopyTo(payload.AsSpan(removeStart));

        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(layout.PlayerCountOffset, 4));
        if (count == 0)
            throw new InvalidDataException("玩家背包物品数已经是 0。");
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(layout.PlayerCountOffset, 4), count - 1);

        var nodes = NodeTable.Parse(container.CopyNodeTable());
        var k = target.NodeIndex;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Index <= k)
                continue;
            var shifted = node.PayloadOffset >= removeEnd ? node.PayloadOffset - removed : node.PayloadOffset;
            if (node.Length < 4 || shifted < 0 || shifted + 4 > payload.Length)
                throw new InvalidDataException($"节点 {node.Index} 太短，放不下自身编号。");
            var self = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(shifted, 4));
            if (self != node.Index)
                throw new InvalidDataException($"节点 {node.Index} 的自身编号是 {self}，无法前移。");
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(shifted, 4), node.Index - 1);
        }

        container.ReplacePayload(payload);
        container.ReplaceNodeTable(RewriteRemoval(nodes, layout, k, removeStart, removeEnd, removed));
        return new ItemRemoval(k, removed, layout.PlayerItems.Count, tweakName);
    }

    internal static ItemInsertion Apply(
        SaveContainer container,
        Layout layout,
        PlacedItem template,
        byte[] header,
        byte[] body,
        string? tweakName,
        uint? quantity,
        uint seed)
    {
        var last = layout.PlayerItems[^1];
        var k = last.NodeIndex + 1;
        var added = HeaderLength + body.Length;
        var insertAt = last.BodyOffset + last.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body, k);

        var old = container.Payload.ToArray();
        var payload = new byte[old.Length + added];
        old.AsSpan(0, insertAt).CopyTo(payload);
        header.CopyTo(payload.AsSpan(insertAt));
        body.CopyTo(payload.AsSpan(insertAt + HeaderLength));
        old.AsSpan(insertAt).CopyTo(payload.AsSpan(insertAt + added));

        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(layout.PlayerCountOffset, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(layout.PlayerCountOffset, 4), count + 1);

        var nodes = NodeTable.Parse(container.CopyNodeTable());
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Index < k)
                continue;
            var shifted = node.PayloadOffset >= insertAt ? node.PayloadOffset + added : node.PayloadOffset;
            if (node.Length < 4)
                throw new InvalidDataException($"节点 {node.Index} 太短，放不下自身编号。");
            var self = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(shifted, 4));
            if (self != node.Index)
                throw new InvalidDataException($"节点 {node.Index} 的自身编号是 {self}，无法后移。");
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(shifted, 4), node.Index + 1);
        }

        container.ReplacePayload(payload);
        container.ReplaceNodeTable(Rewrite(nodes, layout, last, template, k, insertAt, added, body.Length));
        return new ItemInsertion(
            k,
            added,
            layout.PlayerItems.Count,
            Convert.ToHexString(header).ToLowerInvariant(),
            tweakName,
            quantity,
            seed);
    }

    private static byte[] Rewrite(
        NodeTable nodes,
        Layout layout,
        PlacedItem last,
        PlacedItem template,
        int k,
        int insertAt,
        int added,
        int bodyLength)
    {
        var grown = new HashSet<int> { layout.InventoryIndex };
        var parents = Parents(nodes);
        for (var cursor = parents[layout.InventoryIndex]; cursor is int parent; cursor = parents[parent])
            grown.Add(parent);

        var rewritten = new SaveNode[nodes.Count + 1];
        var inherited = Bump(last.Next, k);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var dest = node.Index >= k ? node.Index + 1 : node.Index;
            var next = node.Index == last.NodeIndex ? k : Bump(node.Next, k);
            var fileOffset = node.PayloadOffset >= insertAt ? node.FileOffset + added : node.FileOffset;
            var length = node.Length;
            if (grown.Contains(node.Index)
                && node.PayloadOffset <= insertAt
                && node.PayloadOffset + node.Length >= insertAt)
                length += added;
            rewritten[dest] = new SaveNode(dest, node.Name, next, Bump(node.Child, k), fileOffset, length);
        }

        rewritten[k] = new SaveNode(
            k,
            "itemData",
            inherited,
            Bump(template.Child, k),
            Cp2077Format.FirstChunkOffset + insertAt + HeaderLength,
            bodyLength);
        return NodeTable.Write(rewritten);
    }

    private static byte[] RewriteRemoval(NodeTable nodes, Layout layout, int k, int removeStart, int removeEnd, int removed)
    {
        var shrunk = new HashSet<int> { layout.InventoryIndex };
        var parents = Parents(nodes);
        for (var cursor = parents[layout.InventoryIndex]; cursor is int parent; cursor = parents[parent])
            shrunk.Add(parent);

        var deleted = nodes[k];
        var successor = Drop(deleted.Next, k);
        var rewritten = new SaveNode[nodes.Count - 1];
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i == k)
                continue;
            var node = nodes[i];
            var dest = node.Index > k ? node.Index - 1 : node.Index;
            var next = node.Next == k ? successor : Drop(node.Next, k);
            var child = node.Child == k ? successor : Drop(node.Child, k);
            var fileOffset = node.PayloadOffset >= removeEnd ? node.FileOffset - removed : node.FileOffset;
            var length = node.Length;
            if (shrunk.Contains(node.Index)
                && node.PayloadOffset <= removeStart
                && node.PayloadOffset + node.Length >= removeEnd)
                length -= removed;
            rewritten[dest] = new SaveNode(dest, node.Name, next, child, fileOffset, length);
        }

        return NodeTable.Write(rewritten);
    }

    private static int Drop(int value, int k) => value > k ? value - 1 : value;

    private static int?[] Parents(NodeTable nodes)
    {
        var parents = new int?[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var child = nodes[i].Child;
            var guard = 0;
            while (child >= 0 && child < nodes.Count && guard++ <= nodes.Count)
            {
                parents[child] ??= i;
                child = nodes[child].Next;
            }
        }

        return parents;
    }

    private static int Bump(int value, int k) => value >= k ? value + 1 : value;

    internal static uint NewSeed(ReadOnlySpan<byte> payload, IReadOnlySet<uint>? taken = null)
    {
        Span<byte> bytes = stackalloc byte[4];
        for (var attempt = 0; attempt < 64; attempt++)
        {
            RandomNumberGenerator.Fill(bytes);
            var seed = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (seed is not 0 and not 1 and not 2 && taken?.Contains(seed) != true && !Contains(payload, seed))
                return seed;
        }

        throw new InvalidDataException("找不到未使用的种子。");
    }

    internal static bool Contains(ReadOnlySpan<byte> payload, uint seed)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, seed);
        return payload.IndexOf(bytes) >= 0;
    }

    private static void RejectSeed(ReadOnlySpan<byte> payload, uint seed)
    {
        if (seed is 0 or 1 or 2)
            throw new InvalidDataException("种子不能是 0、1 或 2。");
        if (Contains(payload, seed))
            throw new InvalidDataException("指定的种子在存档里已经出现。");
    }

    internal static PlacedItem Find(Layout layout, string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 3
            || !ulong.TryParse(parts[0], out var bag)
            || !int.TryParse(parts[1], out var ordinal))
            throw new InvalidDataException("物品键格式不对。");
        if (bag != InventoryResearch.DefaultBagId && bag != StashBagId)
            throw new InvalidDataException("只能从玩家背包或储物箱复制。");
        var pool = bag == InventoryResearch.DefaultBagId ? layout.PlayerItems : layout.StashItems;
        var item = pool.FirstOrDefault(candidate => candidate.Ordinal == ordinal);
        if (item is null)
            throw new InvalidDataException("找不到这件物品。");
        var hex = Convert.ToHexString(item.Header).ToLowerInvariant();
        if (!hex.Equals(parts[2], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("物品头已经变化，存档可能被改过。");
        return item;
    }

    internal sealed class Layout
    {
        public int InventoryIndex { get; init; }
        public int PlayerCountOffset { get; init; }
        public List<PlacedItem> PlayerItems { get; init; } = [];
        public List<PlacedItem> StashItems { get; init; } = [];

        public static Layout Read(SaveContainer container)
        {
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
            var items = new List<PlacedItem>();
            var stash = new List<PlacedItem>();
            int? countOffset = null;
            var child = inventory.Child;
            for (var sub = 0; sub < subCount; sub++)
            {
                var bagId = ReadUInt64(payload, ref cursor);
                var atCount = cursor;
                var itemCount = ReadUInt32(payload, ref cursor);
                var player = bagId == InventoryResearch.DefaultBagId;
                var tracked = player || bagId == StashBagId;
                if (player)
                {
                    if (countOffset is not null)
                        throw new InvalidDataException("玩家背包 ID=1 出现了多次。");
                    countOffset = atCount;
                }

                for (var item = 0; item < itemCount; item++)
                {
                    if (child < 0 || child >= nodes.Count)
                        throw new InvalidDataException($"itemData 链在第 {item} 项断开（索引 {child}）。");
                    var node = nodes[child];
                    if (node.Name != "itemData" || node.PayloadOffset != cursor + HeaderLength)
                        throw new InvalidDataException($"子节点 {node.Index} 不是紧跟物品头的 itemData。");
                    if (cursor + HeaderLength + node.Length > payload.Length)
                        throw new InvalidDataException($"itemData {node.Index} 超出解压数据。");
                    if (tracked)
                    {
                        var header = payload.Slice(cursor, HeaderLength).ToArray();
                        var body = payload.Slice(node.PayloadOffset, node.Length).ToArray();
                        var placed = new PlacedItem(
                            item + 1,
                            node.Index,
                            node.Next,
                            node.Child,
                            node.PayloadOffset,
                            node.Length,
                            BinaryPrimitives.ReadUInt64LittleEndian(header),
                            header,
                            body);
                        if (player)
                            items.Add(placed);
                        else
                            stash.Add(placed);
                    }

                    cursor += HeaderLength + node.Length;
                    child = node.Next;
                }
            }

            if (countOffset is null)
                throw new InvalidDataException("没有玩家背包 ID=1。");
            if (cursor != end)
                throw new InvalidDataException($"inventory 游标 {cursor} 与节点末尾 {end} 相差 {cursor - end}。");
            return new Layout
            {
                InventoryIndex = inventory.Index,
                PlayerCountOffset = countOffset.Value,
                PlayerItems = items,
                StashItems = stash
            };
        }

        private static int ReadInt32(ReadOnlySpan<byte> payload, ref int cursor)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(cursor, 4));
            cursor += 4;
            return value;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int cursor)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, 4));
            cursor += 4;
            return value;
        }

        private static ulong ReadUInt64(ReadOnlySpan<byte> payload, ref int cursor)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(cursor, 8));
            cursor += 8;
            return value;
        }
    }

    internal sealed record PlacedItem(int Ordinal, int NodeIndex, int Next, int Child, int BodyOffset, int Length, ulong Tweak, byte[] Header, byte[] Body);
}
