using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;
using GameTool.Core;
using GameTool.Tests.Fake;

namespace GameTool.Tests;

/// <summary>C2b 正式功能：可生成目录、模板选择、生成与严格校验、PatchEngine / App / Bridge 接入。</summary>
public class GearGenerateTests : IDisposable
{
    private const string Tweaks = """
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

        [ Debug ]
        package Items
        using RTDB
        Jacket_01_basic_01 : Clothing
        {
        	displayName = "LocKey#1";
        }

        SQ031_Samurai_Jacket : Clothing
        {
        	displayName = "LocKey#2";
        	[ EP1 ]
        	quality = "Quality.Rare";
        }

        Jacket_Twin : Clothing
        {
        	displayName = "LocKey#1";
        }

        [ notQueryable ]
        HiddenCoat : Clothing
        {
        	displayName = "LocKey#3";
        }

        [ Debug ]
        [ EP1 ]
        DebugHat : Clothing
        {
        	displayName = "LocKey#3";
        }

        [ DisabledContent ]
        DisabledPants : Clothing
        {
        	displayName = "LocKey#3";
        }

        [ abstract ]
        AbstractShoe : Clothing
        {
        	displayName = "LocKey#3";
        }

        NoTextShirt : Clothing
        {
        	displayName = "LocKey#999";
        }

        MaskCWPlusPlus : Clothing
        {
        	displayName = "LocKey#4";
        }

        Preset_MA70_Default : WeaponItem
        {
        	displayName = "LocKey#5";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_lmg__midnight_ma70__rcv1"; },
        		{ slot = "AttachmentSlots.Magazine"; itemPartPreset = "Items.w_lmg__midnight_ma70__mag_std"; }
        	];
        }

        Preset_Ajax_Default : WeaponItem
        {
        	displayName = "LocKey#6";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_rifle_assault__militech_ajax__rcv1"; },
        		{ slot = "AttachmentSlots.Barrel"; itemPartPreset = "Items.w_rifle_assault__militech_ajax__base1"; },
        		{ slot = "AttachmentSlots.Magazine"; itemPartPreset = "Items.w_rifle_assault__militech_ajax__mag_std"; },
        		{ slot = "AttachmentSlots.MagazineEmpty"; itemPartPreset = "Items.w_rifle_assault__militech_ajax__mag_stdr"; }
        	];
        }

        Katana : WeaponItem
        {
        	displayName = "LocKey#7";
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(1, "", "", "夹克"),
        new(2, "", "", "武士夹克"),
        new(3, "", "", "隐藏"),
        new(4, "", "", "面罩"),
        new(5, "", "", "MA70"),
        new(6, "", "", "阿贾克斯"),
        new(7, "", "", "武士刀")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-gear-" + Guid.NewGuid().ToString("N"));
    private readonly Cp2077ItemNames _names;
    private readonly Cp2077Adapter _adapter;

    public GearGenerateTests()
    {
        Directory.CreateDirectory(_root);
        var index = TweakSourceIndex.Parse([("a.tweak", Tweaks)]);
        _names = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(index, Texts));
        _adapter = new Cp2077Adapter(() => _names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Attributes_AttachToTheNextRecordOnly()
    {
        var index = TweakSourceIndex.Parse([("a.tweak", Tweaks)]);
        Assert.Equal(new[] { "Debug", "EP1" }, index.AttributesOf("Items.DebugHat"));
        Assert.Equal(new[] { "notQueryable" }, index.AttributesOf("RTDB.Clothing"));
        Assert.Empty(index.AttributesOf("Items.Jacket_01_basic_01"));
        Assert.Empty(index.AttributesOf("Items.Jacket_Twin"));
        Assert.Empty(index.AttributesOf("Items.SQ031_Samurai_Jacket"));
        Assert.Empty(index.AttributesOf("Items.Missing"));
        Assert.Equal(new[] { "Items.Jacket_01_basic_01", "RTDB.Clothing", "RTDB.Item" }, index.Ancestors("Items.Jacket_01_basic_01"));
        Assert.Equal("LocKey#2", index.Find(TweakDbId.FromName("Items.SQ031_Samurai_Jacket"))!.DisplayName);

        var overridden = TweakSourceIndex.Parse(
        [
            ("base.tweak", """
                package Items
                [ notQueryable ]
                Food
                {
                }
                """),
            ("ep1.tweak", """
                package Items
                [ EP1 ]
                Food
                {
                }
                """)
        ]);
        Assert.Equal(new[] { "notQueryable", "EP1" }, overridden.AttributesOf("Items.Food"));
    }

    [Fact]
    public void Catalog_ClassifiesKinds_AndFiltersAttributesAndNames()
    {
        var gear = _names.Gear;
        Assert.Equal(
            new[] { "Items.Jacket_01_basic_01", "Items.Jacket_Twin", "Items.MaskCWPlusPlus", "Items.SQ031_Samurai_Jacket" },
            gear.Where(entry => entry.Kind == GearKinds.Clothing).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "Items.Preset_Ajax_Default", "Items.Preset_MA70_Default" },
            gear.Where(entry => entry.Kind == GearKinds.Ranged).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.All(gear.Where(entry => entry.Label == "夹克"), entry => Assert.True(entry.LabelShared));
        Assert.False(gear.Single(entry => entry.RecordName == "Items.MaskCWPlusPlus").LabelShared);
        var ajax = gear.Single(entry => entry.RecordName == "Items.Preset_Ajax_Default");
        Assert.Equal("阿贾克斯", ajax.Label);
        Assert.Equal(Hex(TweakDbId.FromName("Items.Preset_Ajax_Default")), ajax.TweakDbIdHex);

        // 分类本身不看属性和名字：模板选择要能认出这些。
        Assert.Equal(GearKinds.Clothing, _names.KindOf(TweakDbId.FromName("Items.HiddenCoat")));
        Assert.Equal(GearKinds.Clothing, _names.KindOf(TweakDbId.FromName("Items.DebugHat")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.Katana")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("RTDB.Clothing")));
        Assert.Empty(Cp2077ItemNames.FromCatalog(null).Gear);
    }

    [Fact]
    public void ListGeneratable_PagesSearchesAndMarksOwned()
    {
        var path = WriteSave("list", (1, Weapon("Items.Preset_MA70_Default", children: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.True(document.GenerateAvailable);
        Assert.Equal(new[] { "clothing", "ranged" }, document.GenerateKinds.Select(kind => kind.Id));
        var clothing = document.ListGeneratable("clothing", null, 0, 2);
        Assert.Equal(4, clothing.Total);
        Assert.Equal(2, clothing.Items.Count);
        Assert.Equal(2, document.ListGeneratable("clothing", null, 2, 50).Items.Count);
        var search = document.ListGeneratable(null, "ajax", 0, 50);
        Assert.Equal("Items.Preset_Ajax_Default", Assert.Single(search.Items).Target);
        Assert.False(search.Items[0].Stackable);
        Assert.Equal("阿贾克斯", Assert.Single(document.ListGeneratable(null, "阿贾", 0, 50).Items).Label);
        Assert.True(document.ListGeneratable("ranged", "MA70", 0, 50).Items.Single().Owned);
        Assert.Throws<InvalidDataException>(() => document.ListGeneratable(null, null, 0, 201));

        var unnamed = Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);
        Assert.False(unnamed.GenerateAvailable);
        Assert.NotNull(unnamed.GenerateMessage);
        Assert.Equal(0, unnamed.ListGeneratable(null, null, 0, 50).Total);
    }

    [Fact]
    public void TemplateSelection_PrefersGenericItemRoot_ThenAnyClothing()
    {
        var both = WriteSave("both", (1, Mask()), (ItemInserter.StashBagId, Samurai()));
        Assert.StartsWith("1000000:1:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(both), GearKinds.Clothing, _names));
        var maskOnly = WriteSave("mask", (1, Mask()));
        Assert.StartsWith("1:2:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(maskOnly), GearKinds.Clothing, _names));

        // 非标准分类的记录（HiddenCoat 不在目录里，但继承链是服装）也能当服装模板。
        var hidden = WriteSave("hidden", (1, Clothing("Items.HiddenCoat", generic: true)));
        Assert.NotNull(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(hidden), GearKinds.Clothing, _names));
    }

    [Fact]
    public void TemplateSelection_RangedNeedsAChildPart_AndMissingTemplatesAreRejected()
    {
        var bare = WriteSave("bare", (1, Weapon("Items.Preset_MA70_Default", children: false)), (1, Samurai()));
        Assert.Null(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(bare), GearKinds.Ranged, _names));
        var document = (Cp2077SaveDocument)_adapter.Load(bare);
        Assert.Contains("远程武器作为模板", Assert.Throws<InvalidDataException>(() =>
            document.Generate(new GenerateRequest("Items.Preset_Ajax_Default", 1))).Message);

        var withChild = WriteSave("child", (1, Weapon("Items.Preset_MA70_Default", children: false)), (ItemInserter.StashBagId, Weapon("Items.Preset_MA70_Default", children: true)));
        Assert.StartsWith("1000000:1:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(withChild), GearKinds.Ranged, _names));

        var noClothing = WriteSave("noclothing", (1, Weapon("Items.Preset_MA70_Default", children: true)));
        var weaponsOnly = (Cp2077SaveDocument)_adapter.Load(noClothing);
        Assert.Contains("服装作为模板", Assert.Throws<InvalidDataException>(() =>
            weaponsOnly.Generate(new GenerateRequest("Items.Jacket_01_basic_01", 1))).Message);
        Assert.Contains("不在可生成目录", Assert.Throws<InvalidDataException>(() =>
            weaponsOnly.Generate(new GenerateRequest("Items.HiddenCoat", 1))).Message);
        Assert.Contains("1 到 10", Assert.Throws<InvalidDataException>(() =>
            weaponsOnly.Generate(new GenerateRequest("Items.Preset_Ajax_Default", 11))).Message);
    }

    private const string IconicTweaks = """
        package Items
        using RTDB
        [ notQueryable ]
        Preset_Masamune_Rogue : WeaponItem
        {
        	displayName = "LocKey#8";
        	itemType = "ItemType.Wea_AssaultRifle";
        	CName appearanceResourceName = "Preset_Base_Masamune";
        	visualTags = [ "Rogue_Masamune" ];
        	tags = [ "Weapon", "IconicWeapon" ];
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_rifle_assault__arasaka_masamune__rvc1"; },
        		{ slot = "AttachmentSlots.Barrel"; itemPartPreset = "Items.w_rifle_assault__arasaka_masamune__base1"; },
        		{ slot = "AttachmentSlots.IconicWeaponModLegendary"; itemPartPreset = "Items.RogueMasamuneWeaponMod"; }
        	];
        }

        package UIIcon
        weapon_Preset_Base_Masamune_None__Rogue_Masamune
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons2.inkatlas";
        	atlasPartName = "weapon_Preset_Base_Masamune_None__Rogue_Masamune";
        }
        """;

    [Fact]
    public void Iconic_InCatalog_GeneratesWithIconicMod_ButIsNeverATemplate()
    {
        var index = TweakSourceIndex.Parse([("a.tweak", Tweaks), ("b.tweak", IconicTweaks)]);
        var names = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(index, [.. Texts, new LocEntry(8, "", "", "偏见")]));
        var adapter = new Cp2077Adapter(() => names);
        var rogue = names.Gear.Single(entry => entry.RecordName == "Items.Preset_Masamune_Rogue");
        Assert.True(rogue.Iconic);
        Assert.Equal(GearKinds.Ranged, rogue.Kind);

        var iconicOnly = WriteSave("iconic-only", (1, Weapon("Items.Preset_Masamune_Rogue", children: true)));
        Assert.Null(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(iconicOnly), GearKinds.Ranged, names));

        var path = WriteSave("iconic", (1, Weapon("Items.Preset_Masamune_Rogue", children: true)), (ItemInserter.StashBagId, Weapon("Items.Preset_MA70_Default", children: true)));
        var document = (Cp2077SaveDocument)adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.Preset_Masamune_Rogue", 1));
        Assert.StartsWith("1000000:1:", applied.TemplateKey);
        var tree = ItemPartTree.Parse(applied.Records[0].Body);
        Assert.Equal(TweakDbId.FromName("Items.w_rifle_assault__arasaka_masamune__rvc1"), tree.Root.Tweak);
        Assert.Contains(tree.Root.Children, part =>
            part.Tweak == TweakDbId.FromName("Items.RogueMasamuneWeaponMod") && part.Slot == TweakDbId.FromName("AttachmentSlots.IconicWeaponModLegendary"));
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_ThreeRangedCopies_UsesFreshUniqueRootAndPartSeeds()
    {
        var path = WriteSave("three", (1, Weapon("Items.Preset_MA70_Default", children: true)), (ItemInserter.StashBagId, Samurai()));
        var original = SaveContainer.Read(path);
        var otherIndex = OtherIndex(original);
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.Preset_Ajax_Default", 3));

        Assert.Equal(3, applied.Records.Count);
        Assert.Equal(3 * (16 + 241), applied.AddedBytes);
        Assert.StartsWith("1:2:", applied.TemplateKey);
        Assert.Equal("MA70", applied.TemplateLabel);
        var seeds = applied.Records.SelectMany(record => record.Seeds).ToArray();
        Assert.Equal(3 * 5, seeds.Length);
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.All(seeds, seed =>
        {
            Assert.True(seed > 2);
            Assert.Equal(-1, original.Payload.IndexOf(U32(seed)));
        });
        foreach (var record in applied.Records)
        {
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(TweakDbId.FromName("Items.Preset_Ajax_Default"), BinaryPrimitives.ReadUInt64LittleEndian(record.Header));
            Assert.Equal(record.Seed, BinaryPrimitives.ReadUInt32LittleEndian(record.Header.AsSpan(8)));
            Assert.Equal(TweakDbId.FromName("Items.w_rifle_assault__militech_ajax__rcv1"), tree.Root.Tweak);
            Assert.Equal(3, tree.Root.Children.Count);
            Assert.All(record.Body.AsSpan(25, 12).ToArray(), value => Assert.Equal(0, value));
            Assert.Equal(record.Seeds.Skip(1), tree.Walk().Select(entry => entry.Part.Seed));
        }

        Assert.Equal(otherIndex + 3, OtherIndex(Container(document)));
        Assert.Equal(applied.PreviousPlayerCount + 3, document.PlayerItemCount);
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_Clothing_KeepsPartTree_ReplacesRootOnly()
    {
        var path = WriteSave("keep", (ItemInserter.StashBagId, Samurai()));
        var template = Samurai();
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.Jacket_01_basic_01", 2));
        Assert.Equal(2, applied.Records.Count);
        foreach (var record in applied.Records)
        {
            Assert.Equal(91, record.Body.Length);
            Assert.Equal(new[] { record.Seed }, record.Seeds);
            Assert.Equal(template.AsSpan(20).ToArray(), record.Body.AsSpan(20).ToArray());
            Assert.Equal(TweakDbId.FromName("Items.Jacket_01_basic_01"), BinaryPrimitives.ReadUInt64LittleEndian(record.Body.AsSpan(4)));
        }

        Assert.NotEqual(applied.Records[0].Seed, applied.Records[1].Seed);
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void VerifyOnlyGenerated_RejectsTamperedBodyHeaderAndOtherItemHeader()
    {
        var path = WriteSave("strict", (1, Weapon("Items.Preset_MA70_Default", children: true)), (ItemInserter.StashBagId, Samurai()));
        const string target = "Items.Preset_Ajax_Default";

        var body = (Cp2077SaveDocument)_adapter.Load(path);
        var bodyResult = body.Generate(new GenerateRequest(target, 1));
        Flip(body, bodyResult.Records[0].Body, 60);
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => body.VerifyOnlyGenerated(Open(path), bodyResult)).Message);

        var header = (Cp2077SaveDocument)_adapter.Load(path);
        var headerResult = header.Generate(new GenerateRequest(target, 1));
        var at = Container(header).Payload.IndexOf(headerResult.Records[0].Body);
        Container(header).WritePayload(at - 16 + 14, [0x7E]);
        Assert.Contains("物品头与生成结果不一致", Assert.Throws<InvalidDataException>(() => header.VerifyOnlyGenerated(Open(path), headerResult)).Message);

        var recorded = (Cp2077SaveDocument)_adapter.Load(path);
        var recordedResult = recorded.Generate(new GenerateRequest(target, 1));
        recordedResult.Records[0].Body[70] ^= 0x7F;
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => recorded.VerifyOnlyGenerated(Open(path), recordedResult)).Message);

        // 另一件物品（储物箱里的服装）的 16 字节头被改：只有 inventory 自身字节的比对能拦下。
        var other = (Cp2077SaveDocument)_adapter.Load(path);
        var otherResult = other.Generate(new GenerateRequest(target, 1));
        var samurai = Container(other).Payload.IndexOf(Samurai().AsSpan(4, 16));
        Container(other).WritePayload(samurai - 16 + 14, [0x7E]);
        Assert.Contains("inventory 自身字节", Assert.Throws<InvalidDataException>(() => other.VerifyOnlyGenerated(Open(path), otherResult)).Message);

        var reused = (Cp2077SaveDocument)_adapter.Load(path);
        var reusedResult = reused.Generate(new GenerateRequest(target, 1));
        var fake = reusedResult with { Records = [reusedResult.Records[0] with { Seeds = [.. reusedResult.Records[0].Seeds, 0x6666] }] };
        Assert.Contains("种子", Assert.Throws<InvalidDataException>(() => reused.VerifyOnlyGenerated(Open(path), fake)).Message);
    }

    [Fact]
    public void PatchEngineGenerate_WritesBak_ChecksCounts_AndRejectsBadRequests()
    {
        var path = WriteSave("engine", (1, Weapon("Items.Preset_MA70_Default", children: true)), (1, Samurai()));
        var original = File.ReadAllBytes(path);
        var before = _adapter.Load(path);
        var money = before.GetQuantity(new ResourceId("money"));

        Assert.Throws<PatchRejectedException>(() => PatchEngine.Generate(_adapter, path, new GenerateRequest("Items.Preset_Ajax_Default", 0), new PatchOptions(path + ".bak")));
        Assert.Throws<PatchRejectedException>(() => PatchEngine.Generate(_adapter, path, new GenerateRequest(" ", 1), new PatchOptions(path + ".bak")));
        Assert.Throws<InvalidDataException>(() => PatchEngine.Generate(_adapter, path, new GenerateRequest("Items.Katana", 1), new PatchOptions(path + ".bak")));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));

        var report = PatchEngine.Generate(_adapter, path, new GenerateRequest("Items.Preset_Ajax_Default", 2), new PatchOptions(path + ".bak"));
        Assert.Equal(PatchOutcome.Patched, report.Patch.Outcome);
        Assert.Equal(2, report.Seeds.Count);
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(((Cp2077SaveDocument)before).PlayerItemCount + 2, after.PlayerItemCount);
        Assert.Equal(money, after.GetQuantity(new ResourceId("money")));

        var clothing = PatchEngine.Generate(_adapter, path, new GenerateRequest("Items.Jacket_01_basic_01", 1), new PatchOptions(null));
        Assert.StartsWith("1:3:", clothing.TemplateKey);
        Assert.Equal(before.GetQuantity(new ResourceId("money")), _adapter.Load(path).GetQuantity(new ResourceId("money")));
    }

    [Fact]
    public async Task BaseOnly_HidesExpansionItems_ReportsThem_AndRejectsGeneratingThem()
    {
        // D023：没装往日之影。ep1 源里的 Ep1Coat 不进目录，生成和物品库获得都按固定说明拒绝；本体物品照常生成。
        const string ep1 = """
            package Items
            using RTDB
            Ep1Coat : Clothing
            {
            	displayName = "LocKey#7";
            }
            """;
        var index = TweakSourceIndex.Parse([("a.tweak", Tweaks)], [("ep1.tweak", ep1)], expansionInstalled: false);
        var names = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(index, Texts));
        var adapter = new Cp2077Adapter(() => names);
        var path = WriteSave("ep1", (1, Weapon("Items.Preset_MA70_Default", children: true)), (1, Samurai()));
        var original = File.ReadAllBytes(path);
        var document = (Cp2077SaveDocument)adapter.Load(path);
        Assert.Null(document.FindGeneratable("Items.Ep1Coat"));
        Assert.Equal(0, document.ListGeneratable(null, "Ep1Coat", 0, 50).Total);
        Assert.Equal(GearCatalog.ExpansionHiddenMessage, document.HiddenReason("Items.Ep1Coat"));
        Assert.Null(document.HiddenReason("Items.Jacket_01_basic_01"));
        var hidden = Assert.Single(document.HiddenKinds);
        Assert.Equal(new GenerateHiddenKind(GearKinds.Clothing, "服装", 1, 1), hidden);
        Assert.Contains("往日之影", document.HiddenNotice, StringComparison.Ordinal);

        var view = GenerateService.Groups(adapter, path, GearKinds.Clothing, null, 0, 50);
        Assert.Equal(hidden, Assert.Single(view.Hidden));
        Assert.Equal(document.HiddenNotice, view.HiddenNotice);
        Assert.Equal(hidden, Assert.Single(GenerateService.Catalog(adapter, path, null, null, 0, 50).Hidden));

        var rejected = Assert.Throws<InvalidDataException>(() => PatchEngine.Generate(adapter, path, new GenerateRequest("Items.Ep1Coat", 1), new PatchOptions(null)));
        Assert.Equal("这件物品属于往日之影资料片，当前游戏目录未检测到往日之影。", rejected.Message);
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using (var enable = new EnableService(backups, _ => { }))
        {
            var library = new LibraryService(backups, enable);
            Assert.Equal(GearCatalog.ExpansionHiddenMessage, Assert.Throws<PatchRejectedException>(() => library.Obtain(adapter, path, "Items.Ep1Coat", null)).Message);
        }

        Assert.Equal(original, File.ReadAllBytes(path));
        var report = PatchEngine.Generate(adapter, path, new GenerateRequest("Items.Jacket_01_basic_01", 1), new PatchOptions(null));
        Assert.Equal(PatchOutcome.Patched, report.Patch.Outcome);

        // 装了往日之影时同一份源不隐藏任何东西。
        var full = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(TweakSourceIndex.Parse([("a.tweak", Tweaks)], [("ep1.tweak", ep1)], expansionInstalled: true), Texts));
        var fullDocument = (Cp2077SaveDocument)new Cp2077Adapter(() => full).Load(path);
        Assert.NotNull(fullDocument.FindGeneratable("Items.Ep1Coat"));
        Assert.Empty(fullDocument.HiddenKinds);
        Assert.Null(fullDocument.HiddenNotice);
        Assert.Null(fullDocument.HiddenReason("Items.Ep1Coat"));
    }

    [Fact]
    public void PatchEngineGenerate_RejectsDocumentsWithoutTheCapability()
    {
        var root = Path.Combine(_root, "fake");
        Directory.CreateDirectory(root);
        var save = Path.Combine(root, "save.json");
        File.WriteAllText(save, """{"gold":10}""");
        var fake = new FakeJsonGameAdapter(root, FakeJsonGameAdapter.Catalog(("gold", 100)));
        Assert.Contains("不能生成", Assert.Throws<PatchRejectedException>(() =>
            PatchEngine.Generate(fake, save, new GenerateRequest("x", 1), new PatchOptions(null))).Message);
        Assert.Equal("""{"gold":10}""", File.ReadAllText(save));
        Assert.False(File.Exists(save + ".tmp"));
    }

    [Fact]
    public async Task GenerateService_OnAnEnabledSlot_KeepsTheEnableTargets()
    {
        var slot = Path.Combine(_root, "saves", "ManualSave-0");
        Directory.CreateDirectory(slot);
        var save = Path.Combine(slot, "sav.dat");
        File.Copy(WriteSave("enable", (1, Weapon("Items.Preset_MA70_Default", children: true))), save);
        var events = new List<string>();
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, notice =>
        {
            lock (events)
                events.Add(notice.Event);
        });
        var money = new ResourceId("money");
        enable.Start(_adapter, save, [new ResourceChange(money, 150)], backupFirst: false, Path.Combine(_root, "saves"));
        var count = ((Cp2077SaveDocument)_adapter.Load(save)).PlayerItemCount;

        var report = GenerateService.Run(_adapter, save, new GenerateRequest("Items.Preset_Ajax_Default", 1), backupFirst: true, backups, enable);
        Assert.Equal(PatchOutcome.Patched, report.Patch.Outcome);
        Assert.Null(report.Patch.BackupPath);
        Assert.False(File.Exists(save + ".bak"));
        Assert.Single(backups.List(_adapter.GameId));
        await Task.Delay(600);

        Assert.True(enable.IsEnabled(save));
        Assert.Equal(150, Assert.Single(enable.Status.Changes).TargetQuantity);
        var after = (Cp2077SaveDocument)_adapter.Load(save);
        Assert.Equal(150, after.GetQuantity(money));
        Assert.Equal(count + 1, after.PlayerItemCount);
        lock (events)
            Assert.DoesNotContain("disabled", events);
    }

    [Fact]
    public async Task Bridge_GenerateCatalogPages_AndRunWrites()
    {
        var saves = Path.Combine(_root, "bridge-saves");
        var slot = Path.Combine(saves, "ManualSave-0");
        Directory.CreateDirectory(slot);
        var save = Path.Combine(slot, "sav.dat");
        File.Copy(WriteSave("bridge", (1, Samurai())), save);
        var registry = new AdapterRegistry();
        registry.Register(_adapter);
        await using var bridge = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "bridge-backups"), Path.Combine(_root, "bridge-skins"));
        var slotJson = JsonSerializer.Serialize(save);

        using var page = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"c1\",\"method\":\"generate.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"kind\":\"clothing\",\"take\":2}}"));
        var data = page.RootElement.GetProperty("data");
        Assert.Equal("ok", page.RootElement.GetProperty("status").GetString());
        Assert.Equal(4, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.True(data.GetProperty("namesAvailable").GetBoolean());
        Assert.Equal(2, data.GetProperty("kinds").GetArrayLength());

        using var tooMany = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"c2\",\"method\":\"generate.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"take\":500}}"));
        Assert.Equal("bad_args", tooMany.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var ran = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"g1\",\"method\":\"generate.run\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"target\":\"Items.Jacket_01_basic_01\",\"copies\":2,\"backupFirst\":false}}"));
        Assert.Equal("ok", ran.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, ran.RootElement.GetProperty("data").GetProperty("seeds").GetArrayLength());
        Assert.False(File.Exists(save + ".bak"));

        using var missing = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"g2\",\"method\":\"generate.run\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + "}}"));
        Assert.Equal("bad_args", missing.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private Cp2077SaveDocument Open(string path) =>
        Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);

    private static SaveContainer Container(Cp2077SaveDocument document) =>
        (SaveContainer)typeof(Cp2077SaveDocument).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;

    private static void Flip(Cp2077SaveDocument document, byte[] body, int offset)
    {
        var container = Container(document);
        var at = container.Payload.IndexOf(body);
        Assert.True(at > 0);
        container.WritePayload(at + offset, [(byte)(body[offset] ^ 0x7F)]);
    }

    private static int OtherIndex(SaveContainer container) =>
        NodeTable.Parse(container.CopyNodeTable()).RequireSingle("other").Index;

    private static byte[] Samurai() => Clothing("Items.SQ031_Samurai_Jacket", generic: true);

    private static byte[] Mask() => Clothing("Items.MaskCWPlusPlus", generic: false);

    internal static byte[] Clothing(string record, bool generic) =>
        ItemPartTreeTests.Body(record, 0x8888,
            generic
                ? ItemPartTreeTests.Part(TweakDbId.FromName("Items.GenericItemRoot"), 2, 0, "None", TweakDbId.FromName("AttachmentSlots.GenericItemRoot"))
                : ItemPartTreeTests.Part(0, 0, 0, "None", 0));

    internal static byte[] Weapon(string record, bool children) =>
        children
            ? ItemPartTreeTests.Body(record, 0x6666,
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__rcv1"), 0x70, 0x6FEE, "None", TweakDbId.FromName("AttachmentSlots.Receiver"),
                    ItemPartTreeTests.Part(TweakDbId.FromName("Items.DummyBlankMod5"), 0x71, 0x6FEF, "None", TweakDbId.FromName("AttachmentSlots.Power_AR_SMG_LMG_WeaponMod2")),
                    ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_std"), 0x72, 0x6FF0, "None", TweakDbId.FromName("AttachmentSlots.Magazine")),
                    ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__mag_stdr"), 0x73, 0x6FF1, "None", TweakDbId.FromName("AttachmentSlots.MagazineEmpty")),
                    ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__base1"), 0x74, 0x6FF2, "None", TweakDbId.FromName("AttachmentSlots.Barrel"))))
            : ItemPartTreeTests.Body(record, 0x6667,
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_lmg__midnight_ma70__rcv1"), 0x75, 0x6FEE, "None", TweakDbId.FromName("AttachmentSlots.Receiver")));

    /// <summary>
    /// inventory（玩家背包：100 欧元 + 给定物品；储物箱：给定物品）后面跟一个 other 节点，用来检查节点号平移。
    /// </summary>
    private string WriteSave(string name, params (ulong Bag, byte[] Body)[] items) => WriteSaveAt(_root, name, items);

    internal static string WriteSaveAt(string root, string name, params (ulong Bag, byte[] Body)[] items)
    {
        var money = TweakDbId.FromName("Items.money");
        var player = items.Where(item => item.Bag == 1).Select(item => item.Body).ToArray();
        var stash = items.Where(item => item.Bag == ItemInserter.StashBagId).Select(item => item.Body).ToArray();
        var payload = new List<byte>();
        var bodies = new List<(int Offset, int Length)>();
        payload.AddRange(U32(0));
        payload.AddRange(U32(stash.Length > 0 ? 2u : 1u));
        payload.AddRange(U64(1));
        payload.AddRange(U32((uint)(player.Length + 1)));
        payload.AddRange(U64(money));
        payload.AddRange(U32(0x5151));
        payload.AddRange(U32(0));
        bodies.Add((payload.Count, 29));
        payload.AddRange(U32(1));
        payload.AddRange(U64(money));
        payload.AddRange(U32(0x5151));
        payload.AddRange(new byte[9]);
        payload.AddRange(U32(100));
        void Append(byte[] template)
        {
            var body = template.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(body, bodies.Count + 1);
            payload.AddRange(body.AsSpan(4, 16).ToArray());
            bodies.Add((payload.Count, body.Length));
            payload.AddRange(body);
        }

        foreach (var body in player)
            Append(body);
        if (stash.Length > 0)
        {
            payload.AddRange(U64(ItemInserter.StashBagId));
            payload.AddRange(U32((uint)stash.Length));
            foreach (var body in stash)
                Append(body);
        }

        var inventoryLength = payload.Count;
        var otherIndex = bodies.Count + 1;
        payload.AddRange(U32((uint)otherIndex));
        payload.AddRange(U32(0xABCD));

        var nodes = new List<byte> { 0x45, 0x44, 0x4F, 0x4E };
        WriteCompact(nodes, otherIndex + 1);
        Node(nodes, "inventory", otherIndex, 1, 0, inventoryLength);
        for (var i = 0; i < bodies.Count; i++)
            Node(nodes, "itemData", i + 1 == bodies.Count ? -1 : i + 2, -1, bodies[i].Offset, bodies[i].Length);
        Node(nodes, "other", -1, -1, inventoryLength, 8);
        var container = SaveContainer.Create(
            Cp2077Format.SupportedSaveVersion,
            Cp2077Format.SupportedGameVersion,
            timestamp: 1,
            archiveVersion: 1,
            decompressedChunks: [payload.ToArray()],
            nodeTable: nodes.ToArray());
        var path = Path.Combine(root, name, "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
            container.Write(stream);
        return path;
    }

    private static void Node(List<byte> buffer, string name, int next, int child, int payloadOffset, int length)
    {
        WriteCompact(buffer, -Encoding.ASCII.GetByteCount(name));
        buffer.AddRange(Encoding.ASCII.GetBytes(name));
        buffer.AddRange(U32((uint)next));
        buffer.AddRange(U32((uint)child));
        buffer.AddRange(U32((uint)(Cp2077Format.FirstChunkOffset + payloadOffset)));
        buffer.AddRange(U32((uint)length));
    }

    private static void WriteCompact(List<byte> buffer, int value)
    {
        var negative = value < 0;
        var magnitude = (uint)(negative ? -value : value);
        var first = (byte)(magnitude & 0x3F);
        if (negative)
            first |= 0x80;
        magnitude >>= 6;
        if (magnitude != 0)
            first |= 0x40;
        buffer.Add(first);
        while (magnitude != 0)
        {
            var next = (byte)(magnitude & 0x7F);
            magnitude >>= 7;
            if (magnitude != 0)
                next |= 0x80;
            buffer.Add(next);
        }
    }

    private static string Hex(ulong value) => Convert.ToHexString(BitConverter.GetBytes(value)).ToLowerInvariant();

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] U64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }
}
