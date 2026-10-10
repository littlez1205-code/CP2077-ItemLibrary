using System.Reflection;

namespace GameTool.App;

/// <summary>
/// 程序版本（D022）：取入口程序集的 InformationalVersion（src/Directory.Build.props 统一设置），
/// 去掉 SDK 附加的「+提交号」后缀，如 <c>1.0.0+abc123</c> → <c>1.0.0</c>。
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Of(Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly);

    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var cleaned = Clean(informational);
        if (cleaned.Length > 0)
            return cleaned;
        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }

    public static string Clean(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return "";
        var text = informational.Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return (plus >= 0 ? text[..plus] : text).Trim();
    }
}
