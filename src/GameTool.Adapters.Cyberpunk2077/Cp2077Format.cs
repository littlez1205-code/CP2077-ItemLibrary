namespace GameTool.Adapters.Cyberpunk2077;

/// <summary>
/// Cyberpunk 2077 v2.31 存档容器常量。数值来自 ManualSave-7 实测，越界即拒绝，不截断。
/// </summary>
public static class Cp2077Format
{
    private static readonly byte[] VascBytes = [0x56, 0x41, 0x53, 0x43];
    private static readonly byte[] FzlcBytes = [0x46, 0x5A, 0x4C, 0x43];
    private static readonly byte[] DoneBytes = [0x45, 0x4E, 0x4F, 0x44];
    private static readonly byte[] NodeBytes = [0x45, 0x44, 0x4F, 0x4E];

    public static ReadOnlySpan<byte> Vasc => VascBytes;
    public static ReadOnlySpan<byte> Fzlc => FzlcBytes;
    public static ReadOnlySpan<byte> Done => DoneBytes;

    /// <summary>节点表魔数，磁盘序 45 44 4F 4E，即小端的 NODE。</summary>
    public static ReadOnlySpan<byte> Node => NodeBytes;

    /// <summary>节点个数安全上限。实测 ManualSave-7 为 1263。超过视为损坏。</summary>
    public const int MaxNodeCount = 1_000_000;

    /// <summary>块头 4 字节，磁盘序 34 5A 4C 58，即小端 u32 0x584C5A34。</summary>
    public const uint ChunkMagic = 0x584C5A34;

    /// <summary>白名单：支持 2.31 与 2.5。实测 2.31 为 269/2310，2.5 为 277/2500。</summary>
    public const uint SaveVersion231 = 269;
    public const uint GameVersion231 = 2310;

    public const uint SaveVersion250 = 277;
    public const uint GameVersion250 = 2500;

    public const uint SupportedSaveVersion = SaveVersion231;
    public const uint SupportedGameVersion = GameVersion231;

    public const uint LatestSaveVersion = SaveVersion250;
    public const uint LatestGameVersion = GameVersion250;

    public static bool IsSupported(uint saveVersion, uint gameVersion) =>
        (saveVersion == SaveVersion250 && gameVersion == GameVersion250) ||
        (saveVersion == SaveVersion231 && gameVersion == GameVersion231);

    /// <summary>分块表槽位数。合法块数是 1..512。未用槽位原样保留，不再要求全 0。</summary>
    public const int ChunkSlotCount = 512;

    /// <summary>
    /// 单块解压长度上限。实测每块 0x40000（262144）字节。
    /// 安全范围 1..16777216；超过视为损坏，避免解压炸弹。
    /// </summary>
    public const int MaxDecompressedChunkLength = 16 * 1024 * 1024;

    /// <summary>VASC 头到 FZLC 之前的字节数。元数据字符串实测为单字节 00。</summary>
    public const int HeaderBeforeFzlc = 25;

    public const int ChunkEntrySize = 12;
    public const int ChunkHeaderSize = 8;

    public const int FzlcOffset = HeaderBeforeFzlc;
    public const int ChunkCountOffset = HeaderBeforeFzlc + 4;
    public const int ChunkTableOffset = HeaderBeforeFzlc + 8;

    /// <summary>首块文件偏移。实测 ManualSave-7 为 0x1821（6177）。</summary>
    public const int FirstChunkOffset = ChunkTableOffset + (ChunkSlotCount * ChunkEntrySize);

    /// <summary>重新分块时每块的解压长度。实测存档按这个长度切。</summary>
    public const int DecompressedChunkSize = 0x40000;
}
