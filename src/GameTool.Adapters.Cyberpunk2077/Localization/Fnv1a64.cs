namespace GameTool.Adapters.Cyberpunk2077.Localization;

/// <summary>FNV-1a 64。路径按 ASCII 字节、反斜杠计算，与归档索引里的路径哈希一致。</summary>
public static class Fnv1a64
{
    public const ulong OffsetBasis = 0xCBF29CE484222325UL;
    public const ulong Prime = 0x100000001B3UL;

    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        var hash = OffsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }

    public static ulong HashPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Hash(System.Text.Encoding.ASCII.GetBytes(path));
    }
}
