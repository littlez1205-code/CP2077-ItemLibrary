using System.Text;

namespace GameTool.Adapters.Cyberpunk2077;

/// <summary>
/// TweakDBID = CRC32(ASCII 名字) 放在低 32 位，名字字节长度放在高 32 位。
/// CRC32 是 IEEE 多项式 0xEDB88320，不依赖第三方包。
/// </summary>
public static class TweakDbId
{
    public static ulong FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var bytes = Encoding.ASCII.GetBytes(name);
        return Crc32(bytes) | ((ulong)bytes.Length << 32);
    }

    public static ulong FromLocator(string locator)
    {
        const string prefix = "tdb:";
        if (string.IsNullOrEmpty(locator) || !locator.StartsWith(prefix, StringComparison.Ordinal) || locator.Length == prefix.Length)
            throw new InvalidDataException($"locator 必须是非空的 tdb: 名字，实际为 {locator}。");
        return FromName(locator[prefix.Length..]);
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
        }

        return ~crc;
    }
}
