namespace GameTool.Core;

/// <summary>
/// 面向用户的提示文字的语言：简体中文、繁體中文、English。
/// 每条提示在原地写成 <c>UiText.T("简体", "繁體", "English")</c>，三种语言放在一起，改一处不会漏掉另外两处。
/// 桌面端每个请求都在 <see cref="Use"/> 的作用域里处理（BridgeDispatcher），作用域里给出的提示按界面语言；
/// 作用域外（命令行、单元测试）一律简体中文。
/// 用 AsyncLocal 而不是全局变量：并行处理的请求、并行运行的测试各用各的语言，互不影响；
/// 在作用域里启动的后台任务（如使能的存档监听）沿用启动时的语言。
/// </summary>
public static class UiText
{
    public const string SimplifiedChinese = "zh-cn";
    public const string TraditionalChinese = "zh-tw";
    public const string English = "en-us";

    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>当前作用域的语言；不在作用域里时为简体中文。</summary>
    public static string Language => Current.Value ?? SimplifiedChinese;

    /// <summary>在返回值被释放之前，提示文字使用 language（zh-cn / zh-tw / en-us，其他写法按 <see cref="Normalize"/> 归一）。</summary>
    public static IDisposable Use(string? language)
    {
        var previous = Current.Value;
        Current.Value = Normalize(language);
        return new Scope(previous);
    }

    /// <summary>按当前语言选一条提示。</summary>
    public static string T(string simplified, string traditional, string english) => Language switch
    {
        TraditionalChinese => traditional,
        English => english,
        _ => simplified
    };

    /// <summary>与设置里的语言写法一致：zh-tw / zh-hk 为繁体，en 开头为英文，其余（含空）为简体。</summary>
    public static string Normalize(string? language)
    {
        var lower = language?.Trim().ToLowerInvariant() ?? "";
        if (lower.StartsWith("zh-tw", StringComparison.Ordinal) || lower.StartsWith("zh-hk", StringComparison.Ordinal)
            || lower is "tw" or "zhtw" or "zh-hant")
            return TraditionalChinese;
        if (lower.StartsWith("en", StringComparison.Ordinal))
            return English;
        return SimplifiedChinese;
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Current.Value = previous;
        }
    }
}
