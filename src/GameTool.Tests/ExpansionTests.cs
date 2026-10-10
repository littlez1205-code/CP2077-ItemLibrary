using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;

namespace GameTool.Tests;

/// <summary>D023：没买往日之影的玩家。tweak 源里的往日之影记录、字段级 [ EP1 ]、目录隐藏和游戏目录检测。</summary>
public sealed class ExpansionTests : IDisposable
{
    private const string BaseTweaks = """
        package RTDB
        Item
        {
        }

        [ notQueryable ]
        Clothing : Item
        {
        }

        WeaponItem : Item
        {
        }

        package Items
        using RTDB
        BaseJacket : Clothing
        {
        	displayName = "LocKey#1";
        }

        [ EP1 ]
        Ep1Hat : Clothing
        {
        	displayName = "LocKey#2";
        }

        Ep1HatRecolor : Ep1Hat
        {
        	displayName = "LocKey#3";
        }

        [ EP1 ]
        Ep1Nameless : Clothing
        {
        	displayName = "LocKey#99";
        }

        PackChild : PackCoat
        {
        	displayName = "LocKey#4";
        }

        BaseRifle : WeaponItem
        {
        	displayName = "LocKey#5";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.base_rcv"; }
        	];
        }

        Ep1PartRifle : WeaponItem
        {
        	displayName = "LocKey#6";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.ep1_rcv"; }
        	];
        }

        base_rcv
        {
        }

        GatedJacket : Clothing
        {
        	displayName = "LocKey#7";
        	quality = "Quality.Common";
        	[ EP1 ]
        	tags +=
        	[
        		"Ep1Tag"
        	];
        	[ EP1 ]
        	craftingResult =
        	{
        		item = "Items.Ep1Hat";
        	};
        	[ EP1 ]
        	quality = "Quality.Legendary";
        	appearanceName = "gated_jacket";
        }
        """;

    private const string Ep1Tweaks = """
        package Items
        using RTDB
        PackCoat : Clothing
        {
        	displayName = "LocKey#8";
        }

        ep1_rcv
        {
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(1, "", "本体夹克", ""),
        new(2, "", "往日之影帽子", ""),
        new(3, "", "往日之影帽子改色", ""),
        new(4, "", "继承资料片外套", ""),
        new(5, "", "本体步枪", ""),
        new(6, "", "资料片配件步枪", ""),
        new(7, "", "字段夹克", ""),
        new(8, "", "资料片外套", "")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-ep1-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // 这里只有测试自己建的普通文件和文件夹（没有目录联接），可以整树删除。
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TweakSourceIndex Index(bool installed) =>
        TweakSourceIndex.Parse([("base.tweak", BaseTweaks)], [("ep1.tweak", Ep1Tweaks)], installed);

    [Fact]
    public void IsExpansion_CoversEp1Files_Attributes_Inheritance_PresetParts_AndCrafting()
    {
        foreach (var installed in new[] { true, false })
        {
            var index = Index(installed);
            Assert.Equal(installed, index.ExpansionInstalled);
            Assert.False(index.IsExpansion("Items.BaseJacket"));
            Assert.False(index.IsExpansion("Items.BaseRifle"));
            Assert.False(index.IsExpansion("RTDB.Clothing"));
            Assert.True(index.IsExpansion("Items.Ep1Hat"), "带 [ EP1 ] 属性");
            Assert.True(index.IsExpansion("Items.Ep1HatRecolor"), "父记录带 [ EP1 ]");
            Assert.True(index.IsExpansion("Items.PackCoat"), "定义在 tweaks\\ep1");
            Assert.True(index.IsExpansion("Items.PackChild"), "父记录定义在 tweaks\\ep1");
            Assert.True(index.IsExpansion("Items.Ep1PartRifle"), "配件预设引用 ep1 配件");
            Assert.False(index.IsExpansion("Items.Missing"));
        }

        // 配方产物是往日之影记录：只在装了往日之影时 craftingResult 才生效（字段带 [ EP1 ]），没装时这条字段不算，记录也不算往日之影记录。
        Assert.True(Index(installed: true).IsExpansion("Items.GatedJacket"));
        Assert.False(Index(installed: false).IsExpansion("Items.GatedJacket"));
    }

    [Fact]
    public void FieldLevelEp1_IsIgnoredOnlyWithoutTheExpansion()
    {
        var full = Index(installed: true);
        Assert.Equal("Quality.Legendary", full.FieldOf("Items.GatedJacket", "quality"));
        Assert.Equal(new[] { "Ep1Tag" }, full.TagsOf("Items.GatedJacket"));
        Assert.Equal("Items.Ep1Hat", full.FieldOf("Items.GatedJacket", TweakSourceIndex.CraftingResultItem));
        Assert.Equal("gated_jacket", full.FieldOf("Items.GatedJacket", "appearanceName"));

        var baseOnly = Index(installed: false);
        Assert.Equal("Quality.Common", baseOnly.FieldOf("Items.GatedJacket", "quality"));
        Assert.Empty(baseOnly.TagsOf("Items.GatedJacket"));
        Assert.Null(baseOnly.FieldOf("Items.GatedJacket", TweakSourceIndex.CraftingResultItem));
        // 被跳过的字段之后的普通字段照常读到，记录本身和其他记录都不受影响。
        Assert.Equal("gated_jacket", baseOnly.FieldOf("Items.GatedJacket", "appearanceName"));
        Assert.Equal("LocKey#7", baseOnly.Find(TweakDbId.FromName("Items.GatedJacket"))!.DisplayName);
        Assert.Equal(full.RecordNames.Order(StringComparer.Ordinal), baseOnly.RecordNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalog_WithoutTheExpansion_HidesExpansionItems_AndRejectsThem()
    {
        var full = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(Index(installed: true), Texts));
        Assert.True(full.ExpansionInstalled);
        Assert.Equal(
            new[] { "Items.BaseJacket", "Items.Ep1Hat", "Items.Ep1HatRecolor", "Items.GatedJacket", "Items.PackChild", "Items.PackCoat" },
            full.Gear.Where(entry => entry.Kind == GearKinds.Clothing).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Items.BaseRifle", "Items.Ep1PartRifle" }, full.Gear.Where(entry => entry.Kind == GearKinds.Ranged).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Empty(full.HiddenGroups);
        Assert.False(full.IsHidden("Items.Ep1Hat"));

        var baseOnly = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(Index(installed: false), Texts));
        Assert.False(baseOnly.ExpansionInstalled);
        Assert.Equal(
            new[] { "Items.BaseJacket", "Items.GatedJacket" },
            baseOnly.Gear.Where(entry => entry.Kind == GearKinds.Clothing).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal("Items.BaseRifle", Assert.Single(baseOnly.Gear, entry => entry.Kind == GearKinds.Ranged).RecordName);
        Assert.Equal(
            new[] { "Items.Ep1Hat", "Items.Ep1HatRecolor", "Items.Ep1Nameless", "Items.Ep1PartRifle", "Items.PackChild", "Items.PackCoat" },
            baseOnly.HiddenGroups.SelectMany(group => group.Variants).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal(5, baseOnly.HiddenGroups.Count(group => group.Kind == GearKinds.Clothing));
        // 查不到中文名（没有 ep1 文本包）的往日之影物品也记进隐藏目录，用记录名当标签；装了往日之影时它照旧因为没有名字不进目录。
        Assert.Equal("Ep1Nameless", baseOnly.HiddenGroups.Single(group => group.Variants[0].RecordName == "Items.Ep1Nameless").Label);
        Assert.True(baseOnly.IsHidden("Items.Ep1Nameless"));
        Assert.DoesNotContain(full.Gear, entry => entry.RecordName == "Items.Ep1Nameless");
        Assert.True(baseOnly.IsHidden("Items.Ep1HatRecolor"));
        Assert.True(baseOnly.IsHidden("Items.Ep1PartRifle"));
        Assert.False(baseOnly.IsHidden("Items.BaseJacket"));
        Assert.False(baseOnly.IsHidden("Items.Missing"));
        // 本体模式下字段级 [ EP1 ] 的品质不生效：按本体定义是普通品质。
        Assert.Equal(GearQuality.Common, baseOnly.Gear.Single(entry => entry.RecordName == "Items.GatedJacket").Quality);
        Assert.Equal(GearQuality.Legendary, full.Gear.Single(entry => entry.RecordName == "Items.GatedJacket").Quality);
        // 模板分类不看是否隐藏：往日之影物品仍能被认出类别。
        Assert.Equal(GearKinds.Clothing, baseOnly.KindOf(TweakDbId.FromName("Items.Ep1Hat")));
    }

    [Fact]
    public void TryLoad_ReadsBaseAndEp1Folders_AndEp1IsOptional()
    {
        var tweaks = Path.Combine(_root, "tweaks");
        Directory.CreateDirectory(Path.Combine(tweaks, "base", "items"));
        File.WriteAllText(Path.Combine(tweaks, "base", "items", "a.tweak"), BaseTweaks);
        var withoutEp1 = TweakSourceIndex.TryLoad(tweaks, expansionInstalled: false, out var unavailable)!;
        Assert.Null(unavailable);
        Assert.DoesNotContain("Items.PackCoat", withoutEp1.RecordNames);
        Assert.True(withoutEp1.IsExpansion("Items.Ep1Hat"));
        // 父记录只在 ep1 源里：没有 ep1 源时继承链断开，记录本身不是往日之影记录，但也分不出类别、进不了目录。
        Assert.False(withoutEp1.IsExpansion("Items.PackChild"));
        Assert.Equal(new[] { "Items.PackChild" }, withoutEp1.Ancestors("Items.PackChild"));

        Directory.CreateDirectory(Path.Combine(tweaks, "ep1"));
        File.WriteAllText(Path.Combine(tweaks, "ep1", "b.tweak"), Ep1Tweaks);
        var withEp1 = TweakSourceIndex.TryLoad(tweaks, expansionInstalled: false, out _)!;
        Assert.True(withEp1.IsExpansion("Items.PackCoat"));
        Assert.True(withEp1.IsExpansion("Items.PackChild"));
        Assert.True(TweakSourceIndex.TryLoad(tweaks, out _)!.ExpansionInstalled);
        Assert.Null(TweakSourceIndex.TryLoad(Path.Combine(_root, "missing"), expansionInstalled: false, out var missing));
        Assert.Equal(ItemNameCatalog.RedmodUnavailable, missing);
    }

    [Fact]
    public void Detection_NeedsAnEp1ContentArchive_AndTheEp1TweakDb()
    {
        var game = FakeGame("detect");
        Assert.False(Cp2077Paths.HasPhantomLiberty(game), "没有 archive\\pc\\ep1");

        var ep1 = Path.Combine(game, "archive", "pc", "ep1");
        Directory.CreateDirectory(ep1);
        var tweakDb = Path.Combine(game, "r6", "cache", "tweakdb_ep1.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(tweakDb)!);
        File.WriteAllBytes(tweakDb, [1]);
        Assert.False(Cp2077Paths.HasPhantomLiberty(game), "ep1 文件夹是空的");

        File.WriteAllBytes(Path.Combine(ep1, "lang_zh-cn_text.archive"), [1]);
        File.WriteAllBytes(Path.Combine(ep1, "audio_1_general.archive"), [1]);
        File.WriteAllBytes(Path.Combine(ep1, "ep1.addcont_keystone"), [1]);
        Assert.False(Cp2077Paths.HasPhantomLiberty(game), "只有语言包、音频包和 keystone");

        var content = Path.Combine(ep1, "ep1_2_gamedata.archive");
        File.WriteAllBytes(content, []);
        Assert.False(Cp2077Paths.HasPhantomLiberty(game), "内容归档是空文件");

        File.WriteAllBytes(content, [1, 2, 3]);
        Assert.True(Cp2077Paths.HasPhantomLiberty(game));

        File.Delete(tweakDb);
        Assert.False(Cp2077Paths.HasPhantomLiberty(game), "缺 tweakdb_ep1.bin");
    }

    [Fact]
    public void CheckGameDirectory_ReportsTheDetection()
    {
        var game = FakeGame("check");
        var baseOnly = Cp2077Paths.CheckGameDirectory(game);
        Assert.True(baseOnly.Ok);
        Assert.Equal(Cp2077Paths.DetectedBaseOnly, baseOnly.Detected);
        Assert.Equal("已检测到：仅本体（往日之影物品将不显示）", baseOnly.Detected);
        Assert.False(baseOnly.Expansions![Cp2077Paths.PhantomLibertyKey]);

        Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "ep1"));
        File.WriteAllBytes(Path.Combine(game, "archive", "pc", "ep1", "ep1_1_nightcity.archive"), [1]);
        Directory.CreateDirectory(Path.Combine(game, "r6", "cache"));
        File.WriteAllBytes(Path.Combine(game, "r6", "cache", "tweakdb_ep1.bin"), [1]);
        // 选到 bin\x64 也会改用根目录，检测按根目录做。
        var withExpansion = Cp2077Paths.CheckGameDirectory(Path.Combine(game, "bin", "x64"));
        Assert.True(withExpansion.Ok);
        Assert.Equal("已检测到：本体 + 往日之影", withExpansion.Detected);
        Assert.True(withExpansion.Expansions![Cp2077Paths.PhantomLibertyKey]);

        var bad = Cp2077Paths.CheckGameDirectory(Path.Combine(_root, "nothing"));
        Assert.False(bad.Ok);
        Assert.Null(bad.Detected);
        Assert.Null(bad.Expansions);
    }

    /// <summary>最小的游戏目录：bin\x64\Cyberpunk2077.exe（空文件）和 archive\pc\content。</summary>
    private string FakeGame(string name)
    {
        var game = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(game, "bin", "x64"));
        File.WriteAllBytes(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), []);
        Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "content"));
        return game;
    }
}
