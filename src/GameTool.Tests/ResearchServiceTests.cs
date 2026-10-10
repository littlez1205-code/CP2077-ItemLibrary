using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class ResearchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-research-" + Guid.NewGuid().ToString("N"));

    public ResearchServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Diff_IdenticalImages_AreEmpty()
    {
        var image = Item("itemData", 0, 10);
        var diff = ResearchService.Diff(image, image);
        Assert.Empty(diff.Header);
        Assert.Empty(diff.Regions);
    }

    [Fact]
    public void Diff_QuantityChange_IsOneFourByteSpan()
    {
        var before = Item("itemData", 0, 10);
        var after = Item("itemData", 0, 11);
        var diff = ResearchService.Diff(before, after);
        var change = Assert.Single(diff.Regions);
        Assert.Equal("changed", change.Kind);
        Assert.Equal("itemData", change.Name);
        var span = Assert.Single(change.Spans);
        Assert.Equal(25, span.Start);
        Assert.Equal(29, span.End);
    }

    [Fact]
    public void Diff_InsertedRegion_ShiftsLaterIndexesOnly()
    {
        var before = ImageOf(Body(0, 10), Body(1, 20));
        var after = ImageOf(Body(9, 99), Body(1, 10), Body(2, 20));
        var diff = ResearchService.Diff(before, after);
        Assert.Single(diff.Regions, region => region.Kind == "added");
        Assert.Equal(2, diff.Regions.Count(region => region.Kind == "indexShiftOnly"));
        Assert.DoesNotContain(diff.Regions, region => region.Kind == "changed");
    }

    [Fact]
    public void Find_U32AndTdb_HitTheItemRegion()
    {
        var image = Item("itemData", 7, 42);
        var adapter = new FixedAdapter(image, 0x0102030405060708UL);
        var save = Path.Combine(_root, "slot", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        File.WriteAllBytes(save, [0]);
        var quantity = ResearchService.Find(adapter, save, new ResearchNeedle(ResearchNeedleKind.U32, "42"));
        var quantityHit = Assert.Single(quantity.Hits, hit => hit.Region == "itemData" && hit.Relative == 25);
        Assert.Equal(25, quantityHit.Offset);

        var tweak = ResearchService.Find(adapter, save, new ResearchNeedle(ResearchNeedleKind.Tdb, "Items.money"));
        var tweakHit = Assert.Single(tweak.Hits);
        Assert.Equal("itemData", tweakHit.Region);
        Assert.Equal(4, tweakHit.Relative);
    }

    [Fact]
    public void Dump_RejectsOutputInsideTheSaveRoot()
    {
        var slot = Path.Combine(_root, "saves", "ManualSave-0");
        Directory.CreateDirectory(slot);
        var save = Path.Combine(slot, "sav.dat");
        File.WriteAllBytes(save, [1]);
        var adapter = new FixedAdapter(Item("itemData", 0, 1), 1);
        var inside = Path.Combine(_root, "saves", "dump");
        var error = Assert.Throws<ResearchRejectedException>(() => ResearchService.Dump(adapter, save, inside, force: false));
        Assert.Contains("存档根", error.Message);
    }

    [Fact]
    public void Summary_CountsChangedAddedAndIndexShifts()
    {
        var before = ImageOf(Body(0, 10), Body(1, 20));
        var inserted = Body(9, 99);
        var afterPayload = inserted.Concat(Body(0, 11)).Concat(Body(2, 20)).ToArray();
        var after = new ResearchImage(afterPayload, [],
        [
            new ResearchRegion(0, "widget", 0, inserted.Length, null),
            new ResearchRegion(1, "itemData", inserted.Length, 29, null),
            new ResearchRegion(2, "itemData", inserted.Length + 29, 29, null)
        ]);
        var summary = ResearchService.Summarize(ResearchService.Diff(before, after));
        Assert.Equal(1, summary.ChangedCount);
        Assert.Equal(1, summary.AddedCount);
        Assert.Equal(0, summary.RemovedCount);
        Assert.Equal(1, summary.IndexShiftOnly);
        var row = Assert.Single(summary.Changes);
        Assert.Equal("itemData", row.Name);
        Assert.Equal(4, row.ChangedBytes);
        Assert.Equal(1, row.SpanCount);
        Assert.Equal("widget", Assert.Single(summary.Added).Name);

        var only = ResearchService.SelectRegions(ResearchService.Diff(before, after), ["ITEMDATA", "missing"]);
        Assert.DoesNotContain(only.Regions, region => region.Name == "widget");
        Assert.Equal(2, only.Regions.Count);
    }

    [Fact]
    public void Find_InRegion_DropsHitsOutsideThatName()
    {
        var body = Body(7, 42);
        var payload = body.Concat(BitConverter.GetBytes(42)).ToArray();
        var image = new ResearchImage(payload, [],
        [
            new ResearchRegion(0, "itemData", 0, body.Length, null),
            new ResearchRegion(1, "other", body.Length, 4, null)
        ]);
        var adapter = new FixedAdapter(image, 1);
        var save = Path.Combine(_root, "find", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        File.WriteAllBytes(save, [0]);
        var inside = ResearchService.Find(adapter, save, new ResearchNeedle(ResearchNeedleKind.U32, "42"), "ITEMDATA");
        var hit = Assert.Single(inside.Hits);
        Assert.Equal("itemData", hit.Region);
        Assert.Equal(25, hit.Relative);
        Assert.Equal(1, inside.Total);

        var outside = ResearchService.Find(adapter, save, new ResearchNeedle(ResearchNeedleKind.U32, "42"), "other");
        Assert.Equal("other", Assert.Single(outside.Hits).Region);
    }

    [Fact]
    public void FakeGame_DiffAndFind_WorkWithoutRegions()
    {
        var left = Path.Combine(_root, "a.json");
        var right = Path.Combine(_root, "b.json");
        File.WriteAllBytes(left, [1, 2, 3, 4]);
        File.WriteAllBytes(right, [1, 2, 9, 4]);
        var adapter = new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100)));
        var diff = ResearchService.Diff(adapter, left, right);
        var change = Assert.Single(diff.Regions);
        Assert.Equal("", change.Name);
        Assert.Equal(2, Assert.Single(change.Spans).Start);

        var found = ResearchService.Find(adapter, left, new ResearchNeedle(ResearchNeedleKind.Hex, "0102"));
        var hit = Assert.Single(found.Hits);
        Assert.Null(hit.Region);
        Assert.Equal(0, hit.Offset);
    }

    private static ResearchImage Item(string name, int index, uint quantity)
    {
        var body = Body(index, quantity);
        return ImageOf(body);
    }

    private static byte[] Body(int index, uint quantity)
    {
        var body = new byte[29];
        BitConverter.GetBytes(index).CopyTo(body, 0);
        BitConverter.GetBytes(0x0102030405060708UL).CopyTo(body, 4);
        BitConverter.GetBytes(quantity).CopyTo(body, 25);
        return body;
    }

    private static ResearchImage ImageOf(params byte[][] bodies)
    {
        var payload = bodies.SelectMany(body => body).ToArray();
        var regions = new List<ResearchRegion>();
        var offset = 0;
        for (var i = 0; i < bodies.Length; i++)
        {
            regions.Add(new ResearchRegion(i, "itemData", offset, bodies[i].Length, null));
            offset += bodies[i].Length;
        }

        return new ResearchImage(payload, [], regions);
    }

    private sealed class FixedAdapter : IGameAdapter, IResearchProvider, ITweakDbIdSource
    {
        private readonly ResearchImage _image;
        private readonly ulong _tweak;

        public FixedAdapter(ResearchImage image, ulong tweak)
        {
            _image = image;
            _tweak = tweak;
        }

        public string GameId => "fixed";
        public string DisplayName => "fixed";
        public string DefaultSaveRoot => "";
        public SaveWatchSpec WatchSpec => new("", [], 1);
        public IReadOnlyList<ResourceDescriptor> Resources => [];
        public ResearchImage OpenForResearch(string saveFilePath) => _image;
        public ulong TweakId(string name) => _tweak;
        public IEnumerable<SaveSlotInfo> DiscoverSaves(string saveRoot) => [];
        public bool IsComplete(string saveFilePath) => true;
        public ISaveDocument Load(string saveFilePath) => throw new NotSupportedException();
    }
}
