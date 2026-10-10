using System.Diagnostics;

namespace GameTool.App;

/// <summary>
/// 作者署名与 B 站主页（D024）。地址只写在这里，页面只能请求「打开作者主页」，不能让宿主打开任意地址。
/// </summary>
public static class AuthorPage
{
    public const string AuthorName = "魔法苦茶籽";

    public const string Url = "https://space.bilibili.com/3546366456367644";
}

/// <summary>用系统默认浏览器打开网址。生产环境用 <see cref="ShellUrlLauncher"/>；单元测试和自检注入假实现，绝不真的打开浏览器。</summary>
public interface IUrlLauncher
{
    void Open(string url);
}

/// <summary>交给系统外壳打开（UseShellExecute），即用户的默认浏览器。</summary>
public sealed class ShellUrlLauncher : IUrlLauncher
{
    public void Open(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
