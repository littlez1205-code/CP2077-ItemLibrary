namespace GameTool.Tests;

/// <summary>
/// 只在 Windows 上运行的用例：测的是 Windows 才有的路径行为（反斜杠分隔、盘符、%USERPROFILE% 缩写），
/// 程序本身也只在 Windows 上运行。其他系统上标为跳过，不算失败。
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = WindowsOnly.Reason;
    }
}

/// <inheritdoc cref="WindowsFactAttribute"/>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = WindowsOnly.Reason;
    }
}

internal static class WindowsOnly
{
    public const string Reason = "测的是 Windows 路径行为，只在 Windows 上运行。";
}
