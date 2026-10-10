using System.Text;

namespace GameTool.App;

/// <summary>
/// 界面显示用的路径缩写（D021）：用户目录下的路径显示成 <c>%USERPROFILE%\…</c>，本地应用数据目录下的显示成
/// <c>%LOCALAPPDATA%\…</c>，界面上就不会出现 Windows 用户名。只影响显示：设置文件和内部使用的仍是完整路径。
/// <para>
/// <see cref="Mask"/> 处理任意文本（路径本身、校验提示、异常信息），在文本中任何位置找到这些目录就替换；
/// 先比较较长的目录（本地应用数据目录在用户目录里面），不区分大小写，<c>\</c> 与 <c>/</c> 视为相同。
/// 目录名必须完整匹配：<c>C:\Users\playerone</c> 不会被当成 <c>C:\Users\player</c>。
/// </para>
/// <para><see cref="Expand"/> 把页面送回来的缩写路径还原成完整路径（只认开头的缩写）。</para>
/// </summary>
public sealed class PathDisplay
{
    public const string UserProfileToken = "%USERPROFILE%";
    public const string LocalAppDataToken = "%LOCALAPPDATA%";

    private readonly (string Prefix, string Token)[] _rules;

    /// <param name="userProfile">用户目录；null 或空表示不缩写。</param>
    /// <param name="localAppData">本地应用数据目录；null 或空表示不缩写。</param>
    public PathDisplay(string? userProfile, string? localAppData)
    {
        var rules = new List<(string Prefix, string Token)>();
        AddRule(rules, localAppData, LocalAppDataToken);
        AddRule(rules, userProfile, UserProfileToken);
        _rules = rules.OrderByDescending(rule => rule.Prefix.Length).ToArray();
    }

    /// <summary>当前用户的目录（%USERPROFILE%、%LOCALAPPDATA%）。</summary>
    public static PathDisplay Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>把文本里出现的用户目录、本地应用数据目录换成缩写；没有出现时原样返回同一个字符串。</summary>
    public string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_rules.Length == 0 || text.Length == 0)
            return text;
        StringBuilder? builder = null;
        var copied = 0;
        var index = 0;
        while (index < text.Length)
        {
            var rule = MatchAt(text, index);
            if (rule is null)
            {
                index++;
                continue;
            }

            builder ??= new StringBuilder(text.Length);
            builder.Append(text, copied, index - copied).Append(rule.Value.Token);
            index += rule.Value.Prefix.Length;
            copied = index;
        }

        return builder is null ? text : builder.Append(text, copied, text.Length - copied).ToString();
    }

    /// <summary>null 安全的 <see cref="Mask"/>。</summary>
    public string? MaskOrNull(string? text) => text is null ? null : Mask(text);

    /// <summary>页面送回的路径：开头是 %USERPROFILE% / %LOCALAPPDATA% 缩写时还原成完整路径，其余原样返回。</summary>
    public string? Expand(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var trimmed = path.TrimStart();
        foreach (var token in new[] { LocalAppDataToken, UserProfileToken })
        {
            if (!trimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.Length > token.Length && !IsSeparator(trimmed[token.Length]))
                continue;
            var rule = _rules.FirstOrDefault(item => item.Token == token);
            return rule.Prefix is null ? path : rule.Prefix + trimmed[token.Length..];
        }

        return path;
    }

    private (string Prefix, string Token)? MatchAt(string text, int index)
    {
        // 左边界：前一个字符是英文字母或数字时，这里不是一个路径的开头（如 xC:\Users\…）；中文紧挨着路径不影响。
        if (index > 0 && char.IsAsciiLetterOrDigit(text[index - 1]))
            return null;
        foreach (var rule in _rules)
        {
            var prefix = rule.Prefix;
            if (text.Length - index < prefix.Length)
                continue;
            var same = true;
            for (var offset = 0; offset < prefix.Length && same; offset++)
                same = SameChar(text[index + offset], prefix[offset]);
            if (same && IsBoundary(text, index + prefix.Length))
                return rule;
        }

        return null;
    }

    private static bool SameChar(char left, char right)
    {
        if (IsSeparator(left) && IsSeparator(right))
            return true;
        return left == right || char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
    }

    private static bool IsSeparator(char value) => value is '\\' or '/';

    /// <summary>
    /// 目录名到这里结束：文本结束、路径分隔符、文件名里不能出现的字符、空白，或紧跟在路径后面的常见标点。
    /// 字母、数字以及 .-_ 等能出现在文件名里的字符都不算结束，避免把 playerone 当成 player。
    /// </summary>
    private static bool IsBoundary(string text, int index)
    {
        if (index >= text.Length)
            return true;
        var next = text[index];
        return IsSeparator(next)
            || char.IsWhiteSpace(next)
            || next is '"' or '\'' or '<' or '>' or '|' or '*' or '?' or ':' or ',' or ';' or ')' or ']'
            || next is '。' or '，' or '；' or '：' or '、' or '）' or '」' or '』' or '》' or '“' or '”';
    }

    private static void AddRule(List<(string Prefix, string Token)> rules, string? directory, string token)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        string full;
        try
        {
            full = Path.GetFullPath(directory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        full = full.TrimEnd('\\', '/');
        // 盘符根目录（如 C:）不缩写，否则整个盘都会被换掉。
        if (full.Length <= 3)
            return;
        rules.Add((full, token));
    }
}
