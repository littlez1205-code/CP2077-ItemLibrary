using System.Text.Json;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>D020：手动设置存档目录和游戏目录（设置文件、校验、桥接方法）。全部在临时目录里进行。</summary>
public class SettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---------- 设置文件 ----------

    [Fact]
    public void Store_RoundTrip_KeepsOtherGames_AndLeavesNoTempFile()
    {
        var file = Path.Combine(_root, "conf", "settings.json");
        var store = new SettingsStore(file);
        Assert.Equal(GamePaths.Empty, store.Get("cp2077"));

        store.Set("cp2077", new GamePaths(@"C:\Saves", @"D:\Games\Cyberpunk 2077"));
        store.Set("other", new GamePaths(@"E:\Other", null));
        store.Set("cp2077", new GamePaths(@"C:\Saves2", @"D:\Games\Cyberpunk 2077"));

        var reopened = new SettingsStore(file);
        Assert.Equal(new GamePaths(@"C:\Saves2", @"D:\Games\Cyberpunk 2077"), reopened.Get("cp2077"));
        Assert.Equal(new GamePaths(@"E:\Other", null), reopened.Get("other"));
        Assert.Equal(GamePaths.Empty, reopened.Get("unknown"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!));

        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var game = json.RootElement.GetProperty("games").GetProperty("cp2077");
        Assert.Equal(@"C:\Saves2", game.GetProperty("saveRoot").GetString());
        Assert.Equal(@"D:\Games\Cyberpunk 2077", game.GetProperty("gameDir").GetString());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("""{"games":5}""")]
    [InlineData("""{"games":{"cp2077":"x"}}""")]
    [InlineData("""{"games":{"cp2077":{"saveRoot":3}}}""")]
    public void Store_CorruptFile_IsTreatedAsUnset_AndCanBeOverwritten(string content)
    {
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, content);
        var store = new SettingsStore(file);
        Assert.Equal(GamePaths.Empty, store.Get("cp2077"));

        store.Set("cp2077", new GamePaths(@"C:\Saves", " "));
        Assert.Equal(new GamePaths(@"C:\Saves", null), new SettingsStore(file).Get("cp2077"));
    }

    // ---------- 「说明」已自动弹出的标记（D022） ----------

    [Fact]
    public void Store_HelpShown_RoundTrip_KeepsGamePaths()
    {
        var file = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(file);
        Assert.False(store.GetHelpShown());

        // 旧版写出的文件没有 helpShown：路径照常读出，标记当作未弹出。
        store.Set("cp2077", new GamePaths(@"C:\Saves", @"D:\Game"));
        Assert.DoesNotContain("helpShown", File.ReadAllText(file));
        Assert.False(new SettingsStore(file).GetHelpShown());

        store.SetHelpShown(true);
        var reopened = new SettingsStore(file);
        Assert.True(reopened.GetHelpShown());
        Assert.Equal(new GamePaths(@"C:\Saves", @"D:\Game"), reopened.Get("cp2077"));

        // 改路径不丢标记。
        reopened.Set("cp2077", new GamePaths(@"C:\Saves2", @"D:\Game"));
        Assert.True(new SettingsStore(file).GetHelpShown());
        using (var json = JsonDocument.Parse(File.ReadAllText(file)))
        {
            Assert.True(json.RootElement.GetProperty("helpShown").GetBoolean());
            Assert.Equal(@"C:\Saves2", json.RootElement.GetProperty("games").GetProperty("cp2077").GetProperty("saveRoot").GetString());
        }

        store.SetHelpShown(false);
        Assert.False(new SettingsStore(file).GetHelpShown());
        Assert.DoesNotContain("helpShown", File.ReadAllText(file));
        Assert.Equal(new GamePaths(@"C:\Saves2", @"D:\Game"), new SettingsStore(file).Get("cp2077"));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("""{"games":{"cp2077":{"saveRoot":"C:\\S"}},"helpShown":"yes"}""", false, true)]
    [InlineData("""{"games":{"cp2077":{"saveRoot":"C:\\S"}},"helpShown":1}""", false, true)]
    [InlineData("""{"games":{"cp2077":{"saveRoot":"C:\\S"}},"helpShown":false}""", false, true)]
    [InlineData("""{"games":{"cp2077":{"saveRoot":"C:\\S"}},"helpShown":true}""", true, true)]
    [InlineData("""{"games":5,"helpShown":true}""", true, false)]
    [InlineData("""{"helpShown":true}""", true, false)]
    [InlineData("""[true]""", false, false)]
    public void Store_HelpShown_IsReadIndependentlyOfGames(string content, bool helpShown, bool hasPaths)
    {
        var file = Path.Combine(_root, "settings.json");
        File.WriteAllText(file, content);
        var store = new SettingsStore(file);
        Assert.Equal(helpShown, store.GetHelpShown());
        Assert.Equal(hasPaths ? new GamePaths(@"C:\S", null) : GamePaths.Empty, store.Get("cp2077"));
    }

    [Fact]
    public void AppVersion_IsOneZeroZero_WithoutCommitSuffix()
    {
        Assert.Equal("1.0.2", AppVersion.Of(typeof(SettingsStore).Assembly));
        Assert.Equal("1.0.0", AppVersion.Clean("1.0.0+0123abcdef"));
        Assert.Equal("1.2.3-beta", AppVersion.Clean(" 1.2.3-beta "));
        Assert.Equal("", AppVersion.Clean(null));
        Assert.DoesNotContain("+", AppVersion.Current);
        Assert.NotEmpty(AppVersion.Current);
    }

    [Fact]
    public async Task Bridge_AppInfo_And_MarkHelpShown_PersistInSettings()
    {
        var file = Path.Combine(_root, "settings.json");
        var saves = MakeSaves("saves", "ManualSave-0");
        new SettingsStore(file).Set(Cp2077Adapter.Game, new GamePaths(saves, null));
        var registry = new AdapterRegistry();
        registry.Register(new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null)));
        await using (var bridge = new BridgeDispatcher(registry, null, _ => { }, Path.Combine(_root, "b"), Path.Combine(_root, "k"), new SettingsStore(file), appVersion: "9.8.7"))
        {
            var info = Data(bridge, "app.info", "");
            Assert.Equal("9.8.7", info.GetProperty("version").GetString());
            Assert.False(info.GetProperty("helpShown").GetBoolean());
            Assert.True(Data(bridge, "app.markHelpShown", "").GetProperty("helpShown").GetBoolean());
            Assert.True(Data(bridge, "app.info", "").GetProperty("helpShown").GetBoolean());
        }

        Assert.True(new SettingsStore(file).GetHelpShown());
        Assert.Equal(new GamePaths(saves, null), new SettingsStore(file).Get(Cp2077Adapter.Game));
        await using (var again = new BridgeDispatcher(registry, null, _ => { }, Path.Combine(_root, "b"), Path.Combine(_root, "k"), new SettingsStore(file)))
        {
            var info = Data(again, "app.info", "");
            Assert.True(info.GetProperty("helpShown").GetBoolean());
            Assert.Equal(AppVersion.Current, info.GetProperty("version").GetString());
        }

        // 没有设置文件（命令行等）：标记只记在内存里。
        await using var memory = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "b"), Path.Combine(_root, "k"));
        Assert.False(Data(memory, "app.info", "").GetProperty("helpShown").GetBoolean());
        Assert.True(Data(memory, "app.markHelpShown", "").GetProperty("helpShown").GetBoolean());
        Assert.True(Data(memory, "app.info", "").GetProperty("helpShown").GetBoolean());
    }

    [Fact]
    public void Store_DefaultPath_IsUnderLocalAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameTool", "settings.json");
        Assert.Equal(expected, SettingsStore.DefaultFilePath());
        Assert.Throws<ArgumentException>(() => new SettingsStore(Path.Combine(_root, "s.json")).Get(" "));
    }

    // ---------- 存档目录校验 ----------

    [Fact]
    public void SaveRoot_AcceptsRoot_WithSlots()
    {
        var saves = MakeSaves("saves", "ManualSave-0", "AutoSave-3", "NotASlot");
        var check = Cp2077Paths.CheckSaveRoot(saves + Path.DirectorySeparatorChar);
        Assert.True(check.Ok);
        Assert.Equal(saves, check.Path);
        Assert.Contains("2 个存档", check.Message);
    }

    [Fact]
    public void SaveRoot_SingleSlot_UsesParent()
    {
        var saves = MakeSaves("saves", "ManualSave-0", "QuickSave-1");
        var check = Cp2077Paths.CheckSaveRoot(Path.Combine(saves, "ManualSave-0"));
        Assert.True(check.Ok);
        Assert.Equal(saves, check.Path);
        Assert.Contains("上一级", check.Message);
    }

    [Fact]
    public void SaveRoot_RejectsEmptyMissingAndRelative()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(Path.Combine(empty, "ManualSave-0"));
        var emptyCheck = Cp2077Paths.CheckSaveRoot(empty);
        Assert.False(emptyCheck.Ok);
        Assert.Null(emptyCheck.Path);
        Assert.Contains("没有找到存档", emptyCheck.Message);

        var missing = Cp2077Paths.CheckSaveRoot(Path.Combine(_root, "nope"));
        Assert.False(missing.Ok);
        Assert.Contains("不存在", missing.Message);

        Assert.Contains("请选择", Cp2077Paths.CheckSaveRoot(null).Message);
        Assert.False(Cp2077Paths.CheckSaveRoot(@"relative\saves").Ok);

        // 单个存档文件夹，但上一级不是存档目录。
        var lone = Path.Combine(_root, "lone", "MySlot");
        Directory.CreateDirectory(lone);
        File.WriteAllBytes(Path.Combine(lone, "sav.dat"), [1]);
        Assert.False(Cp2077Paths.CheckSaveRoot(lone).Ok);
    }

    // ---------- 游戏目录校验 ----------

    [Fact]
    public void GameDir_AcceptsRoot_WithoutWarning()
    {
        var game = MakeGame("Cyberpunk 2077", redmod: true);
        var check = Cp2077Paths.CheckGameDirectory(game);
        Assert.True(check.Ok);
        Assert.Equal(game, check.Path);
        Assert.Null(check.Warning);
    }

    [Fact]
    public void GameDir_Subfolder_WalksUpToRoot()
    {
        var game = MakeGame("Cyberpunk 2077", redmod: true);
        var check = Cp2077Paths.CheckGameDirectory(Path.Combine(game, "bin", "x64"));
        Assert.True(check.Ok);
        Assert.Equal(game, check.Path);
        Assert.Contains("根目录", check.Message);

        // 选到上一层（如 steamapps\common）也能找到。
        var parent = Cp2077Paths.CheckGameDirectory(Path.GetDirectoryName(game));
        Assert.True(parent.Ok);
        Assert.Equal(game, parent.Path);
    }

    [Fact]
    public void GameDir_RejectsMissingExeOrContent()
    {
        var noExe = Path.Combine(_root, "noexe");
        Directory.CreateDirectory(Path.Combine(noExe, "archive", "pc", "content"));
        var check = Cp2077Paths.CheckGameDirectory(noExe);
        Assert.False(check.Ok);
        Assert.Contains("Cyberpunk2077.exe", check.Message);

        var noContent = Path.Combine(_root, "nocontent");
        Directory.CreateDirectory(Path.Combine(noContent, "bin", "x64"));
        File.WriteAllBytes(Path.Combine(noContent, "bin", "x64", "Cyberpunk2077.exe"), [0]);
        var content = Cp2077Paths.CheckGameDirectory(noContent);
        Assert.False(content.Ok);
        Assert.Contains(@"archive\pc\content", content.Message);

        Assert.False(Cp2077Paths.CheckGameDirectory(Path.Combine(_root, "missing")).Ok);
        Assert.False(Cp2077Paths.CheckGameDirectory("").Ok);
    }

    [Fact]
    public void GameDir_WithoutRedmod_IsAcceptedWithWarning()
    {
        var game = MakeGame("Cyberpunk 2077", redmod: false);
        var check = Cp2077Paths.CheckGameDirectory(game);
        Assert.True(check.Ok);
        Assert.Equal(game, check.Path);
        Assert.NotNull(check.Warning);
        Assert.Contains("REDmod", check.Warning);
        Assert.Contains(ItemNameCatalog.RedmodUnavailable, check.Warning);
    }

    [Fact]
    public void ItemNames_ForGame_RebuildsWhenDirectoryChanges()
    {
        var first = MakeGame("A", redmod: false);
        var second = MakeGame("B", redmod: false);
        var a = Cp2077ItemNames.ForGame(first);
        Assert.Same(a, Cp2077ItemNames.ForGame(first));
        Assert.Equal(first, a.GameDirectory);
        Assert.False(a.Available);
        var b = Cp2077ItemNames.ForGame(second);
        Assert.NotSame(a, b);
        Assert.Equal(second, b.GameDirectory);
        Assert.Null(Cp2077ItemNames.ForGame(null).GameDirectory);
    }

    [Fact]
    public void Adapter_UsesConfiguredGameDirectory_SharedWithResourceCopies()
    {
        var adapter = new Cp2077Adapter();
        Assert.Null(adapter.GameDirectory);
        var game = MakeGame("Cyberpunk 2077", redmod: false);
        adapter.UseGameDirectory(game);
        var copy = (Cp2077Adapter)adapter.WithResources([]);
        Assert.Equal(game, adapter.GameDirectory);
        Assert.Equal(game, copy.GameDirectory);
        Assert.Null(adapter.IconVersion);
        adapter.UseGameDirectory(" ");
        Assert.Null(copy.GameDirectory);
    }

    // ---------- 桥接 ----------

    [Fact]
    public async Task Bridge_FirstLaunch_IsNotReady_AndSaveAppliesPaths()
    {
        var file = Path.Combine(_root, "settings.json");
        var adapter = new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null));
        await using var bridge = Create(adapter, new SettingsStore(file), null, null, (_, _) => null);

        var initial = Data(bridge, "settings.get", "");
        Assert.False(initial.GetProperty("ready").GetBoolean());
        Assert.False(initial.TryGetProperty("saveRoot", out _));
        // D021：界面上的路径都把用户目录缩写成 %USERPROFILE% / %LOCALAPPDATA%。
        Assert.Equal(Shown(Cp2077Paths.DefaultSaveRoot), initial.GetProperty("saveRootHint").GetString());
        Assert.StartsWith(PathDisplay.UserProfileToken, initial.GetProperty("saveRootHint").GetString());
        Assert.Contains("steamapps", initial.GetProperty("gameDirHint").GetString());
        Assert.Equal(Shown(file), initial.GetProperty("file").GetString());
        Assert.False(File.Exists(file));

        // 没设置前列存档会给出明确提示，而不是去读默认位置。
        var list = Error(bridge, "saves.list", "");
        Assert.Contains("存档目录", list);

        var saves = MakeSaves("saves", "ManualSave-0");
        var game = MakeGame("Cyberpunk 2077", redmod: false);
        Assert.Contains("游戏目录", Error(bridge, "settings.save", "\"saveRoot\":" + Js(saves) + ",\"gameDir\":" + Js(_root)));
        Assert.Contains("存档目录", Error(bridge, "settings.save", "\"saveRoot\":" + Js(_root) + ",\"gameDir\":" + Js(game)));
        Assert.False(File.Exists(file));

        var saved = Data(bridge, "settings.save", "\"saveRoot\":" + Js(Path.Combine(saves, "ManualSave-0")) + ",\"gameDir\":" + Js(Path.Combine(game, "bin", "x64")));
        Assert.True(saved.GetProperty("ready").GetBoolean());
        Assert.Equal(Shown(saves), saved.GetProperty("saveRoot").GetString());
        Assert.Equal(Shown(game), saved.GetProperty("gameDir").GetString());
        Assert.Contains("REDmod", saved.GetProperty("gameDirCheck").GetProperty("warning").GetString());
        Assert.Equal(new GamePaths(saves, game), new SettingsStore(file).Get(Cp2077Adapter.Game));
        Assert.Equal(game, adapter.GameDirectory);

        var slots = Data(bridge, "saves.list", "");
        Assert.Equal("ManualSave-0", slots[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Bridge_ReadsStoredSettings_AndOverridesWin()
    {
        var file = Path.Combine(_root, "settings.json");
        var saves = MakeSaves("saves", "ManualSave-0");
        var other = MakeSaves("other", "ManualSave-1", "ManualSave-2");
        var game = MakeGame("Cyberpunk 2077", redmod: true);
        new SettingsStore(file).Set(Cp2077Adapter.Game, new GamePaths(saves, game));

        var stored = new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null));
        await using (var bridge = Create(stored, new SettingsStore(file), null, null, null))
        {
            var data = Data(bridge, "settings.get", "");
            Assert.True(data.GetProperty("ready").GetBoolean());
            Assert.Equal(game, stored.GameDirectory);
            Assert.Single(Data(bridge, "saves.list", "").EnumerateArray());
            Assert.Contains("选择文件夹", Error(bridge, "settings.pickFolder", "\"kind\":\"saveRoot\""));
        }

        var overridden = new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null));
        var otherGame = MakeGame("Other 2077", redmod: true);
        await using (var bridge = Create(overridden, new SettingsStore(file), other, otherGame, null))
        {
            Assert.Equal(2, Data(bridge, "saves.list", "").GetArrayLength());
            Assert.Equal(otherGame, overridden.GameDirectory);
        }

        // 覆盖不写回设置文件。
        Assert.Equal(new GamePaths(saves, game), new SettingsStore(file).Get(Cp2077Adapter.Game));
    }

    [Fact]
    public async Task Bridge_Validate_And_PickFolder()
    {
        var saves = MakeSaves("saves", "ManualSave-0", "AutoSave-0");
        var game = MakeGame("Cyberpunk 2077", redmod: true);
        var asked = new List<(string Title, string? Initial)>();
        string? answer = null;
        var adapter = new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null));
        await using var bridge = Create(adapter, new SettingsStore(Path.Combine(_root, "settings.json")), null, null, (title, initial) =>
        {
            asked.Add((title, initial));
            return answer;
        });

        var bad = Data(bridge, "settings.validate", "\"kind\":\"saveRoot\",\"path\":" + Js(Path.Combine(_root, "nope")));
        Assert.False(bad.GetProperty("ok").GetBoolean());
        Assert.Contains("不存在", bad.GetProperty("message").GetString());
        var good = Data(bridge, "settings.validate", "\"kind\":\"saveRoot\",\"path\":" + Js(saves));
        Assert.True(good.GetProperty("ok").GetBoolean());
        Assert.Equal(Shown(saves), good.GetProperty("path").GetString());
        var gameCheck = Data(bridge, "settings.validate", "\"kind\":\"gameDir\",\"path\":" + Js(game));
        Assert.True(gameCheck.GetProperty("ok").GetBoolean());
        Assert.Contains("kind", Error(bridge, "settings.validate", "\"kind\":\"other\",\"path\":" + Js(saves)));

        var cancelled = Data(bridge, "settings.pickFolder", "\"kind\":\"gameDir\"");
        Assert.True(cancelled.GetProperty("cancelled").GetBoolean());
        Assert.Equal("选择游戏目录", asked[0].Title);

        answer = Path.Combine(saves, "AutoSave-0");
        var picked = Data(bridge, "settings.pickFolder", "\"kind\":\"saveRoot\"");
        Assert.False(picked.GetProperty("cancelled").GetBoolean());
        Assert.True(picked.GetProperty("ok").GetBoolean());
        Assert.Equal(Shown(saves), picked.GetProperty("path").GetString());
        Assert.Contains("上一级", picked.GetProperty("message").GetString());
        Assert.Equal("选择存档目录", asked[1].Title);

        answer = _root;
        var wrong = Data(bridge, "settings.pickFolder", "\"kind\":\"gameDir\"");
        Assert.False(wrong.GetProperty("ok").GetBoolean());
        Assert.Equal(Shown(_root), wrong.GetProperty("picked").GetString());

        // 设置生效后，再打开对话框从当前目录开始。
        Data(bridge, "settings.save", "\"saveRoot\":" + Js(saves) + ",\"gameDir\":" + Js(game));
        answer = null;
        Data(bridge, "settings.pickFolder", "\"kind\":\"gameDir\"");
        Assert.Equal(game, asked[^1].Initial);
    }

    private static BridgeDispatcher Create(Cp2077Adapter adapter, SettingsStore settings, string? saveRoot, string? gameDir, Func<string, string?, string?>? picker)
    {
        var registry = new AdapterRegistry();
        registry.Register(adapter);
        var temp = Path.Combine(Path.GetDirectoryName(settings.FilePath)!, "bridge-" + Guid.NewGuid().ToString("N"));
        return new BridgeDispatcher(registry, saveRoot, _ => { }, Path.Combine(temp, "backups"), Path.Combine(temp, "skins"), settings, gameDir, picker);
    }

    private static JsonElement Data(BridgeDispatcher bridge, string method, string extra)
    {
        using var document = JsonDocument.Parse(bridge.Dispatch(Request(method, extra)));
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static string Error(BridgeDispatcher bridge, string method, string extra)
    {
        using var document = JsonDocument.Parse(bridge.Dispatch(Request(method, extra)));
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        return document.RootElement.GetProperty("error").GetProperty("message").GetString()!;
    }

    private static string Request(string method, string extra) =>
        "{\"id\":\"t\",\"method\":\"" + method + "\",\"params\":{\"gameId\":\"cp2077\"" + (extra.Length == 0 ? "" : "," + extra) + "}}";

    private static string Js(string value) => JsonSerializer.Serialize(value);

    private static string Shown(string path) => PathDisplay.Current.Mask(path);

    private string MakeSaves(string name, params string[] slots)
    {
        var root = Path.Combine(_root, name);
        foreach (var slot in slots)
        {
            Directory.CreateDirectory(Path.Combine(root, slot));
            File.WriteAllBytes(Path.Combine(root, slot, "sav.dat"), [1, 2, 3]);
        }

        return root;
    }

    private string MakeGame(string name, bool redmod)
    {
        var game = Path.Combine(_root, "library", "steamapps", "common", name);
        Directory.CreateDirectory(Path.Combine(game, "bin", "x64"));
        File.WriteAllBytes(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), [0]);
        Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "content"));
        if (redmod)
            Directory.CreateDirectory(Path.Combine(game, "tools", "redmod", "tweaks"));
        return game;
    }
}
