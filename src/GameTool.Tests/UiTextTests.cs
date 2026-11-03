using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>面向用户的提示按界面语言给出（简体 / 繁體 / English）。</summary>
public class UiTextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-uitext-" + Guid.NewGuid().ToString("N"));

    public UiTextTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scope_DefaultsToSimplified_NestsAndRestores()
    {
        Assert.Equal("简", UiText.T("简", "繁", "en"));
        using (UiText.Use("en"))
        {
            Assert.Equal("en", UiText.T("简", "繁", "en"));
            using (UiText.Use("zh-TW"))
                Assert.Equal("繁", UiText.T("简", "繁", "en"));
            Assert.Equal("en", UiText.T("简", "繁", "en"));
        }

        Assert.Equal("简", UiText.T("简", "繁", "en"));
        Assert.Equal(UiText.TraditionalChinese, UiText.Normalize("zh-hk"));
        Assert.Equal(UiText.English, UiText.Normalize("en-US"));
        Assert.Equal(UiText.SimplifiedChinese, UiText.Normalize(null));
        Assert.Equal(UiText.SimplifiedChinese, UiText.Normalize("fr"));
    }

    [Fact]
    public async Task Scope_FlowsIntoTasksStartedInside_AndDoesNotLeakOut()
    {
        Task<string> inner;
        using (UiText.Use("en-us"))
            inner = Task.Run(async () =>
            {
                await Task.Yield();
                return UiText.T("简", "繁", "en");
            });
        Assert.Equal("en", await inner);
        Assert.Equal("简", UiText.T("简", "繁", "en"));
    }

    [Fact]
    public async Task Bridge_ErrorsFollowTheInterfaceLanguage()
    {
        var registry = new AdapterRegistry();
        registry.Register(new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null)));
        await using var bridge = new BridgeDispatcher(registry, Path.Combine(_root, "saves"), _ => { }, Path.Combine(_root, "backups"), Path.Combine(_root, "skins"));
        string Error(string json)
        {
            using var response = JsonDocument.Parse(bridge.Dispatch(json));
            return response.RootElement.GetProperty("error").GetProperty("message").GetString()!;
        }

        string Validate()
        {
            using var response = JsonDocument.Parse(bridge.Dispatch(
                "{\"id\":\"v\",\"method\":\"settings.validate\",\"params\":{\"gameId\":\"cp2077\",\"kind\":\"saveRoot\",\"path\":\"\"}}"));
            return response.RootElement.GetProperty("data").GetProperty("message").GetString()!;
        }

        void Language(string language) =>
            bridge.Dispatch("{\"id\":\"l\",\"method\":\"app.setLanguage\",\"params\":{\"language\":\"" + language + "\"}}");

        const string unknown = "{\"id\":\"1\",\"method\":\"no.such\"}";
        Assert.Equal("未知方法 no.such。", Error(unknown));
        Assert.Equal("请选择存档目录。", Validate());
        Language("zh-tw");
        Assert.Equal("請選擇存檔目錄。", Validate());
        Language("en");
        Assert.Equal("Unknown method no.such.", Error(unknown));
        Assert.Equal("Please choose the save folder.", Validate());

        // 作用域只在请求内：请求结束后，这个线程上的提示仍是默认的简体中文。
        Assert.Equal("简", UiText.T("简", "繁", "en"));
    }

    [Fact]
    public void PathChecks_FollowTheLanguage()
    {
        Assert.Equal("请选择存档目录。", Cp2077Paths.CheckSaveRoot(null).Message);
        using (UiText.Use("zh-tw"))
            Assert.Equal("請選擇存檔目錄。", Cp2077Paths.CheckSaveRoot(null).Message);
        using (UiText.Use("en"))
        {
            Assert.Equal("Please choose the save folder.", Cp2077Paths.CheckSaveRoot(null).Message);
            Assert.Equal("Please choose the game folder.", Cp2077Paths.CheckGameDirectory(null).Message);
            var missing = Path.Combine(_root, "missing");
            Assert.Equal("The folder does not exist: " + Path.GetFullPath(missing), Cp2077Paths.CheckSaveRoot(missing).Message);
        }
    }

    /// <summary>
    /// 防漏：生产代码里直接 throw 中文字面量（没有包在 UiText.T 里）的提示会被列出来。
    /// 只给开发者看的检查（内嵌资源、参数范围、命令行研究工具）在白名单里。
    /// </summary>
    [Fact]
    public void ThrownMessages_AreLocalized()
    {
        var src = SourceRoot();
        var direct = new Regex(@"throw new \w*Exception\(\$?@?""[^""]*\p{IsCJKUnifiedIdeographs}");
        string[] skippedFiles = ["ResearchService.cs", "InventoryResearch.cs", "ResourceCatalog.cs", "ThemeCatalog.cs", "SaveInfo.cs", "Program.cs"];
        string[] developerOnly = ["防抖毫秒的安全范围", "缺少内嵌资源", "的 locator 必须是 tdb:记录名", "与已有资源重复", "输出目录", "无法做完整自检"];
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file);
            if (relative.Contains("GameTool.Tests", StringComparison.Ordinal) || relative.Contains("GameTool.Cli", StringComparison.Ordinal)
                || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || skippedFiles.Contains(Path.GetFileName(file)))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (direct.IsMatch(lines[i]) && !developerOnly.Any(lines[i].Contains))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.Empty(offenders);
    }

    private static string SourceRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
