using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class PatchEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-fake-" + Guid.NewGuid().ToString("N"));

    public PatchEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Patch_WritesBackup_SecondRunIsNoChange_RestoreMatchesOriginal()
    {
        var save = WriteSave("""{"gold":10}""");
        var original = File.ReadAllBytes(save);
        var adapter = Adapter();

        var first = PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)]);
        Assert.Equal(PatchOutcome.Patched, first.Outcome);
        Assert.Equal(original, File.ReadAllBytes(save + ".bak"));
        var stamp = File.GetLastWriteTimeUtc(save);
        var backup = File.ReadAllBytes(save + ".bak");

        var second = PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)]);
        Assert.Equal(PatchOutcome.NoChange, second.Outcome);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(save));
        Assert.Equal(backup, File.ReadAllBytes(save + ".bak"));

        PatchEngine.Restore(adapter, save);
        Assert.Equal(original, File.ReadAllBytes(save));
    }

    [Fact]
    public void Patch_WithoutBackup_DoesNotCreateBak_AndStillVerifies()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter();
        var report = PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)], new PatchOptions(null));
        Assert.Equal(PatchOutcome.Patched, report.Outcome);
        Assert.Null(report.BackupPath);
        Assert.False(File.Exists(save + ".bak"));
        Assert.False(File.Exists(save + ".tmp"));
        Assert.Contains("40", File.ReadAllText(save));

        var again = PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)], new PatchOptions(null));
        Assert.Equal(PatchOutcome.NoChange, again.Outcome);
        Assert.False(File.Exists(save + ".bak"));
    }

    [Fact]
    public void Patch_RejectsOutOfRange_WithoutCreatingFiles()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter();
        Assert.Throws<PatchRejectedException>(() =>
            PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 101)]));
        var names = Directory.GetFiles(_root).Select(Path.GetFileName).Order().ToArray();
        Assert.Single(names);
        Assert.Equal("save.json", names[0]);
    }

    [Fact]
    public void Patch_SkipsMissingResource()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter(("gold", 100), ("gem", 100));
        var report = PatchEngine.Patch(adapter, save,
        [
            new ResourceChange(new ResourceId("gold"), 12),
            new ResourceChange(new ResourceId("gem"), 3)
        ]);
        Assert.Equal(PatchOutcome.Patched, report.Outcome);
        Assert.Contains(report.Results, result => result.Id == new ResourceId("gem") && result.Outcome == ChangeOutcome.NotHeld);
        Assert.Equal(12, adapter.Load(save).GetQuantity(new ResourceId("gold")));
        Assert.Null(adapter.Load(save).GetQuantity(new ResourceId("gem")));
    }

    [Fact]
    public void Patch_InsertsOneMissingResource_WhenAllowed()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter(("gold", 100), ("gem", 100));
        adapter.AllowInsert = true;
        var report = PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gem"), 3)], new PatchOptions(null));
        Assert.Equal(PatchOutcome.Patched, report.Outcome);
        Assert.Equal(ChangeOutcome.Inserted, Assert.Single(report.Results).Outcome);
        Assert.Equal(3, adapter.Load(save).GetQuantity(new ResourceId("gem")));
    }

    [Fact]
    public void Patch_InsertsSeveral_AndMixesWithAQuantityEdit()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter(("gold", 100), ("gem", 100), ("ore", 100));
        adapter.AllowInsert = true;
        var report = PatchEngine.Patch(adapter, save,
        [
            new ResourceChange(new ResourceId("gold"), 12),
            new ResourceChange(new ResourceId("gem"), 3),
            new ResourceChange(new ResourceId("ore"), 4)
        ], new PatchOptions(null));
        Assert.Equal(PatchOutcome.Patched, report.Outcome);
        Assert.Contains(report.Results, result => result.Id == new ResourceId("gold") && result.Outcome == ChangeOutcome.Changed);
        Assert.Contains(report.Results, result => result.Id == new ResourceId("gem") && result.Outcome == ChangeOutcome.Inserted);
        Assert.Contains(report.Results, result => result.Id == new ResourceId("ore") && result.Outcome == ChangeOutcome.Inserted);
        var reloaded = adapter.Load(save);
        Assert.Equal(12, reloaded.GetQuantity(new ResourceId("gold")));
        Assert.Equal(3, reloaded.GetQuantity(new ResourceId("gem")));
        Assert.Equal(4, reloaded.GetQuantity(new ResourceId("ore")));
    }

    [Fact]
    public void Patch_CorruptWrite_LeavesOriginalUntouched()
    {
        var save = WriteSave("""{"gold":10}""");
        var original = File.ReadAllBytes(save);
        var adapter = Adapter();
        adapter.CorruptWrites = true;
        Assert.Throws<PatchRejectedException>(() =>
            PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)]));
        Assert.Equal(original, File.ReadAllBytes(save));
        Assert.False(File.Exists(save + ".tmp"));
        Assert.False(File.Exists(save + ".bak"));
    }

    [Fact]
    public void Patch_AmbiguousStacks_AreRejected()
    {
        var save = WriteSave("""{"gold":[1,2]}""");
        var adapter = Adapter();
        Assert.Throws<PatchRejectedException>(() =>
            PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 5)]));
        Assert.False(File.Exists(save + ".tmp"));
        Assert.False(File.Exists(save + ".bak"));
    }

    [Fact]
    public void Patch_RejectsWhenOriginalChangesBeforeReplace()
    {
        var save = WriteSave("""{"gold":10}""");
        var adapter = Adapter();
        adapter.RewriteOriginalOnSave = true;
        var error = Assert.Throws<SaveChangedDuringPatchException>(() =>
            PatchEngine.Patch(adapter, save, [new ResourceChange(new ResourceId("gold"), 40)]));
        Assert.Equal("存档在补丁过程中被改写", error.Message);
        Assert.Equal(FakeJsonGameAdapter.RewrittenMarker, File.ReadAllText(save));
        Assert.False(File.Exists(save + ".bak"));
        Assert.False(File.Exists(save + ".tmp"));
    }

    private FakeJsonGameAdapter Adapter(params (string Id, long Max)[] resources)
    {
        if (resources.Length == 0)
            resources = [("gold", 100)];
        return new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(resources));
    }

    private string WriteSave(string json)
    {
        var path = Path.Combine(_root, "save.json");
        File.WriteAllText(path, json);
        return path;
    }
}
