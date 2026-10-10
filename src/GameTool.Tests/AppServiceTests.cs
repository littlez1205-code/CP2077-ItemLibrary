using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

public class AppServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-app-" + Guid.NewGuid().ToString("N"));

    public AppServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void GetInfo_ReadsQuantityAndStacks()
    {
        var path = Path.Combine(_root, "save.json");
        File.WriteAllText(path, """{"gold":3,"gem":[1,2]}""");
        var adapter = new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100), ("gem", 100)));
        var info = SaveQueryService.GetInfo(adapter, path);
        Assert.Equal(1u, info.GameVersion);
        Assert.Equal(1u, info.SaveVersion);
        var gold = Assert.Single(info.Resources, item => item.Id == "gold");
        Assert.Equal(3, gold.Quantity);
        Assert.False(gold.Ambiguous);
        Assert.Equal([3L], gold.Stacks);
        var gem = Assert.Single(info.Resources, item => item.Id == "gem");
        Assert.Null(gem.Quantity);
        Assert.True(gem.Ambiguous);
        Assert.Equal([1L, 2L], gem.Stacks);
    }

    [Fact]
    public void GetInfo_OmitsDiagnosticsTheDocumentDoesNotProvide()
    {
        var path = Path.Combine(_root, "plain.json");
        File.WriteAllText(path, """{"gold":3}""");
        var adapter = new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100)));
        var json = JsonEnvelope.Ok("fake", SaveQueryService.GetInfo(adapter, path)).ToJson();
        Assert.DoesNotContain("chunkCount", json);
    }

    [Fact]
    public void GetInfo_FlattensDiagnosticsInGivenOrder()
    {
        var path = Path.Combine(_root, "diag.json");
        File.WriteAllText(path, """{"gold":1,"_diagnostics":{"zeta":9,"alpha":2}}""");
        var adapter = new FakeJsonGameAdapter(_root, FakeJsonGameAdapter.Catalog(("gold", 100)));
        var json = JsonEnvelope.Ok("fake", SaveQueryService.GetInfo(adapter, path)).ToJson();
        var saveVersion = json.IndexOf("\"saveVersion\"", StringComparison.Ordinal);
        var zeta = json.IndexOf("\"zeta\"", StringComparison.Ordinal);
        var alpha = json.IndexOf("\"alpha\"", StringComparison.Ordinal);
        var resources = json.IndexOf("\"resources\"", StringComparison.Ordinal);
        Assert.True(saveVersion >= 0 && saveVersion < zeta && zeta < alpha && alpha < resources);
        Assert.DoesNotContain("chunkCount", json);
        Assert.DoesNotContain("_diagnostics", json);
    }

    [Fact]
    public void ChangePlanner_FillMoneyAndMaterials()
    {
        var resources = SampleResources();
        var filled = ChangePlanner.Plan(resources, fill: true, money: null, materials: null);
        Assert.Equal(2, filled.Count);
        Assert.Equal(10, filled[0].TargetQuantity);
        Assert.Equal(5, filled[1].TargetQuantity);

        var money = ChangePlanner.Plan(resources, fill: false, money: 40, materials: null);
        var only = Assert.Single(money);
        Assert.Equal("money", only.Id.Value);
        Assert.Equal(40, only.TargetQuantity);

        var materials = ChangePlanner.Plan(resources, fill: false, money: null, materials: 7);
        var material = Assert.Single(materials);
        Assert.Equal("ore", material.Id.Value);
        Assert.Equal(7, material.TargetQuantity);
    }

    [Fact]
    public void ChangePlanner_RejectsMissingOrDuplicateCurrency()
    {
        var missing = new[]
        {
            new ResourceDescriptor(new ResourceId("ore"), "矿", "Ore", "material", 100, 5, "json:ore")
        };
        var missingError = Assert.Throws<InvalidDataException>(() =>
            ChangePlanner.Plan(missing, fill: false, money: 1, materials: null));
        Assert.Equal("找不到 group 为 currency 的资源。", missingError.Message);

        var duplicate = new[]
        {
            new ResourceDescriptor(new ResourceId("money"), "欧元", "Euro", "currency", 100, 10, "json:money"),
            new ResourceDescriptor(new ResourceId("coin"), "硬币", "Coin", "currency", 100, 1, "json:coin")
        };
        var duplicateError = Assert.Throws<InvalidDataException>(() =>
            ChangePlanner.Plan(duplicate, fill: false, money: 1, materials: null));
        Assert.Equal("group 为 currency 的资源不止一个。", duplicateError.Message);
    }

    [Fact]
    public void ErrorMapper_MapsEachException()
    {
        Assert.Equal((3, "unsupported_save"), Codes(new UnsupportedSaveException(0, 0, "版本")));
        Assert.Equal((1, "failed"), Codes(new PatchRejectedException("拒绝")));
        Assert.Equal((1, "failed"), Codes(new AmbiguousResourceException(new ResourceId("gold"))));
        Assert.Equal((1, "failed"), Codes(new InvalidDataException("数据")));
        Assert.Equal((1, "failed"), Codes(new IOException("io")));
        Assert.Equal((1, "failed"), Codes(new UnauthorizedAccessException("denied")));
        Assert.Equal((1, "internal"), Codes(new InvalidOperationException("内部")));
    }

    private static (int ExitCode, string Code) Codes(Exception exception)
    {
        var mapped = ErrorMapper.Map(exception);
        Assert.Equal(exception.Message, mapped.Message);
        return (mapped.ExitCode, mapped.Code);
    }

    private static ResourceDescriptor[] SampleResources() =>
    [
        new(new ResourceId("money"), "欧元", "Euro", "currency", 100, 10, "json:money"),
        new(new ResourceId("ore"), "矿", "Ore", "material", 100, 5, "json:ore")
    ];
}
