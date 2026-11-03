using GameTool.Core;
using System.Buffers.Binary;
using System.Text;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public sealed record ItemPart(byte[] ItemId, string Appearance, ulong Slot, IReadOnlyList<ItemPart> Children, byte[] Trailer)
{
    public ulong Tweak => BinaryPrimitives.ReadUInt64LittleEndian(ItemId);

    public uint Seed => BinaryPrimitives.ReadUInt32LittleEndian(ItemId.AsSpan(8));

    /// <summary>第一个子配件；没有子配件时为 null。</summary>
    public ItemPart? FirstChild => Children.Count > 0 ? Children[0] : null;
}

public sealed record ItemDataBody(byte[] Prefix, ItemPart Root)
{
    public int NodeIndex => BinaryPrimitives.ReadInt32LittleEndian(Prefix);

    public byte[] RootId => Prefix.AsSpan(4, ItemPartTree.ItemIdLength).ToArray();

    /// <summary>结构 3（手雷）的 45 字节前缀在 +25 多一个 u32 数量；其他物品为 null。</summary>
    public uint? Quantity => Prefix.Length == ItemPartTree.QuantityPrefixLength
        ? BinaryPrimitives.ReadUInt32LittleEndian(Prefix.AsSpan(ItemPartTree.QuantityOffset))
        : null;

    public IEnumerable<(ItemPart Part, int Depth)> Walk() => Walk(Root, 0);

    private static IEnumerable<(ItemPart Part, int Depth)> Walk(ItemPart part, int depth)
    {
        yield return (part, depth);
        foreach (var child in part.Children)
        {
            foreach (var nested in Walk(child, depth + 1))
                yield return nested;
        }
    }
}

/// <summary>
/// 非堆叠 itemData = 固定前缀 + 递归配件树（C2b 报告第 1 节）。
/// 前缀一般是 41 字节；ItemID 的结构字节（物品头 +12，即 itemData +16）为 3 的物品（手雷，T022 实测）在 +25 多一个 u32 数量，前缀 45 字节：
/// 0 节点号 | 4..20 ItemID | 20 标志 | 21 u32 时间 | 25 u32 数量 | 29 8 字节 ID | 37 u32 | 41 f32 FLT_MAX。
/// 配件 = ItemID(16) + CName（紧凑长度，负数为 ASCII）+ 挂点 TweakDBID(8) + 紧凑子节点数 + 子配件 + 20 字节尾部。
/// 只接受最短编码，保证写回逐字节一致。
/// </summary>
public static class ItemPartTree
{
    public const int PrefixLength = 41;
    public const int QuantityPrefixLength = 45;
    public const int ItemIdLength = 16;
    public const int TrailerLength = 20;

    /// <summary>ItemID 里结构字节的位置（物品头 +12）；itemData 里对应 +16。</summary>
    public const int StructureOffset = 12;

    /// <summary>带数量的结构（手雷）。物品头这 4 字节是 <c>03 00 00 00</c>，普通装备是 <c>00 [u16 计数器] 00</c>。</summary>
    public const byte QuantityStructure = 3;

    /// <summary>45 字节前缀里数量字段的位置。</summary>
    public const int QuantityOffset = 25;

    private const int MaxChildren = 64;
    private const int MaxDepth = 16;
    private const int MaxNameLength = 512;

    /// <summary>按 itemData 自带的根 ItemID 的结构字节（与物品头一致）决定前缀长度：3 为 45，其余为 41。</summary>
    public static int PrefixLengthOf(ReadOnlySpan<byte> body) =>
        body.Length > 4 + StructureOffset && body[4 + StructureOffset] == QuantityStructure ? QuantityPrefixLength : PrefixLength;

    /// <summary>物品头（或任何 16 字节 ItemID）是不是带数量的结构 3。</summary>
    public static bool IsQuantityStructure(ReadOnlySpan<byte> itemId) =>
        itemId.Length > StructureOffset && itemId[StructureOffset] == QuantityStructure;

    public static ItemDataBody Parse(ReadOnlySpan<byte> body)
    {
        var prefixLength = PrefixLengthOf(body);
        if (body.Length < prefixLength)
            throw new InvalidDataException(UiText.T($"itemData 只有 {body.Length} 字节，放不下 {prefixLength} 字节固定前缀。", $"itemData 只有 {body.Length} 位元組，放不下 {prefixLength} 位元組固定字首。", $"itemData has only {body.Length} bytes, too short for the {prefixLength}-byte fixed prefix."));
        var cursor = prefixLength;
        var root = ReadPart(body, ref cursor, 0);
        if (cursor != body.Length)
            throw new InvalidDataException(UiText.T($"配件树解析停在 {cursor}，itemData 长 {body.Length}，没有正好读完。", $"配件樹解析停在 {cursor}，itemData 長 {body.Length}，沒有正好讀完。", $"Part tree parsing stopped at {cursor} of {body.Length} bytes instead of reaching the end."));
        return new ItemDataBody(body[..prefixLength].ToArray(), root);
    }

    public static byte[] Write(ItemDataBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var prefixLength = PrefixLengthOf(body.Prefix);
        if (body.Prefix.Length != prefixLength)
            throw new InvalidDataException(UiText.T($"固定前缀必须是 {prefixLength} 字节（结构字节为 3 的物品 45 字节，其余 41 字节）。", $"固定字首必須是 {prefixLength} 位元組（結構位元組為 3 的物品 45 位元組，其餘 41 位元組）。", $"The fixed prefix must be {prefixLength} bytes (45 for items with structure byte 3, otherwise 41)."));
        var buffer = new List<byte>(prefixLength + 64);
        buffer.AddRange(body.Prefix);
        WritePart(buffer, body.Root, 0);
        return buffer.ToArray();
    }

    private static ItemPart ReadPart(ReadOnlySpan<byte> body, ref int cursor, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException(UiText.T($"配件树深度超过 {MaxDepth}。", $"配件樹深度超過 {MaxDepth}。", $"The part tree is deeper than {MaxDepth}."));
        var id = Take(body, ref cursor, ItemIdLength, "ItemID");
        var nameLength = ReadCanonical(body, ref cursor, UiText.T("外观名长度", "外觀名長度", "appearance name length"));
        if (nameLength > 0)
            throw new InvalidDataException(UiText.T($"外观名长度 {nameLength} 为正数，当前只接受负数 ASCII 长度。", $"外觀名長度 {nameLength} 為正數，當前只接受負數 ASCII 長度。", $"Appearance name length {nameLength} is positive; only negative (ASCII) lengths are supported."));
        if (-nameLength > MaxNameLength)
            throw new InvalidDataException(UiText.T($"外观名长度 {-nameLength} 超出安全范围 0..{MaxNameLength}。", $"外觀名長度 {-nameLength} 超出安全範圍 0..{MaxNameLength}。", $"Appearance name length {-nameLength} is outside the safe range 0..{MaxNameLength}."));
        var name = Take(body, ref cursor, -nameLength, UiText.T("外观名", "外觀名", "appearance name"));
        if (name.Any(value => value >= 0x80))
            throw new InvalidDataException(UiText.T("外观名不是可原样写回的 ASCII。", "外觀名不是可原樣寫回的 ASCII。", "The appearance name is not ASCII that can be written back unchanged."));
        var slot = BinaryPrimitives.ReadUInt64LittleEndian(Take(body, ref cursor, 8, UiText.T("挂点", "掛點", "slot")));
        var count = ReadCanonical(body, ref cursor, UiText.T("子配件数", "子配件數", "child part count"));
        if (count is < 0 or > MaxChildren)
            throw new InvalidDataException(UiText.T($"子配件数 {count} 超出安全范围 0..{MaxChildren}。", $"子配件數 {count} 超出安全範圍 0..{MaxChildren}。", $"Child part count {count} is outside the safe range 0..{MaxChildren}."));
        var children = new ItemPart[count];
        for (var i = 0; i < count; i++)
            children[i] = ReadPart(body, ref cursor, depth + 1);
        var trailer = Take(body, ref cursor, TrailerLength, UiText.T("配件尾部", "配件尾部", "part trailer"));
        return new ItemPart(id, Encoding.ASCII.GetString(name), slot, children, trailer);
    }

    private static void WritePart(List<byte> buffer, ItemPart part, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException(UiText.T($"配件树深度超过 {MaxDepth}。", $"配件樹深度超過 {MaxDepth}。", $"The part tree is deeper than {MaxDepth}."));
        if (part.ItemId.Length != ItemIdLength || part.Trailer.Length != TrailerLength)
            throw new InvalidDataException(UiText.T("配件的 ItemID 必须是 16 字节、尾部必须是 20 字节。", "配件的 ItemID 必須是 16 位元組、尾部必須是 20 位元組。", "A part's ItemID must be 16 bytes and its trailer 20 bytes."));
        if (part.Children.Count > MaxChildren)
            throw new InvalidDataException(UiText.T($"子配件数 {part.Children.Count} 超出安全范围 0..{MaxChildren}。", $"子配件數 {part.Children.Count} 超出安全範圍 0..{MaxChildren}。", $"Child part count {part.Children.Count} is outside the safe range 0..{MaxChildren}."));
        var name = Encoding.ASCII.GetBytes(part.Appearance);
        if (name.Length > MaxNameLength || part.Appearance.Any(ch => ch >= 0x80))
            throw new InvalidDataException(UiText.T("外观名不是可原样写回的 ASCII。", "外觀名不是可原樣寫回的 ASCII。", "The appearance name is not ASCII that can be written back unchanged."));
        buffer.AddRange(part.ItemId);
        NodeTable.WriteCompact(buffer, -name.Length);
        buffer.AddRange(name);
        var slot = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(slot, part.Slot);
        buffer.AddRange(slot);
        NodeTable.WriteCompact(buffer, part.Children.Count);
        foreach (var child in part.Children)
            WritePart(buffer, child, depth + 1);
        buffer.AddRange(part.Trailer);
    }

    private static byte[] Take(ReadOnlySpan<byte> body, ref int cursor, int length, string what)
    {
        if (length < 0 || length > body.Length - cursor)
            throw new InvalidDataException(UiText.T($"配件树在{what}处被截断（偏移 {cursor}）。", $"配件樹在{what}處被截斷（偏移 {cursor}）。", $"The part tree is truncated at the {what} (offset {cursor})."));
        var bytes = body.Slice(cursor, length).ToArray();
        cursor += length;
        return bytes;
    }

    private static int ReadCanonical(ReadOnlySpan<byte> body, ref int cursor, string what)
    {
        var start = cursor;
        int value;
        try
        {
            value = NodeTable.ReadCompactInt(body, ref cursor);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException(UiText.T($"配件树在{what}处（偏移 {start}）：{error.Message}", $"配件樹在{what}處（偏移 {start}）：{error.Message}", $"Part tree error at the {what} (offset {start}): {error.Message}"));
        }

        var canonical = new List<byte>(5);
        NodeTable.WriteCompact(canonical, value);
        if (!body[start..cursor].SequenceEqual(canonical.ToArray()))
            throw new InvalidDataException(UiText.T($"配件树的{what}（偏移 {start}）不是最短编码，无法原样写回。", $"配件樹的{what}（偏移 {start}）不是最短編碼，無法原樣寫回。", $"The part tree's {what} (offset {start}) is not in its shortest encoding and cannot be written back unchanged."));
        return value;
    }
}
