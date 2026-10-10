using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class SaveWatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-watch-" + Guid.NewGuid().ToString("N"));

    public SaveWatcherTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Watcher_PatchesMatchingFile_AndDoesNotPatchAgain()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        var path = Path.Combine(_root, "slot-1", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"gold":1}""");

        var patched = await WaitForAsync(events, item => item.Outcome == "patched");
        Assert.Equal(40, adapter.Load(path).GetQuantity(new ResourceId("gold")));
        Assert.True(patched.WriteCompleteToDoneMs <= 500);
        await Task.Delay(2000);
        Assert.Equal(1, events.Count(item => item.Outcome == "patched"));
    }

    [Fact]
    public async Task Watcher_IgnoresNonMatchingFiles()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), """{"gold":1}""");
        await Task.Delay(400);
        Assert.Empty(events);

        var path = Path.Combine(_root, "slot-2", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"gold":1}""");
        await WaitForAsync(events, item => item.Outcome == "patched");
        Assert.Equal(40, adapter.Load(path).GetQuantity(new ResourceId("gold")));
    }

    [Fact]
    public async Task Watcher_WaitsForExclusiveLock_ThenPatches()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        var path = Path.Combine(_root, "slot-3", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("""{"gold":1}""");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            await Task.Delay(400);
        }

        await WaitForAsync(events, item => item.Outcome == "patched", timeoutMs: 5000);
        Assert.Equal(40, adapter.Load(path).GetQuantity(new ResourceId("gold")));
    }

    [Fact]
    public async Task Watcher_ReportsBadVersion_ThenPatchesALaterFile()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        var bad = Path.Combine(_root, "slot-bad", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        await File.WriteAllTextAsync(bad, """{"badVersion":true}""");
        var error = await WaitForAsync(events, item => item.Path == bad && item.Outcome == "error");
        Assert.Contains("不受支持", error.Error);

        var good = Path.Combine(_root, "slot-good", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(good)!);
        await File.WriteAllTextAsync(good, """{"gold":1}""");
        await WaitForAsync(events, item => item.Path == good && item.Outcome == "patched");
        Assert.Equal(40, adapter.Load(good).GetQuantity(new ResourceId("gold")));
    }

    [Fact]
    public async Task Watcher_SurvivesFiveRapidWritesToTheSameSlot()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        var path = Path.Combine(_root, "slot-rapid", "save.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(path, """{"gold":1}""");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            lock (events)
            {
                if (events.Any(item => item.Outcome == "patched") && adapter.Load(path).GetQuantity(new ResourceId("gold")) == 40)
                    break;
            }
        }

        await Task.Delay(500);
        lock (events)
        {
            Assert.All(events.Where(item => item.Outcome == "error"), item =>
                Assert.Equal("存档在补丁过程中被改写", item.Error));
            Assert.Contains(events, item => item.Outcome == "patched");
        }

        Assert.Equal(40, adapter.Load(path).GetQuantity(new ResourceId("gold")));
    }

    /// <summary>
    /// T023：监听期间同目录的 File.Replace 不能失败。File.Replace 先把原文件改名为 xxx~RFxxxx.TMP 再删除；
    /// 以前监听对每个事件路径做 Path.GetRelativePath，带 ~ 的名字会触发 GetLongPathName 去查这个中间文件，
    /// 和改名撞车时 File.Replace 抛「无法删除要被替换的文件」（约 1/10 的概率）。不匹配的文件不应碰文件系统。
    /// </summary>
    [Fact]
    public async Task Watcher_DoesNotBreakFileReplaceInTheWatchedTree()
    {
        var adapter = Adapter();
        var events = new List<WatchEvent>();
        await using var watcher = Create(adapter, events);
        watcher.Start();
        var directory = Path.Combine(_root, "slot-replace");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "other.dat");
        await File.WriteAllBytesAsync(target, new byte[64 * 1024]);
        for (var i = 0; i < 150; i++)
        {
            var temporary = target + ".tmp";
            await File.WriteAllBytesAsync(temporary, new byte[(64 * 1024) + i]);
            File.Replace(temporary, target, destinationBackupFileName: null);
        }

        Assert.Equal((64 * 1024) + 149, new FileInfo(target).Length);
        lock (events)
            Assert.Empty(events);
    }

    private SaveWatcher Create(FakeJsonGameAdapter adapter, List<WatchEvent> events) =>
        new(adapter, _root, ["slot-*/save.json"], [new ResourceChange(new ResourceId("gold"), 40)], 20, watchEvent =>
        {
            lock (events)
                events.Add(watchEvent);
        });

    private static FakeJsonGameAdapter Adapter() =>
        new(Path.GetTempPath(), FakeJsonGameAdapter.Catalog(("gold", 100)));

    private static async Task<WatchEvent> WaitForAsync(List<WatchEvent> events, Func<WatchEvent, bool> match, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            lock (events)
            {
                var found = events.FirstOrDefault(match);
                if (found is not null)
                    return found;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("等待监听事件超时。");
    }
}
