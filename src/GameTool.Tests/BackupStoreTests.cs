using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class BackupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-backup-" + Guid.NewGuid().ToString("N"));
    private readonly string _saves;
    private readonly string _backups;

    public BackupStoreTests()
    {
        _saves = Path.Combine(_root, "saves");
        _backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(_saves);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Add_List_Restore_AndDelete_RoundTripHashes()
    {
        var slot = WriteSlot("Manual", """{"gold":10,"silver":2}""");
        var store = new BackupStore(_backups);
        var adapter = Adapter();
        var added = store.Add(adapter, slot);
        var listed = store.List("fake");
        Assert.Single(listed);
        Assert.Equal(added.Id, listed[0].Id);

        File.WriteAllText(Path.Combine(slot, "save.json"), """{"gold":99,"silver":2}""");
        store.Restore(adapter, added.Id, _saves, _ => false);
        Assert.Equal("""{"gold":10,"silver":2}""", File.ReadAllText(Path.Combine(slot, "save.json")));

        store.Delete(added.Id);
        Assert.Empty(store.List("fake"));
    }

    [Fact]
    public void Add_TwiceInTheSameSecond_UsesASuffix()
    {
        var slot = WriteSlot("Manual", """{"gold":10}""");
        var store = new BackupStore(_backups);
        var adapter = Adapter();
        var first = store.Add(adapter, slot);
        var second = store.Add(adapter, slot);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.List("fake").Count);
    }

    [Fact]
    public void Delete_RejectsPathEscape()
    {
        var store = new BackupStore(_backups);
        var outside = Path.Combine(_root, "secret");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "manifest.json"), "{}");
        Assert.Throws<PatchRejectedException>(() => store.Delete("../secret"));
        Assert.Throws<PatchRejectedException>(() => store.Delete(Path.GetFullPath(outside)));
        Assert.True(File.Exists(Path.Combine(outside, "manifest.json")));
    }

    [Fact]
    public void Add_SkipsBakAndTmp()
    {
        var slot = WriteSlot("Manual", """{"gold":10}""");
        File.WriteAllText(Path.Combine(slot, "save.json.bak"), "old");
        File.WriteAllText(Path.Combine(slot, "save.json.tmp"), "partial");
        var added = new BackupStore(_backups).Add(Adapter(), slot);
        var directory = Path.Combine(_backups, added.Id.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(File.Exists(Path.Combine(directory, "save.json.bak")));
        Assert.False(File.Exists(Path.Combine(directory, "save.json.tmp")));
    }

    [Fact]
    public void Restore_RejectsManifestThatEscapesTheSaveRootOrBackup()
    {
        var slot = WriteSlot("Manual", """{"gold":10}""");
        var store = new BackupStore(_backups);
        var added = store.Add(Adapter(), slot);
        var manifestPath = Path.Combine(_backups, added.Id.Replace('/', Path.DirectorySeparatorChar), "manifest.json");
        var text = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, text.Replace("\"Name\": \"save.json\"", "\"Name\": \"../outside.txt\""));
        var error = Assert.Throws<PatchRejectedException>(() => store.Restore(Adapter(), added.Id, _saves, _ => false));
        Assert.Contains("超出", error.Message);

        File.WriteAllText(manifestPath, text.Replace("\"SourceSlot\":", "\"SourceSlot\": \"C:\\\\outside\\\\save.json\", \"ignored\":"));
        Assert.Throws<PatchRejectedException>(() => store.Restore(Adapter(), added.Id, _saves, _ => false));
    }

    [Fact]
    public void Add_RejectsUnreadableSave()
    {
        var slot = WriteSlot("Broken", " ");
        var store = new BackupStore(_backups);
        var error = Assert.Throws<PatchRejectedException>(() => store.Add(Adapter(), slot));
        Assert.Equal("存档不完整或无法读取。", error.Message);
        Assert.Empty(store.List("fake"));
    }

    private string WriteSlot(string name, string json)
    {
        var slot = Path.Combine(_saves, name);
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, "save.json"), json);
        return slot;
    }

    private FakeJsonGameAdapter Adapter() =>
        new(_saves, FakeJsonGameAdapter.Catalog(("gold", 100), ("silver", 100)));
}
