using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace GameTool.Adapters.Cyberpunk2077.Resources;

/// <summary>handle:X 的值：指向同一文件里的导出对象（0 起的序号，无效时为 -1）。</summary>
public readonly record struct Cr2wHandle(int ExportIndex);

/// <summary>rRef: / raRef: 的值：导入表里的深度路径；空引用为 null。</summary>
public sealed record Cr2wResourceRef(string? DepotPath);

/// <summary>延迟数据缓冲：CR2W 缓冲表里的序号（1 起），对应归档文件的同号段。</summary>
public readonly record struct Cr2wBufferRef(int Index);

/// <summary>不认识的类型，保留原始字节，不抛错。</summary>
public sealed record Cr2wRawValue(string Type, byte[] Bytes);

/// <summary>结构或类的已序列化属性（未写出的属性取默认值，由调用方处理）。</summary>
public sealed class Cr2wStruct
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);

    public Cr2wStruct(string type) => Type = type;

    public string Type { get; }

    public IReadOnlyDictionary<string, object?> Properties => _properties;

    internal void Set(string name, object? value) => _properties[name] = value;

    public object? this[string name] => _properties.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => _properties.ContainsKey(name);

    public long GetInt(string name, long fallback = 0) => this[name] switch
    {
        long value => value,
        ulong value => unchecked((long)value),
        _ => fallback
    };

    public double GetFloat(string name, double fallback = 0) => this[name] switch
    {
        double value => value,
        long value => value,
        _ => fallback
    };

    public bool GetBool(string name, bool fallback = false) => this[name] is bool value ? value : fallback;

    /// <summary>CName、枚举的值或字符串。</summary>
    public string? GetName(string name) => this[name] as string;

    public Cr2wStruct? GetStruct(string name) => this[name] as Cr2wStruct;

    public IReadOnlyList<object?> GetArray(string name) => this[name] as IReadOnlyList<object?> ?? [];
}

/// <summary>
/// 最小的 CR2W（RED4，版本 195）读取器，按公开格式说明编写（T020 预研第 2 节）。
/// 头 40 字节 + 10 个表项（偏移、数量、校验）；用到 [0] 字符串池、[1] 名字、[2] 导入、[4] 导出、[5] 缓冲。
/// 导出体：一个 0 字节，然后是带标签的属性（u16 名字、u16 类型名、i32 长度含自身、值），以 u16 0 结束；嵌套结构同样以 0 字节开头。
/// 不认识的类型保留原始字节，不抛错。
/// </summary>
public sealed class Cr2wFile
{
    private const int HeaderLength = 40;
    private const int TableCount = 10;
    private const int MaxDepth = 32;

    private readonly byte[] _bytes;
    private readonly string[] _names;
    private readonly string?[] _imports;
    private readonly Export[] _exports;

    private Cr2wFile(byte[] bytes, uint version, string[] names, string?[] imports, Export[] exports, Cr2wBuffer[] buffers)
    {
        _bytes = bytes;
        Version = version;
        _names = names;
        _imports = imports;
        _exports = exports;
        Buffers = buffers;
    }

    public uint Version { get; }

    public int ExportCount => _exports.Length;

    public IReadOnlyList<Cr2wBuffer> Buffers { get; }

    public IReadOnlyList<string?> Imports => _imports;

    public string ExportClass(int index) => _exports[index].ClassName;

    public static Cr2wFile Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < HeaderLength + TableCount * 12 || !bytes.AsSpan(0, 4).SequenceEqual("CR2W"u8))
            throw new InvalidDataException("不是 CR2W 资源。");
        var version = U32(bytes, 4);
        var tables = new (int Offset, int Count)[TableCount];
        for (var i = 0; i < TableCount; i++)
        {
            var at = HeaderLength + i * 12;
            tables[i] = ((int)U32(bytes, at), (int)U32(bytes, at + 4));
        }

        var (poolOffset, poolSize) = tables[0];
        Check(bytes, poolOffset, poolSize, 1, "字符串池");
        string Pool(uint offset)
        {
            if (offset >= poolSize)
                throw new InvalidDataException("CR2W 字符串偏移越界。");
            var start = poolOffset + (int)offset;
            var end = Array.IndexOf(bytes, (byte)0, start, poolOffset + poolSize - start);
            if (end < 0)
                end = poolOffset + poolSize;
            return Encoding.UTF8.GetString(bytes, start, end - start);
        }

        Check(bytes, tables[1].Offset, tables[1].Count, 8, "名字表");
        var names = new string[tables[1].Count];
        for (var i = 0; i < names.Length; i++)
            names[i] = Pool(U32(bytes, tables[1].Offset + i * 8));

        Check(bytes, tables[2].Offset, tables[2].Count, 8, "导入表");
        var imports = new string?[tables[2].Count];
        for (var i = 0; i < imports.Length; i++)
            imports[i] = Pool(U32(bytes, tables[2].Offset + i * 8));

        Check(bytes, tables[4].Offset, tables[4].Count, 24, "导出表");
        var exports = new Export[tables[4].Count];
        for (var i = 0; i < exports.Length; i++)
        {
            var at = tables[4].Offset + i * 24;
            var classIndex = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            var size = (int)U32(bytes, at + 8);
            var offset = (int)U32(bytes, at + 12);
            if (classIndex >= names.Length)
                throw new InvalidDataException("CR2W 导出的类名越界。");
            Check(bytes, offset, size, 1, "导出数据");
            exports[i] = new Export(names[classIndex], offset, size);
        }

        Check(bytes, tables[5].Offset, tables[5].Count, 24, "缓冲表");
        var buffers = new Cr2wBuffer[tables[5].Count];
        for (var i = 0; i < buffers.Length; i++)
        {
            var at = tables[5].Offset + i * 24;
            buffers[i] = new Cr2wBuffer(U32(bytes, at), U32(bytes, at + 4), U32(bytes, at + 8), U32(bytes, at + 12), U32(bytes, at + 16));
        }

        return new Cr2wFile(bytes, version, names, imports, exports, buffers);
    }

    /// <summary>解析第 index 个导出对象（0 起）的属性。</summary>
    public Cr2wStruct ReadExport(int index)
    {
        if (index < 0 || index >= _exports.Length)
            throw new InvalidDataException($"CR2W 没有第 {index} 个导出对象。");
        var export = _exports[index];
        var cursor = export.Offset;
        return ReadStruct(export.ClassName, ref cursor, export.Offset + export.Size, 0);
    }

    private Cr2wStruct ReadStruct(string type, ref int cursor, int end, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("CR2W 嵌套过深。");
        var result = new Cr2wStruct(type);
        if (cursor >= end || _bytes[cursor] != 0)
            throw new InvalidDataException($"CR2W 结构 {type} 不是以 0 字节开头。");
        cursor++;
        while (true)
        {
            if (cursor + 2 > end)
                throw new InvalidDataException($"CR2W 结构 {type} 被截断。");
            var nameIndex = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor));
            cursor += 2;
            if (nameIndex == 0)
                return result;
            if (cursor + 6 > end)
                throw new InvalidDataException($"CR2W 结构 {type} 被截断。");
            var typeIndex = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor));
            var size = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(cursor + 2));
            cursor += 6;
            var valueLength = size - 4;
            if (nameIndex >= _names.Length || typeIndex >= _names.Length || valueLength < 0 || cursor + valueLength > end)
                throw new InvalidDataException($"CR2W 结构 {type} 的属性越界。");
            var propertyType = _names[typeIndex];
            var valueEnd = cursor + valueLength;
            object? value;
            try
            {
                var inner = cursor;
                value = ReadProperty(propertyType, ref inner, valueEnd, depth + 1);
            }
            catch (InvalidDataException)
            {
                value = new Cr2wRawValue(propertyType, _bytes[cursor..valueEnd]);
            }

            result.Set(_names[nameIndex], value);
            cursor = valueEnd;
        }
    }

    /// <summary>属性值：长度已知，认不出的类型按长度猜（2 字节 → 枚举名，0 字节开头 → 结构），再不行保留原始字节。</summary>
    private object? ReadProperty(string type, ref int cursor, int end, int depth)
    {
        if (TryReadKnown(type, ref cursor, end, depth, out var value))
            return value;
        var length = end - cursor;
        if (length == 2)
            return Name(BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor)));
        if (length > 0 && _bytes[cursor] == 0)
            return ReadStruct(type, ref cursor, end, depth);
        return new Cr2wRawValue(type, _bytes[cursor..end]);
    }

    /// <summary>数组元素：没有逐个长度，枚举按 2 字节、其余未知类型按结构读。</summary>
    private object? ReadElement(string type, ref int cursor, int end, int depth)
    {
        if (TryReadKnown(type, ref cursor, end, depth, out var value))
            return value;
        if (IsEnumName(type))
        {
            Need(cursor, 2, end);
            var name = Name(BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor)));
            cursor += 2;
            return name;
        }

        return ReadStruct(type, ref cursor, end, depth);
    }

    private bool TryReadKnown(string type, ref int cursor, int end, int depth, out object? value)
    {
        value = null;
        switch (type)
        {
            case "Bool":
                Need(cursor, 1, end);
                value = _bytes[cursor++] != 0;
                return true;
            case "Uint8":
                Need(cursor, 1, end);
                value = (long)_bytes[cursor++];
                return true;
            case "Int8":
                Need(cursor, 1, end);
                value = (long)(sbyte)_bytes[cursor++];
                return true;
            case "Uint16":
                Need(cursor, 2, end);
                value = (long)BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor));
                cursor += 2;
                return true;
            case "Int16":
                Need(cursor, 2, end);
                value = (long)BinaryPrimitives.ReadInt16LittleEndian(_bytes.AsSpan(cursor));
                cursor += 2;
                return true;
            case "Uint32":
                Need(cursor, 4, end);
                value = (long)BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(cursor));
                cursor += 4;
                return true;
            case "Int32":
                Need(cursor, 4, end);
                value = (long)BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(cursor));
                cursor += 4;
                return true;
            case "Uint64":
                Need(cursor, 8, end);
                value = BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(cursor));
                cursor += 8;
                return true;
            case "Int64":
                Need(cursor, 8, end);
                value = BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(cursor));
                cursor += 8;
                return true;
            case "Float":
                Need(cursor, 4, end);
                value = (double)BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(cursor));
                cursor += 4;
                return true;
            case "Double":
                Need(cursor, 8, end);
                value = BinaryPrimitives.ReadDoubleLittleEndian(_bytes.AsSpan(cursor));
                cursor += 8;
                return true;
            case "CName":
                Need(cursor, 2, end);
                value = Name(BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor)));
                cursor += 2;
                return true;
            case "String":
                value = ReadString(ref cursor, end);
                return true;
            case "serializationDeferredDataBuffer":
            case "SharedDataBuffer":
                Need(cursor, 2, end);
                value = new Cr2wBufferRef(BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor)));
                cursor += 2;
                return true;
        }

        if (type.StartsWith("array:", StringComparison.Ordinal))
        {
            value = ReadArray(type["array:".Length..], ref cursor, end, depth);
            return true;
        }

        if (type.StartsWith('[') && type.IndexOf(']') is var close and > 1
            && int.TryParse(type.AsSpan(1, close - 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            value = ReadArray(type[(close + 1)..], ref cursor, end, depth);
            return true;
        }

        if (type.StartsWith("handle:", StringComparison.Ordinal) || type.StartsWith("whandle:", StringComparison.Ordinal))
        {
            Need(cursor, 4, end);
            var index = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(cursor));
            cursor += 4;
            value = new Cr2wHandle(index > 0 ? index - 1 : -1);
            return true;
        }

        if (type.StartsWith("rRef:", StringComparison.Ordinal) || type.StartsWith("raRef:", StringComparison.Ordinal))
        {
            Need(cursor, 2, end);
            var index = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(cursor));
            cursor += 2;
            value = new Cr2wResourceRef(index > 0 && index <= _imports.Length ? _imports[index - 1] : null);
            return true;
        }

        return false;
    }

    private List<object?> ReadArray(string elementType, ref int cursor, int end, int depth)
    {
        Need(cursor, 4, end);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(cursor));
        cursor += 4;
        if (count > end - cursor)
            throw new InvalidDataException("CR2W 数组长度越界。");
        var items = new List<object?>((int)count);
        for (var i = 0; i < count; i++)
            items.Add(ReadElement(elementType, ref cursor, end, depth + 1));
        return items;
    }

    /// <summary>RED4 字符串：变长前缀（首字节低 6 位 + 续位），负号位表示 UTF-8 字节数，否则是 UTF-16 字符数。</summary>
    private string ReadString(ref int cursor, int end)
    {
        Need(cursor, 1, end);
        var first = _bytes[cursor++];
        var negative = (first & 0x80) != 0;
        var length = first & 0x3F;
        if ((first & 0x40) != 0)
        {
            var shift = 6;
            byte next;
            do
            {
                Need(cursor, 1, end);
                next = _bytes[cursor++];
                length |= (next & 0x7F) << shift;
                shift += 7;
            }
            while ((next & 0x80) != 0 && shift < 32);
        }

        if (negative)
        {
            Need(cursor, length, end);
            var text = Encoding.UTF8.GetString(_bytes, cursor, length);
            cursor += length;
            return text;
        }

        Need(cursor, length * 2, end);
        var wide = Encoding.Unicode.GetString(_bytes, cursor, length * 2);
        cursor += length * 2;
        return wide;
    }

    private string Name(int index) =>
        index < _names.Length ? _names[index] : throw new InvalidDataException("CR2W 名字序号越界。");

    private static bool IsEnumName(string type) =>
        type.Length > 1 && type[0] == 'E' && char.IsUpper(type[1]);

    private static void Need(int cursor, int length, int end)
    {
        if (length < 0 || cursor + length > end)
            throw new InvalidDataException("CR2W 值被截断。");
    }

    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));

    private static void Check(byte[] bytes, int offset, int count, int itemSize, string what)
    {
        if (offset < 0 || count < 0 || (long)offset + (long)count * itemSize > bytes.Length)
            throw new InvalidDataException($"CR2W 的{what}越界。");
    }

    private readonly record struct Export(string ClassName, int Offset, int Size);
}

/// <summary>CR2W 缓冲表的一项（标志、序号、偏移、盘上长度、解压后长度）。</summary>
public readonly record struct Cr2wBuffer(uint Flags, uint Index, uint Offset, uint DiskSize, uint MemorySize);
