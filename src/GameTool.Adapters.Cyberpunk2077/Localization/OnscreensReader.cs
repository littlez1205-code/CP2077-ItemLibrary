using GameTool.Core;
using System.Text;

namespace GameTool.Adapters.Cyberpunk2077.Localization;

public sealed record LocEntry(ulong PrimaryKey, string SecondaryKey, string FemaleVariant, string MaleVariant);

/// <summary>
/// 只读取 localizationPersistenceOnScreenEntry 需要的 CR2W 子集。
/// 名字表和属性步进来自本机 lang_zh-cn onscreens 实测，不引用社区解析器。
/// </summary>
public static class OnscreensReader
{
    public static IReadOnlyList<LocEntry> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40 || data[0] != (byte)'C' || data[1] != (byte)'R' || data[2] != (byte)'2' || data[3] != (byte)'W')
            throw new InvalidDataException(UiText.T("不是 CR2W 资源。", "不是 CR2W 資源。", "This is not a CR2W resource."));
        var tableCount = ReadInt(data, 36);
        if (tableCount < 2)
            throw new InvalidDataException(UiText.T("CR2W 表数量不足。", "CR2W 表數量不足。", "The CR2W resource has too few tables."));
        var directory = 40;
        if (data.Length < directory + tableCount * 12)
            throw new InvalidDataException(UiText.T("CR2W 表目录越界。", "CR2W 表目錄越界。", "The CR2W table directory is out of range."));
        var poolOffset = ReadInt(data, directory);
        var poolSize = ReadInt(data, directory + 4);
        var nameOffset = ReadInt(data, directory + 12);
        var nameCount = ReadInt(data, directory + 16);
        if (poolOffset < 0 || poolSize < 0 || poolOffset > data.Length - poolSize)
            throw new InvalidDataException(UiText.T("CR2W 字符串池越界。", "CR2W 字串池越界。", "The CR2W string pool is out of range."));
        if (nameCount < 1 || nameOffset < 0 || nameOffset > data.Length - nameCount * 8)
            throw new InvalidDataException(UiText.T("CR2W 名字表越界。", "CR2W 名字表越界。", "The CR2W name table is out of range."));

        var names = new string[nameCount];
        for (var i = 0; i < nameCount; i++)
        {
            var relative = ReadInt(data, nameOffset + i * 8);
            if (relative < 0 || relative > poolSize)
                throw new InvalidDataException(UiText.T("CR2W 名字偏移越界。", "CR2W 名字偏移越界。", "A CR2W name offset is out of range."));
            var end = relative;
            while (end < poolSize && data[poolOffset + end] != 0)
                end++;
            names[i] = Encoding.UTF8.GetString(data.Slice(poolOffset + relative, end - relative));
        }

        var primary = IndexOf(names, "primaryKey");
        var secondary = IndexOf(names, "secondaryKey");
        var female = IndexOf(names, "femaleVariant");
        var male = IndexOf(names, "maleVariant");
        var uint64 = IndexOf(names, "Uint64");
        var text = IndexOf(names, "String");
        var entries = new List<LocEntry>();
        var cursor = nameOffset + nameCount * 8;
        cursor = FindProperty(data, cursor, primary, uint64);
        while (cursor >= 0)
        {
            var size = ReadSize(data, cursor);
            var key = BitConverter.ToUInt64(data.Slice(cursor + 8, 8));
            cursor += 4 + size;
            var secondaryKey = "";
            var femaleVariant = "";
            var maleVariant = "";
            while (cursor + 2 <= data.Length)
            {
                var name = BitConverter.ToUInt16(data.Slice(cursor, 2));
                if (name == 0)
                {
                    cursor += 2;
                    break;
                }

                if (cursor + 8 > data.Length)
                    throw new InvalidDataException(UiText.T("CR2W 属性越界。", "CR2W 屬性越界。", "A CR2W property is out of range."));
                var type = BitConverter.ToUInt16(data.Slice(cursor + 2, 2));
                size = ReadSize(data, cursor);
                if (type == text)
                {
                    var value = ReadText(data.Slice(cursor + 8, size - 4));
                    if (name == secondary)
                        secondaryKey = value;
                    else if (name == female)
                        femaleVariant = value;
                    else if (name == male)
                        maleVariant = value;
                }

                cursor += 4 + size;
            }

            entries.Add(new LocEntry(key, secondaryKey, femaleVariant, maleVariant));
            var next = FindProperty(data, cursor, primary, uint64);
            if (next < 0 || next - cursor > 16)
                break;
            cursor = next;
        }

        if (entries.Count == 0)
            throw new InvalidDataException(UiText.T("没有读到本地化条目。", "沒有讀到本地化條目。", "No localization entries were read."));
        return entries;
    }

    private static int FindProperty(ReadOnlySpan<byte> data, int start, int name, int type)
    {
        for (var i = start; i + 4 <= data.Length && i - start <= 4096; i++)
        {
            if (BitConverter.ToUInt16(data.Slice(i, 2)) == name && BitConverter.ToUInt16(data.Slice(i + 2, 2)) == type)
                return i;
        }

        return -1;
    }

    private static int ReadSize(ReadOnlySpan<byte> data, int cursor)
    {
        var size = ReadInt(data, cursor + 4);
        if (size < 4 || cursor > data.Length - 4 - size)
            throw new InvalidDataException(UiText.T("CR2W 属性长度无效。", "CR2W 屬性長度無效。", "A CR2W property has an invalid length."));
        return size;
    }

    /// <summary>
    /// 10xxxxxx 为 7 位长度；11xxxxxx 起为 6 位，后续字节各 7 位，最高位为延续标记。
    /// </summary>
    internal static string ReadText(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            throw new InvalidDataException(UiText.T("字符串载荷为空。", "字串載荷為空。", "A string payload is empty."));
        var index = 0;
        var head = payload[index++];
        if ((head & 0x80) == 0)
            throw new InvalidDataException(UiText.T("未识别的字符串长度前缀。", "未識別的字串長度字首。", "Unrecognized string length prefix."));
        int length;
        if ((head & 0x40) == 0)
            length = head & 0x7F;
        else
        {
            length = head & 0x3F;
            var shift = 6;
            while (true)
            {
                if (index >= payload.Length || shift > 28)
                    throw new InvalidDataException(UiText.T("字符串长度前缀不完整。", "字串長度字首不完整。", "A string length prefix is incomplete."));
                var next = payload[index++];
                length |= (next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                    break;
                shift += 7;
            }
        }

        if (index + length != payload.Length)
            throw new InvalidDataException(UiText.T("字符串长度和属性长度不一致。", "字串長度和屬性長度不一致。", "A string length does not match its property length."));
        return Encoding.UTF8.GetString(payload.Slice(index, length));
    }

    private static int IndexOf(string[] names, string expected)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] == expected)
                return i;
        }

        throw new InvalidDataException(UiText.T($"CR2W 名字表里没有 {expected}。", $"CR2W 名字表裡沒有 {expected}。", $"The CR2W name table has no {expected}."));
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
            throw new InvalidDataException(UiText.T("CR2W 字段越界。", "CR2W 欄位越界。", "A CR2W field is out of range."));
        return BitConverter.ToInt32(data.Slice(offset, 4));
    }
}
