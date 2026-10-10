using GameTool.Adapters.Cyberpunk2077;
using GameTool.App;

namespace GameTool.Tests;

public class SlotGroupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-groups-" + Guid.NewGuid().ToString("N"));

    public SlotGroupTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DiscoverSaves_GroupsByAdapterPrefix()
    {
        Touch("QuickSave-1");
        Touch("ManualSave-2");
        Touch("AutoSave-3");
        Touch("EndGameSave");
        var groups = new Cp2077Adapter().DiscoverSaves(_root).ToDictionary(slot => slot.SlotName, slot => slot.Group);
        Assert.Equal("quick", groups["QuickSave-1"]);
        Assert.Equal("manual", groups["ManualSave-2"]);
        Assert.Equal("auto", groups["AutoSave-3"]);
        Assert.Equal("other", groups["EndGameSave"]);
    }

    [Fact]
    public void DefaultManualSave_SkipsNewerExperimentSlot()
    {
        Touch("ManualSave-7", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("ManualSave-92", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("QuickSave-1", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        var selected = ManualSaveSelection.NewestPlayable(new Cp2077Adapter().DiscoverSaves(_root));
        Assert.NotNull(selected);
        Assert.Equal("ManualSave-7", selected.SlotName);
    }

    private void Touch(string name, DateTime? written = null)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "sav.dat");
        File.WriteAllText(file, "x");
        if (written is DateTime stamp)
            File.SetLastWriteTimeUtc(file, stamp);
    }
}
