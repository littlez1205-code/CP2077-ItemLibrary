namespace GameTool.Core;

/// <summary>
/// 存档或游戏版本不在当前适配器的白名单内。CLI 将其映射为退出码 3。
/// </summary>
public sealed class UnsupportedSaveException : Exception
{
    public uint SaveVersion { get; }
    public uint GameVersion { get; }

    public UnsupportedSaveException(uint saveVersion, uint gameVersion, string message)
        : base(message)
    {
        SaveVersion = saveVersion;
        GameVersion = gameVersion;
    }
}
