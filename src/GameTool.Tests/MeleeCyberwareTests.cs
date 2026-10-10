using System.Buffers.Binary;
using System.Reflection;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>T021 第二批类别：近战武器与义体的预设解析、目录分类、配件树形态、种子规则和严格校验。</summary>
public class MeleeCyberwareTests : IDisposable
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

        SlotItemPartPreset
        {
        }

        package Items
        using RTDB

        SQ031_Samurai_Jacket : Clothing
        {
        	displayName = "LocKey#2";
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

        GenericItemRootPreset : SlotItemPartPreset
        {
        	slot = "AttachmentSlots.GenericItemRoot";
        	itemPartPreset = "Items.GenericItemRoot";
        }

        StatsShardSlotPart : SlotItemPartPreset
        {
        	slot = "AttachmentSlots.StatsShardSlot";
        }

        BladeModList_Slot1 : SlotItemPartPreset
        {
        	itemPartPreset = "";
        }

        [ notQueryable ]
        Base_Katana : WeaponItem
        {
        	itemType = "ItemType.Wea_Katana";
        	fk< SlotItemPartListElement >[] slotPartList =
        	[
        		{
        			slot = "AttachmentSlots.Grip";
        			itemPartList = [ { item = "Items.w_katana_grip"; } ];
        		}
        	];
        }

        Preset_Katana_Default : Base_Katana
        {
        	displayName = "LocKey#10";
        	CName[] visualTags = [ "Default" ];
        	CName appearanceResourceName = "Preset_Katana_Default";
        	fk< SlotItemPartPreset >[] slotPartListPreset =
        	[
        		{
        			slot = "AttachmentSlots.Grip";
        			itemPartPreset = "Items.w_katana_grip";
        		},
        		{
        			slot = "AttachmentSlots.Blade_WeaponMod1";
        		} : BladeModList_Slot1,
        		{
        			slot = "AttachmentSlots.Blade_WeaponMod2";
        		} : BladeModList_Slot2
        	];
        }

        [ notQueryable ]
        Preset_Katana_Saburo : Preset_Katana_Default
        {
        	displayName = "LocKey#11";
        	CName[] visualTags = [ "Saburo" ];
        	CName appearanceResourceName = "Preset_Katana_Saburo";
        	tags = [ "Weapon", "IconicWeapon" ];
        	slotPartListPreset +=
        	[
        		{ slot = "AttachmentSlots.IconicMeleeWeaponMod1"; itemPartPreset = "Items.SaburoKatanaWeaponMod"; }
        	];
        }

        [ notQueryable ]
        Preset_Katana_Saburo_NPC : Preset_Katana_Saburo
        {
        }

        [ notQueryable ]
        Cyberware : Item
        {
        	itemType = "ItemType.Cyberware";
        	tags = [ "Cyberware", "HideInBackpackUI" ];
        }

        [ notQueryable ]
        FrontalCortexModule : Cyberware
        {
        	fk< EquipmentArea > equipArea = "EquipmentArea.FrontalCortexCW";
        }

        [ notQueryable ]
        AdvancedFrontalCortexModule : FrontalCortexModule
        {
        	slotPartListPreset =
        	[
        		"Items.GenericItemRootPreset"
        	];
        }

        AdvancedRamUpgradeCommon : AdvancedFrontalCortexModule
        {
        	displayName = "LocKey#20";
        	iconPath = "cw_frontalcortex_ram";
        	quality = "Quality.Common";
        	slotPartListPreset +=
        	[
        		{
        			itemPartPreset = "Items.AdvancedRamUpgradeStatsShard";
        		} : StatsShardSlotPart
        	];
        	nextUpgradeItem = "Items.AdvancedRamUpgradeCommonPlus";
        }

        [ notQueryable ]
        AdvancedRamUpgradeCommonPlus : AdvancedRamUpgradeCommon
        {
        	nextUpgradeItem = "";
        }

        [ notQueryable ]
        AdvancedRamUpgradeOrphan : AdvancedRamUpgradeCommon
        {
        }

        [ notQueryable ]
        EyesModule : Cyberware
        {
        	fk< EquipmentArea > equipArea = "EquipmentArea.EyesCW";
        }

        AdvancedKiroshiOptics : EyesModule
        {
        	displayName = "LocKey#21";
        	iconPath = "cw_eyes_kiroshi";
        	slotPartListPreset =
        	[
        		"Items.GenericItemRootPreset", {
        			itemPartPreset = "Items.AdvancedKiroshiOpticsStatsShard";
        		} : StatsShardSlotPart
        	];
        }

        AdvancedReversePowerInductor : AdvancedFrontalCortexModule
        {
        	displayName = "LocKey#22";
        	iconPath = "cw_frontalcortex_ram";
        }

        MantisBlades : Cyberware
        {
        	displayName = "LocKey#23";
        	iconPath = "cw_eyes_kiroshi";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.CyberwareRoot"; itemPartPreset = "Items.MantisBladesRoot"; },
        		{ itemPartPreset = "Items.MantisBladesStatsShard"; } : StatsShardSlotPart
        	];
        }

        SandevistanC1MK1 : Cyberware
        {
        	displayName = "LocKey#24";
        	iconPath = "cw_eyes_kiroshi";
        }

        package UIIcon
        cw_frontalcortex_ram
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/cyberware.inkatlas";
        	atlasPartName = "cw_frontalcortex_ram";
        }

        cw_eyes_kiroshi
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/cyberware.inkatlas";
        	atlasPartName = "cw_eyes_kiroshi";
        }

        weapon_Preset_Katana_Default_None__Default
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons2.inkatlas";
        	atlasPartName = "katana";
        }

        weapon_Preset_Katana_Saburo_None__Saburo
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons2.inkatlas";
        	atlasPartName = "katana_saburo";
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(2, "", "", "武士夹克"),
        new(5, "", "", "MA70"),
        new(10, "", "", "武士刀"),
        new(11, "", "", "觉"),
        new(20, "", "", "RAM 升级"),
        new(21, "", "", "岐路司义眼"),
        new(22, "", "", "反向电感"),
        new(23, "", "", "螳螂刀"),
        new(24, "", "", "斯安威斯坦")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-t021-" + Guid.NewGuid().ToString("N"));
    private readonly TweakSourceIndex _index;
    private readonly Cp2077ItemNames _names;
    private readonly Cp2077Adapter _adapter;

    public MeleeCyberwareTests()
    {
        Directory.CreateDirectory(_root);
        _index = TweakSourceIndex.Parse([("a.tweak", Tweaks)]);
        _names = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(_index, Texts));
        _adapter = new Cp2077Adapter(() => _names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string[] Flat(IReadOnlyList<TweakPresetPart>? parts) => parts!.Select(part => part.Slot + "=" + part.Item).ToArray();

    [Fact]
    public void PresetParts_ResolveBareStringReferences_AndTemplateSlots()
    {
        Assert.Equal(new[] { "AttachmentSlots.GenericItemRoot=Items.GenericItemRoot" }, Flat(_index.FindPresetParts("Items.AdvancedFrontalCortexModule")));
        Assert.Equal(
            new[] { "AttachmentSlots.GenericItemRoot=Items.GenericItemRoot", "AttachmentSlots.StatsShardSlot=Items.AdvancedRamUpgradeStatsShard" },
            Flat(_index.FindPresetParts("Items.AdvancedRamUpgradeCommon")));
        // 子记录没写列表：沿父记录继承，+= 已接好。
        Assert.Equal(Flat(_index.FindPresetParts("Items.AdvancedRamUpgradeCommon")), Flat(_index.FindPresetParts("Items.AdvancedRamUpgradeCommonPlus")));
        // 同一列表里裸字符串后面紧跟 { … } : 模板。
        Assert.Equal(
            new[] { "AttachmentSlots.GenericItemRoot=Items.GenericItemRoot", "AttachmentSlots.StatsShardSlot=Items.AdvancedKiroshiOpticsStatsShard" },
            Flat(_index.FindPresetParts("Items.AdvancedKiroshiOptics")));
        // 模组槽：模板把 itemPartPreset 置空或没有这个模板，都不产生配件。
        Assert.Equal(new[] { "AttachmentSlots.Grip=Items.w_katana_grip" }, Flat(_index.FindPresetParts("Items.Preset_Katana_Default")));
        Assert.Equal(
            new[] { "AttachmentSlots.Grip=Items.w_katana_grip", "AttachmentSlots.IconicMeleeWeaponMod1=Items.SaburoKatanaWeaponMod" },
            Flat(_index.FindPresetParts("Items.Preset_Katana_Saburo")));
        // slotPartList（随机列表）不是预设。
        Assert.Empty(_index.FindPresetParts("Items.Base_Katana")!);
        Assert.Equal("AttachmentSlots.StatsShardSlot", _index.FieldOf("Items.StatsShardSlotPart", "slot"));
        Assert.Equal("EquipmentArea.FrontalCortexCW", _index.FieldOf("Items.AdvancedRamUpgradeCommon", "equipArea"));
    }

    [Fact]
    public void PresetParts_TemplatesFollowPackageUsingsAndChains_OwnFieldsWin()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("base.tweak", """
                package Slots
                Root
                {
                	slot = "AttachmentSlots.GenericItemRoot";
                	itemPartPreset = "Items.GenericItemRoot";
                }

                Shard : Root
                {
                	slot = "AttachmentSlots.StatsShardSlot";
                	itemPartPreset = "Items.DefaultShard";
                }

                ChildShard : Shard
                {
                }
                """),
            ("items.tweak", """
                package Items
                using Slots
                Thing
                {
                	slotPartListPreset = [ "Slots.Root", { itemPartPreset = "Items.MyShard"; } : ChildShard, { slot = "AttachmentSlots.Own"; } : Shard, "Missing.Record", { } : NoSuchTemplate, "Items.Thing" ];
                }
                """)
        ]);
        Assert.Equal(
            new[]
            {
                "AttachmentSlots.GenericItemRoot=Items.GenericItemRoot",
                "AttachmentSlots.StatsShardSlot=Items.MyShard",
                "AttachmentSlots.Own=Items.DefaultShard"
            },
            Flat(index.FindPresetParts("Items.Thing")));
    }

    [Fact]
    public void Catalog_ClassifiesMeleeAndCyberware()
    {
        var gear = _names.Gear;
        string[] Of(string kind) => gear.Where(entry => entry.Kind == kind).Select(entry => entry.RecordName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Items.Preset_Katana_Default", "Items.Preset_Katana_Saburo" }, Of(GearKinds.Melee));
        Assert.Equal(
            new[] { "Items.AdvancedKiroshiOptics", "Items.AdvancedRamUpgradeCommon", "Items.AdvancedRamUpgradeCommonPlus", "Items.AdvancedReversePowerInductor" },
            Of(GearKinds.Cyberware));
        Assert.Equal(new[] { "Items.Preset_MA70_Default" }, Of(GearKinds.Ranged));

        var saburo = gear.Single(entry => entry.RecordName == "Items.Preset_Katana_Saburo");
        Assert.True(saburo.Iconic);
        Assert.Equal("武士刀", saburo.TypeLabel);
        var ram = gear.Single(entry => entry.RecordName == "Items.AdvancedRamUpgradeCommon");
        Assert.Equal("前额叶皮质", ram.TypeLabel);
        Assert.Equal(GearQuality.Common, ram.Quality);
        Assert.Equal("视觉系统", gear.Single(entry => entry.RecordName == "Items.AdvancedKiroshiOptics").TypeLabel);

        // 分类本身不看属性：NPC 版、孤立的升级档仍认得出类别，只是不进目录。
        Assert.Equal(GearKinds.Melee, _names.KindOf(TweakDbId.FromName("Items.Preset_Katana_Saburo_NPC")));
        Assert.Equal(GearKinds.Cyberware, _names.KindOf(TweakDbId.FromName("Items.AdvancedRamUpgradeOrphan")));
        // 不是 GenericItemRoot 形态的义体（手臂义体）、没有预设的义体、没有握柄预设的近战基类都不分类。
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.MantisBlades")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.SandevistanC1MK1")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.Base_Katana")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.Cyberware")));

        var path = WriteSave("kinds", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(new[] { "clothing", "ranged", "melee", "cyberware" }, document.GenerateKinds.Select(kind => kind.Id));
        Assert.Equal(new[] { "服装", "远程武器", "近战武器", "义体" }, document.GenerateKinds.Select(kind => kind.Label));
        Assert.Equal(2, document.ListGeneratable("melee", null, 0, 50).Total);
        Assert.Equal("Items.AdvancedRamUpgradeCommonPlus", Assert.Single(document.ListGeneratable("cyberware", "RamUpgradeCommonPlus", 0, 50).Items).Target);
    }

    [Fact]
    public void ShapeAndUpgradeRules()
    {
        TweakPresetPart Root() => new("AttachmentSlots.GenericItemRoot", "Items.GenericItemRoot");
        TweakPresetPart Shard(string item = "Items.Shard") => new("AttachmentSlots.StatsShardSlot", item);
        Assert.True(GearCatalog.IsCyberwareShape([Root()]));
        Assert.True(GearCatalog.IsCyberwareShape([Root(), Shard()]));
        Assert.False(GearCatalog.IsCyberwareShape([Root(), Shard(), Shard()]));
        Assert.False(GearCatalog.IsCyberwareShape([Shard(), Root()]));
        Assert.False(GearCatalog.IsCyberwareShape([Root(), Shard("")]));
        Assert.False(GearCatalog.IsCyberwareShape([new("AttachmentSlots.CyberwareRoot", "Items.MantisBladesRoot"), Shard()]));
        Assert.False(GearCatalog.IsCyberwareShape([]));
        Assert.False(GearCatalog.IsCyberwareShape(null));

        var targets = new HashSet<string>(StringComparer.Ordinal) { "Items.RamPlus" };
        Assert.True(GearCatalog.IsUpgradeTier("Items.RamPlus", ["notQueryable", "EP1"], targets));
        Assert.False(GearCatalog.IsUpgradeTier("Items.RamPlus", [], targets));
        Assert.False(GearCatalog.IsUpgradeTier("Items.RamPlus", ["notQueryable", "Debug"], targets));
        Assert.False(GearCatalog.IsUpgradeTier("Items.Other", ["notQueryable"], targets));
    }

    [Fact]
    public void Icons_MeleeUsesGeneratedName_CyberwareUsesIconPath()
    {
        var resolver = _names.IconResolver!;
        Assert.Equal("weapon_Preset_Katana_Default_None__Default", resolver.Resolve("Items.Preset_Katana_Default", IconGender.Female)!.Icon.Name);
        Assert.Equal("weapon_Preset_Katana_Saburo_None__Saburo", resolver.Resolve("Items.Preset_Katana_Saburo", IconGender.Female)!.Icon.Name);
        var ram = resolver.Resolve("Items.AdvancedRamUpgradeCommonPlus", IconGender.Female)!;
        Assert.Equal("cw_frontalcortex_ram", ram.Icon.Name);
        Assert.Equal(IconNameSource.IconPath, ram.Source);
    }

    [Fact]
    public void Generate_Melee_GripRootWithFreshSeed_NoChildren_91Bytes()
    {
        var source = GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true);
        var path = WriteSave("melee", (1, source));
        var original = SaveContainer.Read(path);
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.Preset_Katana_Default", 3));

        Assert.Equal(3 * (16 + 91), applied.AddedBytes);
        var sourceTree = ItemPartTree.Parse(source);
        var seeds = applied.Records.SelectMany(record => record.Seeds).ToArray();
        Assert.Equal(6, seeds.Length);
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.All(seeds, seed =>
        {
            Assert.True(seed > 2);
            Assert.Equal(-1, original.Payload.IndexOf(U32(seed)));
        });
        foreach (var record in applied.Records)
        {
            Assert.Equal(91, record.Body.Length);
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(TweakDbId.FromName("Items.Preset_Katana_Default"), BinaryPrimitives.ReadUInt64LittleEndian(record.Header));
            Assert.Equal(TweakDbId.FromName("Items.w_katana_grip"), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.Grip"), tree.Root.Slot);
            Assert.Empty(tree.Root.Children);
            Assert.Equal(record.Seeds[1], tree.Root.Seed);
            Assert.Equal("None", tree.Root.Appearance);
            Assert.Equal(sourceTree.Root.Trailer, tree.Root.Trailer);
            // 前缀：+20..+25 照抄来源，+25..+37 清零（E3d 形态），+37 起照抄。
            Assert.Equal(source.AsSpan(20, 5).ToArray(), record.Body.AsSpan(20, 5).ToArray());
            Assert.All(record.Body.AsSpan(25, 12).ToArray(), value => Assert.Equal(0, value));
            Assert.Equal(source.AsSpan(37, 4).ToArray(), record.Body.AsSpan(37, 4).ToArray());
        }

        Assert.Equal(applied.PreviousPlayerCount + 3, document.PlayerItemCount);
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_IconicMelee_AddsTheIconicModAsChild()
    {
        var path = WriteSave("iconic", (ItemInserter.StashBagId, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.Preset_Katana_Saburo", 1));
        var record = applied.Records[0];
        Assert.Equal(141, record.Body.Length);
        var tree = ItemPartTree.Parse(record.Body);
        Assert.Equal(TweakDbId.FromName("Items.w_katana_grip"), tree.Root.Tweak);
        var mod = Assert.Single(tree.Root.Children);
        Assert.Equal(TweakDbId.FromName("Items.SaburoKatanaWeaponMod"), mod.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.IconicMeleeWeaponMod1"), mod.Slot);
        Assert.Equal(new[] { record.Seed, tree.Root.Seed, mod.Seed }, record.Seeds);
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_Cyberware_GenericRootSeedTwo_ShardFreshSeed_141Bytes()
    {
        var path = WriteSave("cyberware", (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)));
        var original = SaveContainer.Read(path);
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.AdvancedRamUpgradeCommonPlus", 2));

        Assert.Equal(2 * (16 + 141), applied.AddedBytes);
        var model = ItemPartTree.Parse(GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)).Root.Children[0];
        foreach (var record in applied.Records)
        {
            Assert.Equal(141, record.Body.Length);
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(TweakDbId.FromName("Items.AdvancedRamUpgradeCommonPlus"), BinaryPrimitives.ReadUInt64LittleEndian(record.Header));
            Assert.Equal(TweakDbId.FromName("Items.GenericItemRoot"), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.GenericItemRoot"), tree.Root.Slot);
            Assert.Equal(ItemGenerator.GenericRootSeed, tree.Root.Seed);
            Assert.All(tree.Root.ItemId.AsSpan(12).ToArray(), value => Assert.Equal(0, value));
            var shard = Assert.Single(tree.Root.Children);
            Assert.Equal(TweakDbId.FromName("Items.AdvancedRamUpgradeStatsShard"), shard.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.StatsShardSlot"), shard.Slot);
            Assert.Empty(shard.Children);
            Assert.Equal(model.Trailer, shard.Trailer);
            Assert.Equal(model.ItemId.AsSpan(12).ToArray(), shard.ItemId.AsSpan(12).ToArray());
            // 新种子只有物品根和属性碎片；GenericItemRoot 的 2 不算。
            Assert.Equal(new[] { record.Seed, shard.Seed }, record.Seeds);
            Assert.All(record.Seeds, seed =>
            {
                Assert.True(seed > 2);
                Assert.Equal(-1, original.Payload.IndexOf(U32(seed)));
            });
            Assert.All(record.Body.AsSpan(25, 12).ToArray(), value => Assert.Equal(0, value));
        }

        Assert.NotEqual(applied.Records[0].Seeds[1], applied.Records[1].Seeds[1]);
        document.VerifyOnlyGenerated(Open(path), applied);

        var rootOnly = (Cp2077SaveDocument)_adapter.Load(path);
        var inductor = rootOnly.Generate(new GenerateRequest("Items.AdvancedReversePowerInductor", 1));
        Assert.Equal(91, inductor.Records[0].Body.Length);
        var inductorTree = ItemPartTree.Parse(inductor.Records[0].Body);
        Assert.Equal(ItemGenerator.GenericRootSeed, inductorTree.Root.Seed);
        Assert.Empty(inductorTree.Root.Children);
        Assert.Equal(new[] { inductor.Records[0].Seed }, inductor.Records[0].Seeds);
        rootOnly.VerifyOnlyGenerated(Open(path), inductor);
    }

    [Fact]
    public void PrefixSource_PrefersSameKind_ThenKnownGear_AndNeedsSomeParseableGear()
    {
        var stashMelee = WriteSave("same-kind",
            (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)),
            (ItemInserter.StashBagId, MeleeItem("Items.Preset_Katana_Default")));
        Assert.StartsWith("1000000:1:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(stashMelee), GearKinds.Melee, _names));
        Assert.StartsWith("1:2:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(stashMelee), GearKinds.Cyberware, _names));

        var unknownFirst = WriteSave("known",
            (1, GearGenerateTests.Clothing("Items.Unknown_Thing", generic: false)),
            (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        Assert.StartsWith("1:3:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(unknownFirst), GearKinds.Cyberware, _names));

        var onlyUnknown = WriteSave("unknown", (1, GearGenerateTests.Clothing("Items.Unknown_Thing", generic: false)));
        Assert.StartsWith("1:2:", ItemGenerator.ChooseTemplateKey(SaveContainer.Read(onlyUnknown), GearKinds.Melee, _names));

        var bare = WriteSave("bare");
        Assert.Null(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(bare), GearKinds.Melee, _names));
        var document = (Cp2077SaveDocument)_adapter.Load(bare);
        Assert.Contains("能解析的装备", Assert.Throws<InvalidDataException>(() =>
            document.Generate(new GenerateRequest("Items.AdvancedRamUpgradeCommon", 1))).Message);
        Assert.Contains("不在可生成目录", Assert.Throws<InvalidDataException>(() =>
            document.Generate(new GenerateRequest("Items.MantisBlades", 1))).Message);
    }

    [Fact]
    public void VerifyOnlyGenerated_RejectsTamperedCyberwareAndMelee()
    {
        var path = WriteSave("strict", (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)));

        // GenericItemRoot 的种子 2 被改：itemData 与生成结果不一致。
        var seed = (Cp2077SaveDocument)_adapter.Load(path);
        var seedResult = seed.Generate(new GenerateRequest("Items.AdvancedRamUpgradeCommon", 1));
        var at = Container(seed).Payload.IndexOf(seedResult.Records[0].Body);
        Container(seed).WritePayload(at + 41 + 8, [0x03]);
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => seed.VerifyOnlyGenerated(Open(path), seedResult)).Message);

        // 属性碎片的挂点被改。
        var slot = (Cp2077SaveDocument)_adapter.Load(path);
        var slotResult = slot.Generate(new GenerateRequest("Items.AdvancedRamUpgradeCommon", 1));
        at = Container(slot).Payload.IndexOf(slotResult.Records[0].Body);
        Container(slot).WritePayload(at + 41 + 30 + 16 + 5, [(byte)(slotResult.Records[0].Body[41 + 30 + 16 + 5] ^ 0x7F)]);
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => slot.VerifyOnlyGenerated(Open(path), slotResult)).Message);

        // 结果里冒称用了种子 2 或存档里已有的种子。
        var reused = (Cp2077SaveDocument)_adapter.Load(path);
        var reusedResult = reused.Generate(new GenerateRequest("Items.Preset_Katana_Default", 1));
        var two = reusedResult with { Records = [reusedResult.Records[0] with { Seeds = [.. reusedResult.Records[0].Seeds, ItemGenerator.GenericRootSeed] }] };
        Assert.Contains("种子", Assert.Throws<InvalidDataException>(() => reused.VerifyOnlyGenerated(Open(path), two)).Message);
        var existing = reusedResult with { Records = [reusedResult.Records[0] with { Seeds = [.. reusedResult.Records[0].Seeds, 0x6666] }] };
        Assert.Contains("种子", Assert.Throws<InvalidDataException>(() => reused.VerifyOnlyGenerated(Open(path), existing)).Message);

        // 件数对不上。
        var count = (Cp2077SaveDocument)_adapter.Load(path);
        var countResult = count.Generate(new GenerateRequest("Items.Preset_Katana_Default", 1));
        Assert.Contains("件数", Assert.Throws<InvalidDataException>(() => count.VerifyOnlyGenerated(Open(path), countResult with { Copies = 2 })).Message);
    }

    [Fact]
    public void PatchEngineGenerate_WritesMeleeAndCyberware_KeepsResources()
    {
        var path = WriteSave("engine", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var money = before.GetQuantity(new ResourceId("money"));
        foreach (var target in new[] { "Items.Preset_Katana_Default", "Items.AdvancedRamUpgradeCommonPlus" })
        {
            var report = PatchEngine.Generate(_adapter, path, new GenerateRequest(target, 1), new PatchOptions(null));
            Assert.Equal(PatchOutcome.Patched, report.Patch.Outcome);
        }

        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(before.PlayerItemCount + 2, after.PlayerItemCount);
        Assert.Equal(money, after.GetQuantity(new ResourceId("money")));
        Assert.True(after.FindGeneratable("Items.Preset_Katana_Default")!.Owned);
        Assert.True(after.FindGeneratable("Items.AdvancedRamUpgradeCommonPlus")!.Owned);
    }

    private static byte[] MeleeItem(string record) =>
        ItemPartTreeTests.Body(record, 0x4444,
            ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_katana_grip"), 0x45, 0x0009, "None", TweakDbId.FromName("AttachmentSlots.Grip")));

    private string WriteSave(string name, params (ulong Bag, byte[] Body)[] items) => GearGenerateTests.WriteSaveAt(_root, name, items);

    private Cp2077SaveDocument Open(string path) =>
        Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);

    private static SaveContainer Container(Cp2077SaveDocument document) =>
        (SaveContainer)typeof(Cp2077SaveDocument).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
