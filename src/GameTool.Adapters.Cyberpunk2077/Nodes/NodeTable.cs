using System.Buffers.Binary;
using System.Text;

namespace GameTool.Adapters.Cyberpunk2077.Nodes;

public sealed class NodeTable
{
    private readonly SaveNode[] _nodes;

    private NodeTable(SaveNode[] nodes) => _nodes = nodes;

    public int Count => _nodes.Length;

    public SaveNode this[int index] => _nodes[index];

    public SaveNode RequireSingle(string name)
    {
        SaveNode? found = null;
        foreach (var node in _nodes)
        {
            if (node.Name != name)
                continue;
            if (found is not null)
                throw new InvalidDataException($"节点 {name} 出现了多次。");
            found = node;
        }

        return found ?? throw new InvalidDataException($"缺少节点 {name}。");
    }

    public static NodeTable Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 5 || !bytes.StartsWith(Cp2077Format.Node))
            throw new InvalidDataException("节点表缺少 NODE 魔数。");

        var cursor = 4;
        var count = ReadCompactInt(bytes, ref cursor);
        if (count is < 0 or > Cp2077Format.MaxNodeCount)
            throw new InvalidDataException($"节点个数 {count} 超出安全范围 0..{Cp2077Format.MaxNodeCount}。");

        var nodes = new SaveNode[count];
        for (var i = 0; i < count; i++)
        {
            var name = ReadName(bytes, ref cursor);
            if (cursor + 16 > bytes.Length)
                throw new InvalidDataException("节点表在节点字段处被截断。");
            var next = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(cursor, 4));
            var child = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(cursor + 4, 4));
            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(cursor + 8, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(cursor + 12, 4));
            cursor += 16;
            if (length < 0)
                throw new InvalidDataException($"节点 {name} 的长度为负。");
            nodes[i] = new SaveNode(i, name, next, child, offset, length);
        }

        if (cursor != bytes.Length)
            throw new InvalidDataException($"节点表解析停在 {cursor}，表长 {bytes.Length}，没有正好读完。");
        return new NodeTable(nodes);
    }

    public static byte[] Write(IReadOnlyList<SaveNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count is < 1 or > Cp2077Format.MaxNodeCount)
            throw new InvalidDataException($"节点个数 {nodes.Count} 超出安全范围 1..{Cp2077Format.MaxNodeCount}。");
        var buffer = new List<byte>(4 + nodes.Count * 24);
        buffer.AddRange(Cp2077Format.Node.ToArray());
        WriteCompact(buffer, nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Index != i)
                throw new InvalidDataException($"节点序号 {node.Index} 不在位置 {i}。");
            var name = Encoding.ASCII.GetBytes(node.Name);
            WriteCompact(buffer, -name.Length);
            buffer.AddRange(name);
            WriteInt32(buffer, node.Next);
            WriteInt32(buffer, node.Child);
            WriteInt32(buffer, node.FileOffset);
            WriteInt32(buffer, node.Length);
        }

        return buffer.ToArray();
    }

    private static void WriteInt32(List<byte> buffer, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    internal static void WriteCompact(List<byte> buffer, int value)
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

    private static string ReadName(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var length = ReadCompactInt(bytes, ref cursor);
        if (length > 0)
            throw new InvalidDataException($"节点名长度 {length} 为正数，当前只接受负数 ASCII 长度。");
        var size = -length;
        if (size > 1024)
            throw new InvalidDataException($"节点名长度 {size} 超出安全范围 0..1024。");
        if (cursor + size > bytes.Length)
            throw new InvalidDataException("节点表在名字处被截断。");
        var name = Encoding.ASCII.GetString(bytes.Slice(cursor, size));
        cursor += size;
        return name;
    }

    /// <summary>
    /// 首字节 bit7 符号、bit6 续位、低 6 位是值；后续字节 bit7 续位、低 7 位是值。
    /// </summary>
    internal static int ReadCompactInt(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        if (cursor >= bytes.Length)
            throw new InvalidDataException("紧凑整数被截断。");
        var first = bytes[cursor++];
        var negative = (first & 0x80) != 0;
        var more = (first & 0x40) != 0;
        long value = first & 0x3F;
        var shift = 6;
        while (more)
        {
            if (cursor >= bytes.Length)
                throw new InvalidDataException("紧凑整数被截断。");
            if (shift > 31)
                throw new InvalidDataException("紧凑整数超出 32 位。");
            var next = bytes[cursor++];
            more = (next & 0x80) != 0;
            value |= (long)(next & 0x7F) << shift;
            shift += 7;
        }

        if (value > int.MaxValue)
            throw new InvalidDataException("紧凑整数超出 int 范围。");
        var magnitude = (int)value;
        return negative ? -magnitude : magnitude;
    }
}

public readonly record struct SaveNode(int Index, string Name, int Next, int Child, int FileOffset, int Length)
{
    public int PayloadOffset
    {
        get
        {
            if (FileOffset < Cp2077Format.FirstChunkOffset)
                throw new InvalidDataException($"节点 {Name} 的文件偏移 {FileOffset} 落在解压流之前。");
            return FileOffset - Cp2077Format.FirstChunkOffset;
        }
    }
}
