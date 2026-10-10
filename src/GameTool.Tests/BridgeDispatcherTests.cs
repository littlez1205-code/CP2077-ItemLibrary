using System.Text.Json;
using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class BridgeDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-bridge-" + Guid.NewGuid().ToString("N"));
    private readonly string _backups = Path.Combine(Path.GetTempPath(), "GameTool-bridge-bak-" + Guid.NewGuid().ToString("N"));

    public BridgeDispatcherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        if (Directory.Exists(_backups))
            Directory.Delete(_backups, recursive: true);
    }

    [Fact]
    public async Task Dispatch_CoversEachMethod_AndKeepsRequestId()
    {
        var save = Path.Combine(_root, "save.json");
        await File.WriteAllTextAsync(save, """{"gold":1}""");
        await using var bridge = Create(out _);
        var ping = Json(bridge.Dispatch("""{"id":"p1","method":"app.ping"}"""));
        Assert.Equal("p1", Id(ping));
        Assert.Equal("ok", Status(ping));

        var games = Json(bridge.Dispatch("""{"id":"g1","method":"app.games"}"""));
        Assert.Equal("g1", Id(games));
        Assert.Contains("fake", games.RootElement.GetProperty("data").ToString());

        var list = Json(bridge.Dispatch("""{"id":"l1","method":"saves.list","params":{"gameId":"fake"}}"""));
        Assert.Equal("l1", Id(list));
        var row = list.RootElement.GetProperty("data")[0];
        var slot = row.GetProperty("slot").GetString();
        Assert.Equal("manual", row.GetProperty("group").GetString());
        Assert.False(row.GetProperty("enabled").GetBoolean());

        var info = Json(bridge.Dispatch("{\"id\":\"i1\",\"method\":\"saves.info\",\"params\":{\"gameId\":\"fake\",\"slot\":" + JsonString(slot) + "}}"));
        Assert.Equal("i1", Id(info));
        Assert.Equal(1, info.RootElement.GetProperty("data").GetProperty("resources")[0].GetProperty("quantity").GetInt64());

        var started = Json(bridge.Dispatch("{\"id\":\"a1\",\"method\":\"enable.start\",\"params\":{\"gameId\":\"fake\",\"slot\":" + JsonString(slot) + ",\"backupFirst\":true,\"changes\":[{\"id\":\"gold\",\"value\":40}]}}"));
        Assert.Equal("a1", Id(started));
        Assert.Equal(40, started.RootElement.GetProperty("data").GetProperty("changes")[0].GetProperty("value").GetInt64());
        var backupId = started.RootElement.GetProperty("data").GetProperty("backupId").GetString();

        var backups = Json(bridge.Dispatch("""{"id":"k1","method":"backup.list","params":{"gameId":"fake"}}"""));
        Assert.Equal(backupId, backups.RootElement.GetProperty("data")[0].GetProperty("id").GetString());

        var stopped = Json(bridge.Dispatch("""{"id":"r1","method":"enable.stop"}"""));
        Assert.Equal("ok", Status(stopped));
        var restored = Json(bridge.Dispatch("{\"id\":\"r2\",\"method\":\"backup.restore\",\"params\":{\"gameId\":\"fake\",\"backupId\":" + JsonString(backupId) + "}}"));
        Assert.Equal("ok", Status(restored));
    }

    [Fact]
    public void Dispatch_RejectsUnknownMethodAndMissingParams()
    {
        using var bridge = Create(out _);
        var unknown = Json(bridge.Dispatch("""{"id":"u1","method":"no.such"}"""));
        Assert.Equal("u1", Id(unknown));
        Assert.Equal("bad_args", unknown.RootElement.GetProperty("error").GetProperty("code").GetString());

        var missing = Json(bridge.Dispatch("""{"id":"m1","method":"saves.list","params":{}}"""));
        Assert.Equal("m1", Id(missing));
        Assert.Equal("bad_args", missing.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Restore_UnknownBackup_IsAnError()
    {
        var save = Path.Combine(_root, "save.json");
        File.WriteAllText(save, """{"gold":1}""");
        using var bridge = Create(out _);
        var response = Json(bridge.Dispatch("""{"id":"b1","method":"backup.restore","params":{"gameId":"fake","backupId":"fake/missing/1"}}"""));
        Assert.Equal("b1", Id(response));
        Assert.Equal("error", Status(response));
        Assert.Equal("failed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Dispatch_RejectsSlotsOutsideTheSaveRoot()
    {
        var save = Path.Combine(_root, "save.json");
        File.WriteAllText(save, """{"gold":1}""");
        using var bridge = Create(out _);
        var outside = Path.Combine(Path.GetTempPath(), "GameTool-outside-" + Guid.NewGuid().ToString("N"), "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, """{"gold":1}""");
        try
        {
            var escaped = Json(bridge.Dispatch("{\"id\":\"o1\",\"method\":\"saves.info\",\"params\":{\"gameId\":\"fake\",\"slot\":" + JsonString(outside) + "}}"));
            Assert.Equal("bad_args", escaped.RootElement.GetProperty("error").GetProperty("code").GetString());

            var traversal = Path.Combine(_root, "..", Path.GetFileName(Path.GetDirectoryName(outside)!), "save.json");
            var crossed = Json(bridge.Dispatch("{\"id\":\"o2\",\"method\":\"saves.info\",\"params\":{\"gameId\":\"fake\",\"slot\":" + JsonString(traversal) + "}}"));
            Assert.Equal("o2", Id(crossed));
            Assert.Equal("bad_args", crossed.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(outside)!, recursive: true);
        }
    }

    [Fact]
    public async Task Watch_PushesUntilStopped()
    {
        var events = new List<string>();
        await using var bridge = Create(out events);
        var save = Path.Combine(_root, "save.json");
        await File.WriteAllTextAsync(save, """{"gold":1}""");
        var started = Json(bridge.Dispatch("{\"id\":\"w1\",\"method\":\"enable.start\",\"params\":{\"gameId\":\"fake\",\"slot\":" + JsonString(save) + ",\"changes\":[{\"id\":\"gold\",\"value\":40}]}}"));
        Assert.Equal("ok", Status(started));
        await File.WriteAllTextAsync(save, """{"gold":4}""");
        var pushed = await WaitAsync(events);
        Assert.Contains("\"event\":", pushed);

        Assert.Equal("ok", Status(Json(bridge.Dispatch("""{"id":"w2","method":"enable.stop"}"""))));
        var before = events.Count;
        await File.WriteAllTextAsync(save, """{"gold":5}""");
        await Task.Delay(400);
        Assert.Equal(before, events.Count);
    }

    [Fact]
    public void OpenAuthorPage_OnlyEverOpensTheFixedUrl()
    {
        var launcher = new RecordingLauncher();
        using var bridge = Create(out _, launcher);
        var plain = Json(bridge.Dispatch("""{"id":"a1","method":"app.openAuthorPage"}"""));
        Assert.Equal("a1", Id(plain));
        Assert.Equal("ok", Status(plain));
        Assert.True(plain.RootElement.GetProperty("data").GetProperty("opened").GetBoolean());

        // 页面就算送来别的地址也一律忽略，宿主只会打开固定的作者主页。
        var forged = Json(bridge.Dispatch("""{"id":"a2","method":"app.openAuthorPage","params":{"url":"https://example.com/","href":"file:///C:/Windows/System32/calc.exe"}}"""));
        Assert.Equal("ok", Status(forged));
        var empty = Json(bridge.Dispatch("""{"id":"a3","method":"app.openAuthorPage","params":{}}"""));
        Assert.Equal("ok", Status(empty));

        Assert.Equal(3, launcher.Opened.Count);
        Assert.All(launcher.Opened, url => Assert.Equal("https://space.bilibili.com/3546366456367644", url));
        Assert.Equal(AuthorPage.Url, launcher.Opened[0]);
        Assert.Equal("魔法苦茶籽", AuthorPage.AuthorName);
    }

    [Fact]
    public void OpenAuthorPage_WithoutLauncher_FailsAndOpensNothing()
    {
        using var bridge = Create(out _);
        var response = Json(bridge.Dispatch("""{"id":"n1","method":"app.openAuthorPage"}"""));
        Assert.Equal("n1", Id(response));
        Assert.Equal("error", Status(response));
        Assert.Contains(AuthorPage.Url, response.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void OpenAuthorPage_LauncherFailure_IsReportedWithTheUrl()
    {
        using var bridge = Create(out _, new ThrowingLauncher());
        var response = Json(bridge.Dispatch("""{"id":"f1","method":"app.openAuthorPage"}"""));
        Assert.Equal("error", Status(response));
        Assert.Equal("failed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(AuthorPage.Url, response.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    private sealed class RecordingLauncher : IUrlLauncher
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    private sealed class ThrowingLauncher : IUrlLauncher
    {
        public void Open(string url) => throw new System.ComponentModel.Win32Exception("no browser");
    }

    private BridgeDispatcher Create(out List<string> events, IUrlLauncher? launcher = null)
    {
        var sink = new List<string>();
        events = sink;
        var registry = new AdapterRegistry();
        registry.Register(new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100))));
        return new BridgeDispatcher(registry, _root, message =>
        {
            lock (sink)
                sink.Add(message);
        }, _backups, urlLauncher: launcher);
    }

    private static async Task<string> WaitAsync(List<string> events)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (events)
            {
                if (events.Count > 0)
                    return events[0];
            }

            await Task.Delay(30);
        }

        throw new TimeoutException("没有收到监听推送。");
    }

    private static JsonDocument Json(string json) => JsonDocument.Parse(json);

    private static string? Id(JsonDocument document) => document.RootElement.GetProperty("id").GetString();

    private static string? Status(JsonDocument document) => document.RootElement.GetProperty("status").GetString();

    private static string JsonString(string? value) => JsonSerializer.Serialize(value);
}
