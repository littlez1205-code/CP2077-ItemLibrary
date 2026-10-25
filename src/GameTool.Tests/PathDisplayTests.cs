using System.Text.Json;
using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

/// <summary>D021：界面上不显示含 Windows 用户名的路径。</summary>
public class PathDisplayTests : IDisposable
{
    private const string Profile = @"C:\Users\littl";
    private const string Local = @"C:\Users\littl\AppData\Local";
    private static readonly PathDisplay Display = new(Profile, Local);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-display-" + Guid.NewGuid().ToString("N"));

    public PathDisplayTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---------- 缩写规则 ----------

    [Theory]
    [InlineData(@"C:\Users\littl\Saved Games\CD Projekt Red\Cyberpunk 2077", @"%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077")]
    [InlineData(@"C:\Users\littl", "%USERPROFILE%")]
    [InlineData(@"C:\Users\littl\", @"%USERPROFILE%\")]
    [InlineData(@"C:\Users\littl\AppData\Local\GameTool\settings.json", @"%LOCALAPPDATA%\GameTool\settings.json")]
    [InlineData(@"C:\Users\littl\AppData\Local", "%LOCALAPPDATA%")]
    [InlineData(@"C:\Users\littl\AppData\Roaming\x", @"%USERPROFILE%\AppData\Roaming\x")]
    public void Mask_ShortensProfileAndLocalAppData_LongestFirst(string path, string expected)
    {
        Assert.Equal(expected, Display.Mask(path));
    }

    [Theory]
    [InlineData(@"c:\users\LITTL\saved games", @"%USERPROFILE%\saved games")]
    [InlineData(@"C:\USERS\Littl\APPDATA\local\GameTool", @"%LOCALAPPDATA%\GameTool")]
    [InlineData("C:/Users/littl/Saved Games", "%USERPROFILE%/Saved Games")]
    public void Mask_IgnoresCase_AndSeparatorStyle(string path, string expected)
    {
        Assert.Equal(expected, Display.Mask(path));
    }

    [Theory]
    [InlineData(@"D:\Games\Cyberpunk 2077")]
    [InlineData(@"E:\Users\littl\Saved Games")]
    [InlineData(@"C:\Users\littlefoot\Saved Games")]
    [InlineData(@"C:\Users\littl2")]
    [InlineData(@"C:\Users\littl.old\x")]
    [InlineData(@"C:\Users\littl_backup")]
    [InlineData(@"C:\Users\littl-x")]
    [InlineData(@"C:\Users\littl\AppData\LocalLow\x")]
    [InlineData("")]
    [InlineData("没有路径的普通提示。")]
    public void Mask_LeavesOtherPathsAndPartialSegmentsAlone(string text)
    {
        var shown = Display.Mask(text);
        // LocalLow 不能被当成 Local，但它仍在用户目录里，所以只缩写用户目录那一段。
        if (text.Contains("LocalLow", StringComparison.Ordinal))
            Assert.Equal(@"%USERPROFILE%\AppData\LocalLow\x", shown);
        else
            Assert.Same(text, shown);
    }

    [Fact]
    public void Mask_ReplacesEveryOccurrenceInsideMessages()
    {
        Assert.Equal("目录不存在：%USERPROFILE%\\nope", Display.Mask("目录不存在：C:\\Users\\littl\\nope"));
        Assert.Equal("设置保存在 %LOCALAPPDATA%\\GameTool\\settings.json", Display.Mask("设置保存在 C:\\Users\\littl\\AppData\\Local\\GameTool\\settings.json"));
        Assert.Equal(
            "Could not find a part of the path '%USERPROFILE%\\a'. Also %LOCALAPPDATA%\\b.",
            Display.Mask("Could not find a part of the path 'C:\\Users\\littl\\a'. Also C:\\Users\\littl\\AppData\\Local\\b."));
        Assert.Equal("已改用上一级目录（%USERPROFILE%）。", Display.Mask("已改用上一级目录（C:\\Users\\littl）。"));
        Assert.Equal(@"\\?\%USERPROFILE%\x", Display.Mask(@"\\?\C:\Users\littl\x"));
        Assert.Equal(@"xC:\Users\littl\x", Display.Mask(@"xC:\Users\littl\x"));
    }

    [Fact]
    public void Mask_SkipsMissingOrDriveRootDirectories()
    {
        var none = new PathDisplay(null, "  ");
        Assert.Equal(@"C:\Users\littl\x", none.Mask(@"C:\Users\littl\x"));
        var root = new PathDisplay(@"C:\", null);
        Assert.Equal(@"C:\Games", root.Mask(@"C:\Games"));
        var trailing = new PathDisplay(@"C:\Users\littl\", null);
        Assert.Equal(@"%USERPROFILE%\x", trailing.Mask(@"C:\Users\littl\x"));
    }

    [Theory]
    [InlineData(@"%USERPROFILE%\Saved Games", @"C:\Users\littl\Saved Games")]
    [InlineData(@"%userprofile%\Saved Games", @"C:\Users\littl\Saved Games")]
    [InlineData("%USERPROFILE%", @"C:\Users\littl")]
    [InlineData(@"%LOCALAPPDATA%\GameTool", @"C:\Users\littl\AppData\Local\GameTool")]
    [InlineData(@"D:\Games", @"D:\Games")]
    [InlineData(@"%USERPROFILE%x\a", @"%USERPROFILE%x\a")]
    [InlineData(@"D:\%USERPROFILE%\a", @"D:\%USERPROFILE%\a")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void Expand_RestoresOnlyLeadingTokens(string? shown, string? expected)
    {
        Assert.Equal(expected, Display.Expand(shown));
    }

    [Fact]
    public void Expand_RoundTripsMaskedPaths()
    {
        foreach (var path in new[] { @"C:\Users\littl\Saved Games\CD Projekt Red\Cyberpunk 2077", @"C:\Users\littl\AppData\Local\Temp\x\sav.dat", @"F:\SteamLibrary" })
            Assert.Equal(path, Display.Expand(Display.Mask(path)));
    }

    // ---------- 桥接：页面拿到的应答里不出现用户目录 ----------

    [Fact]
    public async Task Bridge_NeverSendsTheProfilePath_ToThePage()
    {
        // 假的用户目录：存档、设置文件、备份都放在里面，用名字 SecretUser 检查是否泄露。
        var profile = Path.Combine(_root, "Users", "SecretUser");
        var local = Path.Combine(profile, "AppData", "Local");
        var saves = Path.Combine(profile, "Saved Games", "Fake");
        var nested = Path.Combine(saves, "ManualSave-0");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "save.json"), """{"gold":1}""");
        var display = new PathDisplay(profile, local);
        var settings = new SettingsStore(Path.Combine(local, "GameTool", "settings.json"));
        var pushed = new List<string>();
        var registry = new AdapterRegistry();
        registry.Register(new FakeJsonGameAdapter(saves, FakeJsonGameAdapter.Catalog(("gold", 100))));
        var responses = new List<string>();
        await using (var bridge = new BridgeDispatcher(
            registry,
            null,
            message =>
            {
                lock (pushed)
                    pushed.Add(message);
            },
            Path.Combine(local, "GameTool", "backups"),
            Path.Combine(local, "GameTool", "skins"),
            settings,
            null,
            (_, _) => nested,
            display))
        {
            string Call(string method, string extra)
            {
                var response = bridge.Dispatch("{\"id\":\"t\",\"method\":\"" + method + "\",\"params\":{\"gameId\":\"fake\"" + (extra.Length == 0 ? "" : "," + extra) + "}}");
                responses.Add(response);
                return response;
            }

            // 还没设置：错误信息、设置文件位置。
            Call("saves.list", "");
            var initial = Data(Call("settings.get", ""));
            Assert.Equal(@"%LOCALAPPDATA%\GameTool\settings.json", initial.GetProperty("file").GetString());

            // 校验：不存在的目录、好的目录；选择文件夹（选到单个存档里也照样只显示缩写）。
            var bad = Data(Call("settings.validate", "\"kind\":\"saveRoot\",\"path\":" + Js(Path.Combine(saves, "nope"))));
            Assert.Equal(@"目录不存在：%USERPROFILE%\Saved Games\Fake\nope", bad.GetProperty("message").GetString());
            var good = Data(Call("settings.validate", "\"kind\":\"saveRoot\",\"path\":" + Js(saves)));
            Assert.Equal(@"%USERPROFILE%\Saved Games\Fake", good.GetProperty("path").GetString());
            var picked = Data(Call("settings.pickFolder", "\"kind\":\"saveRoot\""));
            Assert.Equal(@"%USERPROFILE%\Saved Games\Fake\ManualSave-0", picked.GetProperty("picked").GetString());

            // 页面把缩写路径原样送回来保存：还原成完整路径存进设置文件，应答里仍是缩写。
            var saved = Data(Call("settings.save", "\"saveRoot\":" + Js(good.GetProperty("path").GetString()) + ",\"gameDir\":null"));
            Assert.True(saved.GetProperty("ready").GetBoolean());
            Assert.Equal(@"%USERPROFILE%\Saved Games\Fake", saved.GetProperty("saveRoot").GetString());
            Assert.Equal(saves, settings.Get("fake").SaveRoot);

            // 槽位标识是缩写后的路径，送回来能用；完整路径也照旧能用。
            var slot = Data(Call("saves.list", "")).EnumerateArray().Single(row => row.GetProperty("name").GetString() == "ManualSave-0").GetProperty("slot").GetString()!;
            Assert.Equal(@"%USERPROFILE%\Saved Games\Fake\ManualSave-0\save.json", slot);
            Assert.Equal(1, Data(Call("saves.info", "\"slot\":" + Js(slot))).GetProperty("resources")[0].GetProperty("quantity").GetInt64());
            Assert.Equal(1, Data(Call("saves.info", "\"slot\":" + Js(Path.Combine(nested, "save.json")))).GetProperty("resources")[0].GetProperty("quantity").GetInt64());

            // 缩写不能拿来绕过槽位检查：用户目录里别的文件、穿越都会被拒绝。
            var outside = Path.Combine(profile, "other.json");
            await File.WriteAllTextAsync(outside, """{"gold":1}""");
            Assert.Equal("bad_args", Code(Call("saves.info", "\"slot\":" + Js(@"%USERPROFILE%\other.json"))));
            Assert.Equal("bad_args", Code(Call("saves.info", "\"slot\":" + Js(@"%USERPROFILE%\Saved Games\Fake\..\..\other.json"))));

            // 备份、使能状态与推送。
            Call("backup.add", "\"slot\":" + Js(slot));
            var backups = Data(Call("backup.list", ""));
            var backupId = backups[0].GetProperty("id").GetString();
            Call("backup.info", "\"backupId\":" + Js(backupId));
            var started = Data(Call("enable.start", "\"slot\":" + Js(slot) + ",\"changes\":[{\"id\":\"gold\",\"value\":40}]"));
            Assert.Equal(slot, started.GetProperty("slot").GetString());
            Call("enable.status", "");
            await File.WriteAllTextAsync(Path.Combine(nested, "save.json"), """{"gold":4}""");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (pushed)
                {
                    if (pushed.Count > 0)
                        break;
                }

                await Task.Delay(30);
            }

            Call("enable.stop", "");
            Call("backup.restore", "\"backupId\":" + Js(backupId));
            Call("backup.delete", "\"backupId\":" + Js("fake/" + "missing"));
        }

        List<string> all;
        lock (pushed)
            all = [.. responses, .. pushed];
        Assert.NotEmpty(pushed);
        foreach (var json in all)
        {
            Assert.DoesNotContain("SecretUser", json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            foreach (var text in Strings(document.RootElement))
            {
                Assert.DoesNotContain("SecretUser", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var text in Strings(property.Value))
                        yield return text;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var text in Strings(item))
                        yield return text;
                }

                break;
        }
    }

    private static JsonElement Data(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static string? Code(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private static string Js(string? value) => JsonSerializer.Serialize(value);
}
