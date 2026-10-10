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

/// <summary>
/// T022 第三批类别：手雷（结构 3、45 字节前缀）、义体操作系统、手臂义体、快速破解、模组的
/// 配件树解析、目录分类、生成形态、种子规则和严格校验。
/// </summary>
public class ThirdBatchTests : IDisposable
{
    private const string Tweaks = """
        package RTDB
        Item
        {
        }

        WeaponItem : Item
        {
        }

        Gadget : WeaponItem
        {
        }

        Grenade : Gadget
        {
        	fk< ItemType > itemType = "ItemType.Gad_Grenade";
        }

        SlotItemPartPreset
        {
        }

        package Items
        using RTDB

        [ notQueryable ]
        Part : Item
        {
        	tags = [ "itemPart" ];
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

        [ notQueryable ]
        Preset_Grenade_Frag_Default : Grenade
        {
        	displayName = "LocKey#30";
        	iconPath = "grenade_frag_normal";
        	fk< SlotItemPartPreset >[] slotPartListPreset =
        	[
        		{
        			slot = "AttachmentSlots.GrenadeCore";
        			fk< Item > itemPartPreset = "Items.w_frag_core";
        		}
        	];
        }

        GrenadeFragEpicPlus : Preset_Grenade_Frag_Default
        {
        	quality = "Quality.EpicPlus";
        }

        [ notQueryable ]
        GrenadeFragEpic : Preset_Grenade_Frag_Default
        {
        	quality = "Quality.Epic";
        }

        [ notQueryable ]
        GrenadeSmokeLegendaryPlus : Preset_Grenade_Frag_Default
        {
        	displayName = "LocKey#31";
        	quality = "Quality.LegendaryPlus";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.GrenadeCore"; itemPartPreset = "Items.w_smoke_core"; }
        	];
        }

        [ notQueryable ]
        NPCGrenadeFragRegular : Preset_Grenade_Frag_Default
        {
        }

        [ notQueryable ]
        CuttingGrenadeLegendaryPlus : Grenade
        {
        	displayName = "LocKey#32";
        	iconPath = "grenade_frag_normal";
        	quality = "Quality.LegendaryPlus";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.GrenadeCore"; itemPartPreset = "Items.w_cutting_core"; },
        		{ slot = "AttachmentSlots.GrenadeDelivery"; itemPartPreset = "Items.w_homing_delivery"; }
        	];
        }

        [ notQueryable ]
        Cyberware : Item
        {
        	itemType = "ItemType.Cyberware";
        	tags = [ "Cyberware", "HideInBackpackUI" ];
        }

        [ notQueryable ]
        SystemReplacementModule : Cyberware
        {
        	fk< EquipmentArea > equipArea = "EquipmentArea.SystemReplacementCW";
        }

        [ notQueryable ]
        SandevistanBase : SystemReplacementModule
        {
        	displayName = "LocKey#40";
        	iconPath = "cw_sandevistan";
        	fk< SlotItemPartListElement >[] slotPartList =
        	[
        		"Items.GenericRootSlotItem"
        	];
        }

        AdvancedSandevistanC1MK1 : SandevistanBase
        {
        	quality = "Quality.Common";
        	nextUpgradeItem = "Items.AdvancedSandevistanC1MK1Plus";
        }

        [ notQueryable ]
        AdvancedSandevistanC1MK1Plus : AdvancedSandevistanC1MK1
        {
        	nextUpgradeItem = "";
        }

        [ notQueryable ]
        FuyutsuiCommonMKI : SystemReplacementModule
        {
        	displayName = "LocKey#41";
        	iconPath = "cw_sandevistan";
        }

        [ notQueryable ]
        MeleeWeapon : WeaponItem
        {
        }

        [ notQueryable ]
        MantisBlades : MeleeWeapon
        {
        	displayName = "LocKey#50";
        	iconPath = "cw_mantis";
        	itemType = "ItemType.Cyb_MantisBlades";
        	fk< EquipmentArea > equipArea = "EquipmentArea.ArmsCW";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.CyberwareRoot"; itemPartPreset = "Items.MantisBladesRoot"; },
        		{ slot = "AttachmentSlots.MantisBladesEdge"; itemPartPreset = "Items.PhysicalDamageEdge"; },
        		{ slot = "AttachmentSlots.MantisBladesRotor"; itemPartPreset = ""; }
        	];
        }

        [ notQueryable ]
        AdvancedMantisBladesBase : MantisBlades
        {
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.CyberwareRoot"; itemPartPreset = "Items.MantisBladesRoot"; }
        	];
        }

        [ notQueryable ]
        AdvancedMantisBladesUncommon : AdvancedMantisBladesBase
        {
        	quality = "Quality.Uncommon";
        	slotPartListPreset +=
        	[
        		{
        			slot = "AttachmentSlots.MantisBladesEdge";
        			itemPartPreset = "Items.PhysicalDamageEdge";
        		},
        		{
        			itemPartPreset = "Items.AdvancedMantisBladesStatsShard";
        		} : StatsShardSlotPart
        	];
        	nextUpgradeItem = "Items.AdvancedMantisBladesUncommonPlus";
        }

        [ EP1 ]
        [ notQueryable ]
        AdvancedMantisBladesUncommonPlus : AdvancedMantisBladesUncommon
        {
        	nextUpgradeItem = "";
        }

        Q306_orbital_cyberware : MantisBlades
        {
        }

        [ notQueryable ]
        CyberdeckProgram : Part
        {
        	iconPath = "program_generic";
        	itemType = "ItemType.Prt_Program";
        	placementSlots =
        	[
        		"AttachmentSlots.CyberdeckProgram1", "AttachmentSlots.CyberdeckProgram2"
        	];
        }

        ContagionProgram : CyberdeckProgram
        {
        	displayName = "LocKey#60";
        	quality = "Quality.Uncommon";
        }

        WhistleProgram : CyberdeckProgram
        {
        	displayName = "LocKey#61";
        	tags += [ "DeprecatedQuickhack" ];
        }

        [ notQueryable ]
        Mod : Part
        {
        	fk< ItemType > itemType = "ItemType.Prt_Mod";
        	iconPath = "mod_generic";
        	tags += [ "WeaponMod" ];
        	placementSlots =
        	[
        		"AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1", "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod2"
        	];
        }

        [ notQueryable ]
        PowerWeaponMod : Mod
        {
        	fk< ItemType > itemType = "ItemType.Prt_PowerMod";
        }

        PowerMod2_Legendary : PowerWeaponMod
        {
        	displayName = "LocKey#70";
        	quality = "Quality.Legendary";
        }

        [ notQueryable ]
        PowerMod2_Base : PowerWeaponMod
        {
        	displayName = "LocKey#70";
        }

        [ EP1 ]
        [ notQueryable ]
        ChimeraPowerMod : PowerWeaponMod
        {
        	displayName = "LocKey#71";
        	tags += [ "Retrievable" ];
        }

        w_att_scope_short_05 : Mod
        {
        	displayName = "LocKey#72";
        	fk< ItemType > itemType = "ItemType.Prt_ShortScope";
        	fk< AttachmentSlot >[] placementSlots = [ "AttachmentSlots.Scope" ];
        }

        Headsman_scope : Mod
        {
        	displayName = "LocKey#73";
        	fk< ItemType > itemType = "ItemType.Prt_ShortScope";
        	tags += [ "HideInUI" ];
        	placementSlots = [ "AttachmentSlots.Scope" ];
        }

        [ notQueryable ]
        FabricEnhancer : Part
        {
        	fk< ItemType > itemType = "ItemType.Prt_FabricEnhancer";
        	placementSlots = [ "AttachmentSlots.FabricEnhancer1" ];
        }

        SimpleFabricEnhancer01 : FabricEnhancer
        {
        	displayName = "LocKey#74";
        	iconPath = "mod_generic";
        }

        package UIIcon
        grenade_frag_normal
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons4.inkatlas";
        	atlasPartName = "grenade_frag_normal";
        }

        cw_sandevistan
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/cyberware.inkatlas";
        	atlasPartName = "cw_sandevistan";
        }

        cw_mantis
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/cyberware.inkatlas";
        	atlasPartName = "cw_mantis";
        }

        program_generic
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons4.inkatlas";
        	atlasPartName = "program_generic";
        }

        mod_generic
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons4.inkatlas";
        	atlasPartName = "mod_generic";
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(30, "", "", "F-GX 破片手雷"),
        new(31, "", "", "烟雾弹"),
        new(32, "", "", "深切割人员杀伤手雷"),
        new(40, "", "", "斯安威斯坦"),
        new(41, "", "", "旧接入仓"),
        new(50, "", "", "螳螂刀"),
        new(60, "", "", "连带传染"),
        new(61, "", "", "诱敌信号"),
        new(70, "", "", "威力模组"),
        new(71, "", "", "奇美拉模组"),
        new(72, "", "", "2067 式"),
        new(73, "", "", "刽子手瞄具"),
        new(74, "", "", "织物强化")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-t022-" + Guid.NewGuid().ToString("N"));
    private readonly TweakSourceIndex _index;
    private readonly Cp2077ItemNames _names;
    private readonly Cp2077Adapter _adapter;

    public ThirdBatchTests()
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

    [Fact]
    public void PartTree_Structure3_RoundTrips95And151_WithQuantityPrefix()
    {
        var frag = Grenade95("Items.GrenadeFragLegendaryPlus");
        var cutting = Grenade151("Items.CuttingGrenadeLegendaryPlus");
        Assert.Equal(95, frag.Length);
        Assert.Equal(151, cutting.Length);
        foreach (var bytes in new[] { frag, cutting })
        {
            Assert.Equal(ItemPartTree.QuantityPrefixLength, ItemPartTree.PrefixLengthOf(bytes));
            var parsed = ItemPartTree.Parse(bytes);
            Assert.Equal(bytes, ItemPartTree.Write(parsed));
            Assert.Equal(45, parsed.Prefix.Length);
            Assert.Equal(1u, parsed.Quantity);
            Assert.Equal(bytes.AsSpan(4, 16).ToArray(), parsed.RootId);
            Assert.True(ItemPartTree.IsQuantityStructure(parsed.RootId));
        }

        var fragTree = ItemPartTree.Parse(frag);
        Assert.Equal(TweakDbId.FromName("Items.w_frag_core"), fragTree.Root.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.GrenadeCore"), fragTree.Root.Slot);
        Assert.Equal("None", fragTree.Root.Appearance);
        Assert.Empty(fragTree.Root.Children);

        var cuttingTree = ItemPartTree.Parse(cutting);
        Assert.Equal("default", cuttingTree.Root.Appearance);
        var delivery = Assert.Single(cuttingTree.Root.Children);
        Assert.Equal(TweakDbId.FromName("Items.w_homing_delivery"), delivery.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.GrenadeDelivery"), delivery.Slot);
        Assert.Equal("default", delivery.Appearance);
        Assert.NotEqual(0, BinaryPrimitives.ReadSingleLittleEndian(delivery.Trailer.AsSpan(4)));

        // 普通装备（结构字节 0、1）仍按 41 字节前缀；数量字段只在 45 字节前缀上有。
        var clothing = GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true);
        Assert.Equal(ItemPartTree.PrefixLength, ItemPartTree.PrefixLengthOf(clothing));
        Assert.Null(ItemPartTree.Parse(clothing).Quantity);
        var structureOne = clothing.ToArray();
        structureOne[16] = 1;
        Assert.Equal(structureOne, ItemPartTree.Write(ItemPartTree.Parse(structureOne)));
    }

    [Fact]
    public void PartTree_Structure3_RejectsWrongPrefixShapes()
    {
        // 结构 3 却按 41 字节前缀写的配件树：在 45 字节处解析会错位。
        var clothing = GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true);
        var flagged = clothing.ToArray();
        flagged[16] = ItemPartTree.QuantityStructure;
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(flagged));

        // 45 字节的手雷把结构字节改成 0：在 41 字节处解析也会错位。
        var unflagged = Grenade95("Items.GrenadeFragLegendaryPlus");
        unflagged[16] = 0;
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(unflagged));

        // 写回时前缀长度必须与结构字节相符。
        var parsed = ItemPartTree.Parse(Grenade95("Items.GrenadeFragLegendaryPlus"));
        Assert.Contains("45 字节", Assert.Throws<InvalidDataException>(() => ItemPartTree.Write(parsed with { Prefix = parsed.Prefix[..41] })).Message);
        var plain = ItemPartTree.Parse(clothing);
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Write(plain with { Prefix = [.. plain.Prefix, 0, 0, 0, 0] }));
        Assert.Throws<InvalidDataException>(() => ItemPartTree.Parse(Grenade95("Items.X").AsSpan(0, 44)));
    }

    [Fact]
    public void Catalog_ClassifiesTheThirdBatch()
    {
        var gear = _names.Gear;
        string[] Of(string kind) => gear.Where(entry => entry.Kind == kind).Select(entry => entry.RecordName).Order(StringComparer.Ordinal).ToArray();

        // 索引：placementSlots 沿继承链取（多行列表、fk 前缀写法都认），nextUpgradeItem 可以只看自己写的。
        Assert.Equal(
            new[] { "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1", "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod2" },
            _index.ListOf("Items.PowerMod2_Legendary", "placementSlots"));
        Assert.Equal(new[] { "AttachmentSlots.Scope" }, _index.ListOf("Items.w_att_scope_short_05", "placementSlots"));
        Assert.Equal("Items.AdvancedMantisBladesUncommonPlus", _index.OwnFieldOf("Items.AdvancedMantisBladesUncommon", "nextUpgradeItem"));
        Assert.Null(_index.OwnFieldOf("Items.AdvancedMantisBladesBase", "nextUpgradeItem"));
        Assert.Equal("", _index.OwnFieldOf("Items.AdvancedMantisBladesUncommonPlus", "nextUpgradeItem"));

        // 手雷：可查询的 + 带 notQueryable 但品质是 …Plus 的档位；不带 Plus 的旧档位、NPC 版、预设基类不收。
        Assert.Equal(new[] { "Items.CuttingGrenadeLegendaryPlus", "Items.GrenadeFragEpicPlus", "Items.GrenadeSmokeLegendaryPlus" }, Of(GearKinds.Grenade));
        // 操作系统并入义体：可查询的 + 升级档；旧接入仓（notQueryable、不在升级链上）不收。
        Assert.Equal(new[] { "Items.AdvancedSandevistanC1MK1", "Items.AdvancedSandevistanC1MK1Plus" }, Of(GearKinds.Cyberware));
        Assert.Equal("操作系统", gear.Single(entry => entry.RecordName == "Items.AdvancedSandevistanC1MK1").TypeLabel);
        // 手臂义体：升级链的起点和升级档；基类、旧版和不在链上的任务副本不收。
        Assert.Equal(new[] { "Items.AdvancedMantisBladesUncommon", "Items.AdvancedMantisBladesUncommonPlus" }, Of(GearKinds.ArmCyberware));
        Assert.Equal("螳螂刀", gear.Single(entry => entry.RecordName == "Items.AdvancedMantisBladesUncommon").TypeLabel);
        // 快速破解：弃用的不收；挂点取 placementSlots 第一项。
        var contagion = Assert.Single(gear, entry => entry.Kind == GearKinds.Quickhack);
        Assert.Equal("Items.ContagionProgram", contagion.RecordName);
        Assert.Equal("AttachmentSlots.CyberdeckProgram1", contagion.RootSlot);
        // 模组：可查询的 + 可回收的奇美拉模组；隐藏的瞄具、notQueryable 的基类不收，服装模组不分类。
        Assert.Equal(new[] { "Items.ChimeraPowerMod", "Items.PowerMod2_Legendary", "Items.w_att_scope_short_05" }, Of(GearKinds.Mod));
        var scope = gear.Single(entry => entry.RecordName == "Items.w_att_scope_short_05");
        Assert.Equal("短程瞄具", scope.TypeLabel);
        Assert.Equal("AttachmentSlots.Scope", scope.RootSlot);
        Assert.Equal("威力武器模组", gear.Single(entry => entry.RecordName == "Items.PowerMod2_Legendary").TypeLabel);
        Assert.Equal("AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1", gear.Single(entry => entry.RecordName == "Items.PowerMod2_Legendary").RootSlot);

        // 分类本身不看属性：被排除的条目仍认得出类别，模板选择据此排序。
        Assert.Equal(GearKinds.Grenade, _names.KindOf(TweakDbId.FromName("Items.NPCGrenadeFragRegular")));
        Assert.Equal(GearKinds.ArmCyberware, _names.KindOf(TweakDbId.FromName("Items.Q306_orbital_cyberware")));
        Assert.Equal(GearKinds.Cyberware, _names.KindOf(TweakDbId.FromName("Items.FuyutsuiCommonMKI")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.SimpleFabricEnhancer01")));
        Assert.Null(_names.KindOf(TweakDbId.FromName("Items.MantisBladesRoot")));

        var path = WriteSave("kinds", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(new[] { "grenade", "cyberware", "arm_cyberware", "quickhack", "mod" }, document.GenerateKinds.Select(kind => kind.Id));
        Assert.Equal(new[] { "手雷", "义体", "手臂义体", "快速破解", "模组" }, document.GenerateKinds.Select(kind => kind.Label));
        Assert.Equal(2, document.ListGeneratable(null, "操作系统", 0, 50).Total);
        Assert.Equal(3, document.ListGeneratable("mod", "模组", 0, 50).Total);
        Assert.Equal("Items.ChimeraPowerMod", Assert.Single(document.ListGeneratable("mod", "奇美拉", 0, 50).Items).Target);
    }

    [Fact]
    public void Icons_ResolveForEveryThirdBatchEntry()
    {
        var resolver = _names.IconResolver!;
        foreach (var entry in _names.Gear)
            Assert.NotNull(resolver.Resolve(entry.RecordName, IconGender.Female));
        Assert.Equal("grenade_frag_normal", resolver.Resolve("Items.CuttingGrenadeLegendaryPlus", IconGender.Female)!.Icon.Name);
        Assert.Equal("program_generic", resolver.Resolve("Items.ContagionProgram", IconGender.Female)!.Icon.Name);
    }

    [Fact]
    public void Generate_Grenade_SeedTwoStructureThree_QuantityOne_95And145()
    {
        var source = GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true);
        var path = WriteSave("grenade", (1, source));
        var original = SaveContainer.Read(path);
        var fragDocument = (Cp2077SaveDocument)_adapter.Load(path);
        var frag = fragDocument.Generate(new GenerateRequest("Items.GrenadeFragEpicPlus", 1));
        var cuttingDocument = (Cp2077SaveDocument)_adapter.Load(path);
        var cutting = cuttingDocument.Generate(new GenerateRequest("Items.CuttingGrenadeLegendaryPlus", 1));

        Assert.Equal(16 + 95, frag.AddedBytes);
        // 存档里的切割手雷是 151 字节，多出的 6 字节是两个配件的 CName「default」；生成时 CName 写 None，所以是 45 + 50 × 2 = 145。
        Assert.Equal(16 + 145, cutting.AddedBytes);
        foreach (var (applied, target, core, length) in new[]
                 {
                     (frag, "Items.GrenadeFragEpicPlus", "Items.w_frag_core", 95),
                     (cutting, "Items.CuttingGrenadeLegendaryPlus", "Items.w_cutting_core", 145)
                 })
        {
            var record = applied.Records[0];
            Assert.Equal(length, record.Body.Length);
            Assert.Equal(ItemGenerator.QuantityHeader(TweakDbId.FromName(target)), record.Header);
            Assert.Equal(new byte[] { 2, 0, 0, 0, 3, 0, 0, 0 }, record.Header.AsSpan(8).ToArray());
            Assert.Equal(ItemGenerator.GenericRootSeed, record.Seed);
            Assert.DoesNotContain(ItemGenerator.GenericRootSeed, record.Seeds);
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(45, tree.Prefix.Length);
            Assert.Equal(1u, tree.Quantity);
            // 前缀：+20..+25 照抄来源，+25 插入数量，来源的 +25..+37 后移到 +29..+41 并清零，FLT_MAX 在 +41。
            Assert.Equal(source.AsSpan(20, 5).ToArray(), record.Body.AsSpan(20, 5).ToArray());
            Assert.All(record.Body.AsSpan(29, 12).ToArray(), value => Assert.Equal(0, value));
            Assert.Equal(source.AsSpan(37, 4).ToArray(), record.Body.AsSpan(41, 4).ToArray());
            Assert.Equal(TweakDbId.FromName(core), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.GrenadeCore"), tree.Root.Slot);
            Assert.Equal("None", tree.Root.Appearance);
            Assert.Equal(ItemGenerator.BlankTrailer(), tree.Root.Trailer);
            Assert.Equal(tree.Walk().Select(entry => entry.Part.Seed).ToArray(), record.Seeds);
            Assert.All(record.Seeds, seed =>
            {
                Assert.True(seed > 2);
                Assert.Equal(-1, original.Payload.IndexOf(U32(seed)));
            });
        }

        var delivery = Assert.Single(ItemPartTree.Parse(cutting.Records[0].Body).Root.Children);
        Assert.Equal(TweakDbId.FromName("Items.w_homing_delivery"), delivery.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.GrenadeDelivery"), delivery.Slot);
        Assert.Empty(ItemPartTree.Parse(frag.Records[0].Body).Root.Children);
        fragDocument.VerifyOnlyGenerated(Open(path), frag);
        cuttingDocument.VerifyOnlyGenerated(Open(path), cutting);
    }

    [Fact]
    public void Generate_Grenade_RefusesCopiesAndOneAlreadyInTheBag()
    {
        var held = WriteSave("held",
            (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)),
            (1, Grenade95("Items.GrenadeFragEpicPlus")));
        var document = (Cp2077SaveDocument)_adapter.Load(held);
        Assert.Contains("背包里已经有这种手雷", Assert.Throws<InvalidDataException>(() =>
            document.Generate(new GenerateRequest("Items.GrenadeFragEpicPlus", 1))).Message);
        Assert.Contains("只能生成 1 件", Assert.Throws<InvalidDataException>(() =>
            document.Generate(new GenerateRequest("Items.GrenadeSmokeLegendaryPlus", 2))).Message);
        // 已有手雷不当前缀来源（45 字节前缀），换一种手雷照常生成，前缀借武器。
        var smoke = document.Generate(new GenerateRequest("Items.GrenadeSmokeLegendaryPlus", 1));
        Assert.StartsWith("1:2:", smoke.TemplateKey);
        Assert.Equal(95, smoke.Records[0].Body.Length);
        document.VerifyOnlyGenerated(Open(held), smoke);

        // 只有手雷时没有可借的前缀。
        var onlyGrenade = WriteSave("only-grenade", (1, Grenade95("Items.GrenadeFragEpicPlus")));
        Assert.Null(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(onlyGrenade), GearKinds.Grenade, _names));
        // 结构字节为 1 的物品（存档里的 Preset_Baton_Murphy）能解析，但也不借：结构字节会随物品头抄给新物品。
        var structureOne = GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true);
        structureOne[16] = 1;
        var onlyStructureOne = WriteSave("only-structure-one", (1, structureOne));
        Assert.Null(ItemGenerator.ChooseTemplateKey(SaveContainer.Read(onlyStructureOne), GearKinds.Mod, _names));
    }

    [Fact]
    public void Generate_OperatingSystem_GenericRootSeedTwo_91Bytes()
    {
        var path = WriteSave("os", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.AdvancedSandevistanC1MK1Plus", 1));
        var record = applied.Records[0];
        Assert.Equal(91, record.Body.Length);
        var tree = ItemPartTree.Parse(record.Body);
        Assert.Equal(TweakDbId.FromName("Items.GenericItemRoot"), tree.Root.Tweak);
        Assert.Equal(TweakDbId.FromName("AttachmentSlots.GenericItemRoot"), tree.Root.Slot);
        Assert.Equal(ItemGenerator.GenericRootSeed, tree.Root.Seed);
        Assert.Empty(tree.Root.Children);
        Assert.Equal(new[] { record.Seed }, record.Seeds);
        Assert.Null(tree.Quantity);
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_ArmCyberware_RootPlusChildrenInPresetOrder_FreshSeeds()
    {
        // 来源的子配件带外观名（像存档里的 PhysicalKnuckles&Male）就不能当前缀来源，改用后面的服装。
        var arms = ItemPartTreeTests.Body("Items.AdvancedStrongArmsLegendaryPlus", 0x3131,
            ItemPartTreeTests.Part(TweakDbId.FromName("Items.StrongArmsRoot"), 0x32, 0x10, "None", TweakDbId.FromName("AttachmentSlots.CyberwareRoot"),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.PhysicalDamageKnuckles"), 0x33, 0x11, "PhysicalKnuckles&Male", TweakDbId.FromName("AttachmentSlots.StrongArmsKnuckles"))));
        var path = WriteSave("arms", (1, arms), (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var original = SaveContainer.Read(path);
        Assert.StartsWith("1:3:", ItemGenerator.ChooseTemplateKey(original, GearKinds.ArmCyberware, _names));

        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var applied = document.Generate(new GenerateRequest("Items.AdvancedMantisBladesUncommonPlus", 2));
        Assert.Equal(2 * (16 + 191), applied.AddedBytes);
        foreach (var record in applied.Records)
        {
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(TweakDbId.FromName("Items.MantisBladesRoot"), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.CyberwareRoot"), tree.Root.Slot);
            Assert.Equal(
                new[] { TweakDbId.FromName("Items.PhysicalDamageEdge"), TweakDbId.FromName("Items.AdvancedMantisBladesStatsShard") },
                tree.Root.Children.Select(child => child.Tweak));
            Assert.Equal(
                new[] { TweakDbId.FromName("AttachmentSlots.MantisBladesEdge"), TweakDbId.FromName("AttachmentSlots.StatsShardSlot") },
                tree.Root.Children.Select(child => child.Slot));
            Assert.All(tree.Walk(), entry => Assert.Equal("None", entry.Part.Appearance));
            Assert.All(tree.Root.Children, child => Assert.Empty(child.Children));
            Assert.Equal(new[] { record.Seed }.Concat(tree.Walk().Select(entry => entry.Part.Seed)), record.Seeds);
            Assert.All(record.Seeds, seed =>
            {
                Assert.True(seed > 2);
                Assert.Equal(-1, original.Payload.IndexOf(U32(seed)));
            });
        }

        Assert.Equal(8, applied.Records.SelectMany(record => record.Seeds).Distinct().Count());
        document.VerifyOnlyGenerated(Open(path), applied);
    }

    [Fact]
    public void Generate_QuickhackAndMod_ItemIsItsOwnRootPart_91Bytes()
    {
        var path = WriteSave("parts", (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)));
        var original = SaveContainer.Read(path);
        foreach (var (target, slot) in new[]
                 {
                     ("Items.ContagionProgram", "AttachmentSlots.CyberdeckProgram1"),
                     ("Items.PowerMod2_Legendary", "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1"),
                     ("Items.w_att_scope_short_05", "AttachmentSlots.Scope")
                 })
        {
            var document = (Cp2077SaveDocument)_adapter.Load(path);
            var applied = document.Generate(new GenerateRequest(target, 1));
            var record = applied.Records[0];
            Assert.Equal(91, record.Body.Length);
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(record.Header, tree.RootId);
            Assert.Equal(record.Header, tree.Root.ItemId);
            Assert.Equal(TweakDbId.FromName(target), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName(slot), tree.Root.Slot);
            Assert.Empty(tree.Root.Children);
            Assert.Equal("None", tree.Root.Appearance);
            Assert.Equal(ItemGenerator.BlankTrailer(), tree.Root.Trailer);
            Assert.Equal(new[] { record.Seed }, record.Seeds);
            Assert.True(record.Seed > 2);
            Assert.Equal(-1, original.Payload.IndexOf(U32(record.Seed)));
            Assert.All(record.Body.AsSpan(25, 12).ToArray(), value => Assert.Equal(0, value));
            document.VerifyOnlyGenerated(Open(path), applied);
        }
    }

    [Fact]
    public void VerifyOnlyGenerated_RejectsTamperedThirdBatchItems()
    {
        var path = WriteSave("strict", (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)));

        // 手雷的根种子 2 冒充新种子，或结果里的根种子不是 2。
        var seed = (Cp2077SaveDocument)_adapter.Load(path);
        var seedResult = seed.Generate(new GenerateRequest("Items.GrenadeFragEpicPlus", 1));
        var claimed = seedResult with { Records = [seedResult.Records[0] with { Seeds = [.. seedResult.Records[0].Seeds, ItemGenerator.GenericRootSeed] }] };
        Assert.Contains("不能算作新种子", Assert.Throws<InvalidDataException>(() => seed.VerifyOnlyGenerated(Open(path), claimed)).Message);
        var other = seedResult with { Records = [seedResult.Records[0] with { Seed = 0x7777 }] };
        Assert.Contains("根种子必须是 2", Assert.Throws<InvalidDataException>(() => seed.VerifyOnlyGenerated(Open(path), other)).Message);

        // 数量被改成 5（存档和结果同时改，逐字节比对能过，数量规则拦下）。
        var quantity = (Cp2077SaveDocument)_adapter.Load(path);
        var quantityResult = quantity.Generate(new GenerateRequest("Items.GrenadeFragEpicPlus", 1));
        var at = Container(quantity).Payload.IndexOf(quantityResult.Records[0].Body);
        Container(quantity).WritePayload(at + ItemPartTree.QuantityOffset, [5]);
        var body = quantityResult.Records[0].Body.ToArray();
        body[ItemPartTree.QuantityOffset] = 5;
        var five = quantityResult with { Records = [quantityResult.Records[0] with { Body = body }] };
        Assert.Contains("数量必须是 1", Assert.Throws<InvalidDataException>(() => quantity.VerifyOnlyGenerated(Open(path), five)).Message);

        // 存档里的手雷被改成结构 0：与生成结果不一致。
        var structure = (Cp2077SaveDocument)_adapter.Load(path);
        var structureResult = structure.Generate(new GenerateRequest("Items.CuttingGrenadeLegendaryPlus", 1));
        at = Container(structure).Payload.IndexOf(structureResult.Records[0].Body);
        Container(structure).WritePayload(at + 16, [0]);
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => structure.VerifyOnlyGenerated(Open(path), structureResult)).Message);

        // 模组的根配件挂点被改。
        var slot = (Cp2077SaveDocument)_adapter.Load(path);
        var slotResult = slot.Generate(new GenerateRequest("Items.PowerMod2_Legendary", 1));
        at = Container(slot).Payload.IndexOf(slotResult.Records[0].Body);
        Container(slot).WritePayload(at + 41 + 16 + 5, [(byte)(slotResult.Records[0].Body[41 + 16 + 5] ^ 0x7F)]);
        Assert.Contains("itemData 与生成结果不一致", Assert.Throws<InvalidDataException>(() => slot.VerifyOnlyGenerated(Open(path), slotResult)).Message);

        // 手臂义体结果里冒称用了存档里已有的种子。
        var reused = (Cp2077SaveDocument)_adapter.Load(path);
        var reusedResult = reused.Generate(new GenerateRequest("Items.AdvancedMantisBladesUncommon", 1));
        var existing = reusedResult with { Records = [reusedResult.Records[0] with { Seeds = [.. reusedResult.Records[0].Seeds, 0x6666] }] };
        Assert.Contains("种子", Assert.Throws<InvalidDataException>(() => reused.VerifyOnlyGenerated(Open(path), existing)).Message);

        // 原存档背包里已有同一 ID 的手雷：直接检查数量型物品的规则。
        var held = WriteSave("strict-held",
            (1, GearGenerateTests.Weapon("Items.Preset_MA70_Default", children: true)),
            (1, Grenade95("Items.GrenadeFragEpicPlus")));
        var verify = typeof(Cp2077SaveDocument).GetMethod("VerifyQuantityItem", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => verify.Invoke(null, [SaveContainer.Read(held), seedResult.Records[0]]));
        Assert.Contains("已经有同一 ID 的手雷", error.InnerException!.Message);
        verify.Invoke(null, [SaveContainer.Read(path), seedResult.Records[0]]);
    }

    [Fact]
    public void PatchEngineGenerate_WritesEveryNewKind_KeepsResources()
    {
        var path = WriteSave("engine", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        var money = before.GetQuantity(new ResourceId("money"));
        var targets = new[]
        {
            "Items.GrenadeSmokeLegendaryPlus", "Items.AdvancedSandevistanC1MK1", "Items.AdvancedMantisBladesUncommon",
            "Items.ContagionProgram", "Items.PowerMod2_Legendary"
        };
        foreach (var target in targets)
        {
            var report = PatchEngine.Generate(_adapter, path, new GenerateRequest(target, 1), new PatchOptions(null));
            Assert.Equal(PatchOutcome.Patched, report.Patch.Outcome);
        }

        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(before.PlayerItemCount + targets.Length, after.PlayerItemCount);
        Assert.Equal(money, after.GetQuantity(new ResourceId("money")));
        Assert.All(targets, target => Assert.True(after.FindGeneratable(target)!.Owned));
        // 写回后的手雷能按结构 3 原样解析，背包里有了它就不能再生成。
        var grenade = ItemGenerator.ScanParts(SaveContainer.Read(path)).Single(scan => scan.TweakDbIdHex == Hex(TweakDbId.FromName("Items.GrenadeSmokeLegendaryPlus")));
        Assert.True(grenade.RoundTrip);
        Assert.True(grenade.RootMatchesHeader);
        Assert.Equal(95, grenade.Length);
        Assert.Throws<InvalidDataException>(() =>
            PatchEngine.Generate(_adapter, path, new GenerateRequest("Items.GrenadeSmokeLegendaryPlus", 1), new PatchOptions(null)));
    }

    /// <summary>ManualSave-8 的 GrenadeFragLegendaryPlus 形态：45 字节前缀 + 弹芯（CName None，无子配件）。</summary>
    internal static byte[] Grenade95(string record) =>
        GrenadeBody(record, ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_frag_core"), 0x7DB27F8B, 0x4701, "None", TweakDbId.FromName("AttachmentSlots.GrenadeCore")));

    /// <summary>ManualSave-8 的 CuttingGrenadeLegendaryPlus 形态：弹芯 + 投掷方式，CName 都是 default，子配件尾部带一个 f32。</summary>
    internal static byte[] Grenade151(string record) =>
        GrenadeBody(record,
            ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_cutting_core"), 0x7DB27F8B, 0x46FD, "default", TweakDbId.FromName("AttachmentSlots.GrenadeCore"),
                ItemPartTreeTests.Part(TweakDbId.FromName("Items.w_homing_delivery"), 0xE6852992, 0x46FE, "default", TweakDbId.FromName("AttachmentSlots.GrenadeDelivery"), 2.5f)));

    private static byte[] GrenadeBody(string record, byte[] root)
    {
        var buffer = new List<byte>();
        var head = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(head, 7);
        head.AsSpan(4).Clear();
        ItemGenerator.QuantityHeader(TweakDbId.FromName(record)).CopyTo(head, 4);
        buffer.AddRange(head);
        buffer.Add(0x00);
        buffer.AddRange([0x00, 0x80, 0x0F, 0x70]);
        buffer.AddRange(U32(1));
        buffer.AddRange(new byte[12]);
        buffer.AddRange([0xFF, 0xFF, 0x7F, 0x7F]);
        buffer.AddRange(root);
        return buffer.ToArray();
    }

    private string WriteSave(string name, params (ulong Bag, byte[] Body)[] items) => GearGenerateTests.WriteSaveAt(_root, name, items);

    private Cp2077SaveDocument Open(string path) =>
        Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);

    private static SaveContainer Container(Cp2077SaveDocument document) =>
        (SaveContainer)typeof(Cp2077SaveDocument).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;

    private static string Hex(ulong value) => Convert.ToHexString(BitConverter.GetBytes(value)).ToLowerInvariant();

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
