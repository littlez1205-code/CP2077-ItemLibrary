using System.Text.RegularExpressions;

namespace GameTool.Tests;

/// <summary>
/// 界面文字表（Desktop/wwwroot/i18n.js）：三种语言的键必须一致，说明对话框三个页签的结构（问答条数、列表项、小标题）也必须一致，
/// 免得某种语言漏翻、少一条说明。
/// </summary>
public sealed partial class FrontendTextTests
{
    private static readonly string[] Languages = ["zh-cn", "zh-tw", "en-us"];

    private static readonly string Source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "i18n.js"));

    [Fact]
    public void EveryLanguage_DefinesTheSameKeys()
    {
        var table = Section("const I18N = {");
        var keys = Languages.ToDictionary(language => language, language =>
            KeyPattern().Matches(Block(table, language)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal));
        Assert.True(keys["zh-cn"].Count > 100, "没有解析出 zh-cn 的键。");
        foreach (var language in Languages)
        {
            Assert.Empty(keys["zh-cn"].Except(keys[language]));
            Assert.Empty(keys[language].Except(keys["zh-cn"]));
        }
    }

    [Fact]
    public void HelpPanes_HaveTheSameStructureInEveryLanguage()
    {
        var help = Section("const HELP_PANES_HTML = {");
        var shapes = Languages.ToDictionary(language => language, language => Shape(Block(help, language)));
        Assert.Equal(12, shapes["zh-cn"]["faq"].Questions);
        foreach (var language in Languages)
            Assert.Equal(shapes["zh-cn"], shapes[language]);
    }

    private readonly record struct PaneShape(int Questions, int Answers, int ListItems, int Headings, int Lists);

    private static Dictionary<string, PaneShape> Shape(string languageBlock)
    {
        var shapes = new Dictionary<string, PaneShape>(StringComparer.Ordinal);
        foreach (Match pane in PanePattern().Matches(languageBlock))
        {
            var html = pane.Groups[2].Value;
            shapes[pane.Groups[1].Value] = new PaneShape(
                Count(html, "<dt>"), Count(html, "<dd>"), Count(html, "<li>"), Count(html, "<h4>"), Count(html, "<ul"));
        }

        Assert.Equal(["about", "faq", "start"], shapes.Keys.Order(StringComparer.Ordinal));
        return shapes;
    }

    private static int Count(string text, string tag) => Regex.Count(text, Regex.Escape(tag));

    /// <summary>从声明开始到第一个顶格的 "};" 为止。</summary>
    private static string Section(string declaration)
    {
        var start = Source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "i18n.js 里没有 " + declaration);
        var end = Source.IndexOf("\n};", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return Source[start..end];
    }

    /// <summary>一种语言的那一块：从 `  "zh-cn": {` 到下一个两格缩进的 `  }`。</summary>
    private static string Block(string section, string language)
    {
        var header = "\n  \"" + language + "\": {";
        var start = section.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少语言 " + language);
        var end = BlockEndPattern().Match(section, start + header.Length);
        return end.Success ? section[start..end.Index] : section[start..];
    }

    [GeneratedRegex(@"^    ([A-Za-z][A-Za-z0-9_]*):", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^    (start|faq|about): `(.*?)`", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PanePattern();

    [GeneratedRegex(@"^  \}", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockEndPattern();
}
