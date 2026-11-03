using GameTool.Core;
using System.Text;

namespace GameTool.Adapters.Cyberpunk2077;

/// <summary>
/// TweakDBID = CRC32(ASCII 名字) 放在低 32 位，名字字节长度放在高 32 位。
/// </summary>
public static class TweakDbId
{
    public static ulong FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var bytes = Encoding.ASCII.GetBytes(name);
        return Crc32.Compute(bytes) | ((ulong)bytes.Length << 32);
    }

    public static ulong FromLocator(string locator)
    {
        const string prefix = "tdb:";
        if (string.IsNullOrEmpty(locator) || !locator.StartsWith(prefix, StringComparison.Ordinal) || locator.Length == prefix.Length)
            throw new InvalidDataException(UiText.T($"locator 必须是非空的 tdb: 名字，实际为 {locator}。", $"locator 必須是非空的 tdb: 名字，實際為 {locator}。", $"A locator must be a non-empty tdb: name, but was {locator}."));
        return FromName(locator[prefix.Length..]);
    }
}
