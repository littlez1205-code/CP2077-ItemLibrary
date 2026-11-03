namespace GameTool.Adapters.Cyberpunk2077;

/// <summary>
/// CRC-32（IEEE，反射多项式 0xEDB88320，初值和结束异或都是 0xFFFFFFFF），与 zlib / PNG 的相同。
/// TweakDBID 的名字哈希和 PNG 块校验共用这一份；查表实现，不依赖第三方包。
/// </summary>
public static class Crc32
{
    private const uint Seed = 0xFFFF_FFFFu;

    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data) => Update(Seed, data) ^ Seed;

    /// <summary>分段计算两段数据连起来的 CRC，不必先拼接。</summary>
    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) => Update(Update(Seed, first), second) ^ Seed;

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var bit = 0; bit < 8; bit++)
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
