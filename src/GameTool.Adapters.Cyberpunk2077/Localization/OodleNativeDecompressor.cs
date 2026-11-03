using GameTool.Core;
using System.Runtime.InteropServices;

namespace GameTool.Adapters.Cyberpunk2077.Localization;

/// <summary>
/// 从游戏目录加载 oo2ext 并调用 OodleLZ_Decompress。不复制 DLL。
/// 签名按本机 bin\x64\oo2ext_7_win64.dll 实测：跳过 KARK 的 8 字节后，最后一参为 3 时返回解压长度。
/// </summary>
public sealed class OodleNativeDecompressor : IOodleDecompressor
{
    private readonly DecompressFn _decompress;

    private OodleNativeDecompressor(DecompressFn decompress) => _decompress = decompress;

    public static OodleNativeDecompressor Load(string dllPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);
        if (!Environment.Is64BitProcess)
            throw new InvalidDataException(UiText.T("Oodle 解压只允许在 64 位进程中加载。", "Oodle 解壓只允許在 64 位程序中載入。", "Oodle decompression can only be loaded in a 64-bit process."));
        if (!File.Exists(dllPath))
            throw new InvalidDataException(UiText.T($"找不到 Oodle 组件：{dllPath}", $"找不到 Oodle 元件：{dllPath}", $"Oodle library not found: {dllPath}"));

        var module = LoadLibrary(dllPath);
        if (module == 0)
            throw new InvalidDataException(UiText.T($"无法加载 Oodle 组件（Win32 {Marshal.GetLastWin32Error()}）：{dllPath}", $"無法載入 Oodle 元件（Win32 {Marshal.GetLastWin32Error()}）：{dllPath}", $"Cannot load the Oodle library (Win32 {Marshal.GetLastWin32Error()}): {dllPath}"));
        var address = GetProcAddress(module, "OodleLZ_Decompress");
        if (address == 0)
            throw new InvalidDataException(UiText.T($"Oodle 组件里没有 OodleLZ_Decompress：{dllPath}", $"Oodle 元件裡沒有 OodleLZ_Decompress：{dllPath}", $"The Oodle library has no OodleLZ_Decompress: {dllPath}"));
        return new OodleNativeDecompressor(Marshal.GetDelegateForFunctionPointer<DecompressFn>(address));
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int uncompressedSize)
    {
        if (uncompressedSize < 0)
            throw new InvalidDataException(UiText.T("解压长度无效。", "解壓長度無效。", "Invalid decompressed length."));
        var source = compressed.ToArray();
        // 缓冲比声明长度多 64 字节，避免解码器越界写进托管堆。
        // 本机 oo2ext 把第 4 个参数当作要解出的长度；传成缓冲总长会返回 0。
        var destination = new byte[uncompressedSize + 64];
        var written = _decompress(source, source.Length, destination, uncompressedSize, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3);
        if (written != uncompressedSize)
            throw new InvalidDataException(UiText.T($"Oodle 解压失败：期望 {uncompressedSize} 字节，实际 {written}。", $"Oodle 解壓失敗：期望 {uncompressedSize} 位元組，實際 {written}。", $"Oodle decompression failed: expected {uncompressedSize} bytes, got {written}."));
        return destination[..uncompressedSize];
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadLibrary(string path);

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecompressFn(
        byte[] source,
        int sourceLength,
        byte[] destination,
        int destinationLength,
        int fuzz,
        int crc,
        int verbose,
        nint destinationBase,
        int destinationBaseSize,
        nint callback,
        nint callbackData,
        nint memory,
        int memorySize,
        int threadPhase);
}
