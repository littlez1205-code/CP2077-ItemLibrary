using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class EnableServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-enable-" + Guid.NewGuid().ToString("N"));

    public EnableServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Start_WritesOnlyChangedItems_AndRewritesAfterOverwrite()
    {
        var slot = WriteSlot("SlotA", """{"gold":10,"silver":3}""");
        var save = Path.Combine(slot, "save.json");
        var events = new List<NoticeRecord>();
        var service = Create(events);
        service.Start(Adapter(), save, [new ResourceChange(new ResourceId("gold"), 40)], backupFirst: false, _root);
        Assert.Contains("40", File.ReadAllText(save));
        Assert.Contains("3", File.ReadAllText(save));
        Assert.False(File.Exists(save + ".bak"));

        var seen = Count(events, "patched");
        await File.WriteAllTextAsync(save, """{"gold":1,"silver":9}""");
        await WaitAsync(events, "patched", seen + 1);
        var text = File.ReadAllText(save);
        Assert.Contains("40", text);
        Assert.Contains("9", text);
        Assert.DoesNotContain("\"silver\":3", text);

        service.Update([
            new ResourceChange(new ResourceId("gold"), 40),
            new ResourceChange(new ResourceId("silver"), 8)
        ]);
        Assert.Contains("8", File.ReadAllText(save));

        var before = Count(events, null);
        service.Stop();
        await File.WriteAllTextAsync(save, """{"gold":1,"silver":1}""");
        await Task.Delay(500);
        Assert.Equal(before + 1, Count(events, null));
        Assert.Equal("disabled", Last(events).Event);
        Assert.Contains("\"gold\":1", File.ReadAllText(save));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_InsertsAnUnheldResource_AndInsertsAgainAfterOverwrite()
    {
        var slot = WriteSlot("SlotA", """{"gold":10}""");
        var save = Path.Combine(slot, "save.json");
        var events = new List<NoticeRecord>();
        var service = Create(events);
        var adapter = new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100), ("gem", 100)))
        {
            AllowInsert = true
        };
        service.Start(adapter, save, [new ResourceChange(new ResourceId("gem"), 3)], backupFirst: false, _root);
        Assert.Contains("gem", File.ReadAllText(save));
        var seen = Count(events, "inserted");
        Assert.True(seen >= 1);

        await File.WriteAllTextAsync(save, """{"gold":10}""");
        await WaitAsync(events, "inserted", seen + 1);
        Assert.Contains("3", File.ReadAllText(save));
        Assert.Equal("inserted", service.Status.LastOutcome);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_RejectsEmptyChanges_AndKeepsASingleEnable()
    {
        var first = WriteSlot("SlotA", """{"gold":10,"silver":3}""");
        var second = WriteSlot("SlotB", """{"gold":2,"silver":2}""");
        var service = Create(new List<NoticeRecord>());
        var error = Assert.Throws<PatchRejectedException>(() =>
            service.Start(Adapter(), Path.Combine(first, "save.json"), [], false, _root));
        Assert.Equal("没有改动", error.Message);

        service.Start(Adapter(), Path.Combine(first, "save.json"), [new ResourceChange(new ResourceId("gold"), 40)], false, _root);
        service.Start(Adapter(), Path.Combine(second, "save.json"), [new ResourceChange(new ResourceId("gold"), 7)], false, _root);
        Assert.Equal(Path.GetFullPath(Path.Combine(second, "save.json")), service.Status.Slot);
        Assert.False(service.IsEnabled(Path.Combine(first, "save.json")));
        Assert.True(service.IsEnabled(Path.Combine(second, "save.json")));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Restore_IsRejectedWhileTheSlotIsEnabled()
    {
        var slot = WriteSlot("SlotA", """{"gold":10,"silver":3}""");
        var save = Path.Combine(slot, "save.json");
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        var service = new EnableService(backups, _ => { });
        var added = service.Start(Adapter(), save, [new ResourceChange(new ResourceId("gold"), 40)], backupFirst: true, _root);
        Assert.False(string.IsNullOrWhiteSpace(added));
        var error = Assert.Throws<PatchRejectedException>(() => backups.Restore(Adapter(), added!, _root, service.IsEnabled));
        Assert.Contains("使能", error.Message);
        service.Stop();
        backups.Restore(Adapter(), added!, _root, service.IsEnabled);
        Assert.Contains("\"gold\":10", File.ReadAllText(save));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task AcceptOwnWrite_KeepsEnableTargets()
    {
        var slot = WriteSlot("SlotA", """{"gold":10,"silver":3}""");
        var save = Path.Combine(slot, "save.json");
        var events = new List<NoticeRecord>();
        var service = Create(events);
        service.Start(Adapter(), save, [new ResourceChange(new ResourceId("gold"), 40)], false, _root);
        await File.WriteAllTextAsync(save, """{"gold":1,"silver":3}""");
        service.AcceptOwnWrite(save, File.GetLastWriteTimeUtc(save));
        Assert.Equal(40, service.Status.Changes[0].TargetQuantity);
        Assert.True(service.IsEnabled(save));
        Assert.Contains("\"gold\":40", File.ReadAllText(save));
        await Task.Delay(400);
        Assert.True(service.IsEnabled(save));
        Assert.Equal(40, service.Status.Changes[0].TargetQuantity);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Update_WithTheFullSet_KeepsEarlierTargets_AndAFailedUpdateDoesNotReplaceThem()
    {
        var slot = WriteSlot("SlotA", """{"gold":10,"silver":3}""");
        var save = Path.Combine(slot, "save.json");
        var events = new List<NoticeRecord>();
        var service = Create(events);
        service.Start(Adapter(), save, [new ResourceChange(new ResourceId("gold"), 40)], false, _root);
        Assert.Throws<PatchRejectedException>(() =>
            service.Update([new ResourceChange(new ResourceId("silver"), 1000)]));
        Assert.Equal("gold", service.Status.Changes[0].Id.Value);

        service.Update([
            new ResourceChange(new ResourceId("gold"), 40),
            new ResourceChange(new ResourceId("silver"), 8)
        ]);
        var seen = Count(events, "patched");
        await File.WriteAllTextAsync(save, """{"gold":1,"silver":1}""");
        await WaitAsync(events, "patched", seen + 1);
        var text = File.ReadAllText(save);
        Assert.Contains("\"gold\":40", text);
        Assert.Contains("\"silver\":8", text);
        await service.DisposeAsync();
    }

    private readonly record struct NoticeRecord(string Event, string? Message);

    private EnableService Create(List<NoticeRecord> events) =>
        new(new BackupStore(Path.Combine(_root, "backups")), notice =>
        {
            lock (events)
                events.Add(new NoticeRecord(notice.Event, notice.Message));
        });

    private string WriteSlot(string name, string json)
    {
        var slot = Path.Combine(_root, name);
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, "save.json"), json);
        return slot;
    }

    private FakeJsonGameAdapter Adapter() =>
        new(_root, FakeJsonGameAdapter.Catalog(("gold", 100), ("silver", 100)));

    private static int Count(List<NoticeRecord> events, string? name)
    {
        lock (events)
            return name is null ? events.Count : events.Count(item => item.Event == name);
    }

    private static NoticeRecord Last(List<NoticeRecord> events)
    {
        lock (events)
            return events[^1];
    }

    private static async Task WaitAsync(List<NoticeRecord> events, string name, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (Count(events, name) >= count)
                return;
            await Task.Delay(30);
        }

        string seen;
        lock (events)
            seen = string.Join("; ", events.Select(item => item.Event + (item.Message is null ? "" : " " + item.Message)));
        throw new TimeoutException("没有收到 " + name + "。已收到：" + seen);
    }
}
