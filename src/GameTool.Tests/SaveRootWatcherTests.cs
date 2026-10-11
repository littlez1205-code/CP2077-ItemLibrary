using System.Text.Json;
using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

/// <summary>T030：存档根目录监视的去抖与推送（只用临时目录，不碰真实存档）。</summary>
public class SaveRootWatcherTests : IDisposable
{
    private const int Debounce = 300;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-rootwatch-" + Guid.NewGuid().ToString("N"));

    public SaveRootWatcherTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("ManualSave-1", false, true)]
    [InlineData("ManualSave-1", true, false)]
    [InlineData(@"ManualSave-1\sav.dat", false, true)]
    [InlineData(@"ManualSave-1\SAV.DAT", true, true)]
    [InlineData("ManualSave-1/sav.dat", true, true)]
    [InlineData(@"ManualSave-1\screenshot.png", true, false)]
    [InlineData(@"ManualSave-1\metadata.9.json", false, false)]
    [InlineData(@"ManualSave-1\sav.dat~RF1a2b.TMP", false, false)]
    [InlineData(@"ManualSave-1\deep\sav.dat", true, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void IsRelevant_SlotDirectoriesAndSaveFilesOnly(string? relative, bool changed, bool expected) =>
        Assert.Equal(expected, SaveRootWatcher.IsRelevant(relative, changed));

    [Fact]
    public void DefaultDebounce_IsOnePointFiveSeconds()
    {
        Assert.Equal(1500, SaveRootWatcher.DefaultDebounceMilliseconds);
        using var watcher = new SaveRootWatcher(_root, () => { });
        Assert.True(watcher.Active);
        Assert.Equal(1500, watcher.DebounceMilliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SaveRootWatcher(_root, () => { }, -1));
    }

    [Fact]
    public async Task GameStyleSave_ManyWritesAndARename_NotifiesOnce()
    {
        var slot = Directory.CreateDirectory(Path.Combine(_root, "ManualSave-1")).FullName;
        File.WriteAllBytes(Path.Combine(slot, "sav.dat"), [1]);
        var count = 0;
        using var watcher = new SaveRootWatcher(_root, () => Interlocked.Increment(ref count), Debounce);

        // 游戏写存档：临时文件多次写入、改名替换、截图和 metadata 跟着写。
        var temp = Path.Combine(slot, "sav.dat.tmp");
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllBytes(temp, new byte[100 * (i + 1)]);
            await Task.Delay(30);
        }

        File.Move(temp, Path.Combine(slot, "sav.dat"), overwrite: true);
        File.WriteAllBytes(Path.Combine(slot, "screenshot.png"), [2]);
        File.WriteAllBytes(Path.Combine(slot, "metadata.9.json"), [3]);
        await WaitFor(() => Volatile.Read(ref count) > 0, 5000);
        await Task.Delay(Debounce * 3);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task OtherFiles_DoNotNotify()
    {
        var slot = Directory.CreateDirectory(Path.Combine(_root, "ManualSave-1")).FullName;
        Directory.CreateDirectory(Path.Combine(slot, "deep"));
        var count = 0;
        using var watcher = new SaveRootWatcher(_root, () => Interlocked.Increment(ref count), Debounce);
        File.WriteAllBytes(Path.Combine(slot, "screenshot.png"), [1]);
        File.WriteAllBytes(Path.Combine(slot, "metadata.9.json"), [1]);
        File.WriteAllBytes(Path.Combine(slot, "deep", "sav.dat"), [1]);
        await Task.Delay(Debounce * 4);
        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task SlotDirectories_CreatedRenamedDeleted_EachNotify()
    {
        var count = 0;
        using var watcher = new SaveRootWatcher(_root, () => Interlocked.Increment(ref count), Debounce);
        var created = Directory.CreateDirectory(Path.Combine(_root, "ManualSave-2")).FullName;
        await WaitFor(() => Volatile.Read(ref count) == 1, 5000);
        var renamed = Path.Combine(_root, "ManualSave-3");
        Directory.Move(created, renamed);
        await WaitFor(() => Volatile.Read(ref count) == 2, 5000);
        Directory.Delete(renamed);
        await WaitFor(() => Volatile.Read(ref count) == 3, 5000);
        await Task.Delay(Debounce * 2);
        Assert.Equal(3, Volatile.Read(ref count));
    }

    [Fact]
    public async Task MissingRoot_IsInactive_AndDisposeStopsPendingNotifications()
    {
        var count = 0;
        using (var missing = new SaveRootWatcher(Path.Combine(_root, "nope"), () => Interlocked.Increment(ref count), Debounce))
            Assert.False(missing.Active);

        var watcher = new SaveRootWatcher(_root, () => Interlocked.Increment(ref count), Debounce);
        Directory.CreateDirectory(Path.Combine(_root, "ManualSave-4"));
        await Task.Delay(50);
        watcher.Dispose();
        watcher.Dispose();
        await Task.Delay(Debounce * 3);
        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Bridge_PushesSavesChanged_FollowsTheSaveRootSetting_AndStopsOnDispose()
    {
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        var pushed = new List<string>();
        var registry = new AdapterRegistry();
        registry.Register(new FakeJsonGameAdapter(first, FakeJsonGameAdapter.Catalog(("gold", 100))));
        var settings = new SettingsStore(Path.Combine(_root, "settings.json"));
        settings.Set("fake", new GamePaths(first, null));
        var bridge = new BridgeDispatcher(registry, null, json =>
        {
            lock (pushed)
                pushed.Add(json);
        }, Path.Combine(_root, "backups"), Path.Combine(_root, "skins"), settings);
        int Count()
        {
            lock (pushed)
                return pushed.Count;
        }

        // 没调用 WatchSaveRoots（命令行、单元测试的默认）：不监视。
        Assert.False(bridge.IsWatchingSaveRoot("fake"));
        Directory.CreateDirectory(Path.Combine(first, "slot-0"));
        await Task.Delay(Debounce * 2);
        Assert.Equal(0, Count());

        bridge.WatchSaveRoots(Debounce);
        Assert.True(bridge.IsWatchingSaveRoot("fake"));
        File.WriteAllText(Path.Combine(first, "slot-0", "save.json"), """{"gold":1}""");
        File.WriteAllText(Path.Combine(first, "slot-0", "save.json"), """{"gold":2}""");
        await WaitFor(() => Count() == 1, 5000);
        using (var message = JsonDocument.Parse(pushed[0]))
        {
            Assert.Equal("saves.changed", message.RootElement.GetProperty("event").GetString());
            Assert.Equal("fake", message.RootElement.GetProperty("data").GetProperty("gameId").GetString());
        }

        // 设置里换了存档根目录：旧目录不再推送，新目录推送。
        using var saved = JsonDocument.Parse(bridge.Dispatch(
            "{\"id\":\"s\",\"method\":\"settings.save\",\"params\":{\"gameId\":\"fake\",\"saveRoot\":" + JsonSerializer.Serialize(second) + "}}"));
        Assert.Equal("ok", saved.RootElement.GetProperty("status").GetString());
        Assert.True(bridge.IsWatchingSaveRoot("fake"));
        Directory.CreateDirectory(Path.Combine(first, "slot-1"));
        await Task.Delay(Debounce * 3);
        Assert.Equal(1, Count());
        Directory.CreateDirectory(Path.Combine(second, "slot-2"));
        await WaitFor(() => Count() == 2, 5000);

        await bridge.DisposeAsync();
        Assert.False(bridge.IsWatchingSaveRoot("fake"));
        bridge.WatchSaveRoots(Debounce);
        Assert.False(bridge.IsWatchingSaveRoot("fake"));
        Directory.CreateDirectory(Path.Combine(second, "slot-3"));
        await Task.Delay(Debounce * 3);
        Assert.Equal(2, Count());
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMs)
                throw new TimeoutException("等待条件超时。");
            await Task.Delay(20);
        }
    }
}
