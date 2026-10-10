using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GameTool.Desktop;

/// <summary>
/// 一体式标题栏（D021）的窗口端：系统标题栏已由 WindowChrome 去掉，这里补上页面做不到的几件事。
/// <list type="bullet">
/// <item>边缘缩放：WebView2 盖住了整个客户区，系统收不到边缘的命中测试；页面边缘按下鼠标时把方向发过来，
/// 这里交给系统的缩放循环（WM_NCLBUTTONDOWN + HTLEFT 等），与原生边框的行为一致。</item>
/// <item>最大化不溢出：没有系统标题栏的窗口最大化后，系统仍按带边框的窗口摆放，四周各超出显示器工作区一圈
/// （实测 125% 缩放下 9 像素），内容会被裁掉。这里按窗口实际位置和所在显示器的工作区算出超出的部分，
/// 把页面往里缩同样的距离，内容正好落在工作区里，不会被裁掉、也不会跑到任务栏下面。</item>
/// <item>Windows 11 圆角：显式请求 DWM 圆角（Windows 10 上调用失败，忽略）。</item>
/// </list>
/// </summary>
internal static class WindowChromeHost
{
    private const int WmNcLButtonDown = 0x00A1;
    private const uint MonitorDefaultToNearest = 2;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int SmSwapButton = 23;

    /// <summary>页面边缘名 → WM_NCHITTEST 的命中码。</summary>
    private static readonly Dictionary<string, int> Edges = new(StringComparer.Ordinal)
    {
        ["left"] = 10,
        ["right"] = 11,
        ["top"] = 12,
        ["topLeft"] = 13,
        ["topRight"] = 14,
        ["bottom"] = 15,
        ["bottomLeft"] = 16,
        ["bottomRight"] = 17
    };

    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var corner = DwmCornerRound;
        try
        {
            _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 没有 DWM（极少见）：用系统默认的直角。
        }
    }

    public static bool IsEdge(string? edge) => edge is not null && Edges.ContainsKey(edge);

    /// <summary>
    /// 从页面边缘开始缩放。只在窗口是普通状态、且左键（按系统设置可能对调）仍按着时开始，
    /// 否则一次很快的单击会让系统缩放循环一直跟着鼠标走。
    /// </summary>
    public static bool BeginResize(Window window, string edge)
    {
        if (window.WindowState != WindowState.Normal || !Edges.TryGetValue(edge, out var hit))
            return false;
        var button = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        if ((GetAsyncKeyState(button) & 0x8000) == 0)
            return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetCursorPos(out var point))
            return false;
        ReleaseCapture();
        var position = (IntPtr)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
        SendMessage(handle, WmNcLButtonDown, (IntPtr)hit, position);
        return true;
    }

    /// <summary>最大化时窗口超出所在显示器工作区的部分（设备无关单位）；不是最大化时为 0。</summary>
    public static Thickness MaximizedOverflow(Window window)
    {
        if (window.WindowState != WindowState.Maximized)
            return default;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
            return default;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return default;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Thickness(
            Math.Max(0, info.Work.Left - bounds.Left) / dpi.DpiScaleX,
            Math.Max(0, info.Work.Top - bounds.Top) / dpi.DpiScaleY,
            Math.Max(0, bounds.Right - info.Work.Right) / dpi.DpiScaleX,
            Math.Max(0, bounds.Bottom - info.Work.Bottom) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
