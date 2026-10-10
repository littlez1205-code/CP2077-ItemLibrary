using System.Text.Json;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>
/// T023 收尾冲刺：网络接入仓、配方（29 字节堆叠）、同名多品阶合并为一张卡（标签、排序、默认项、按卡片分页）、装备品质色。
/// </summary>
public class FinalSprintTests : IDisposable
{
    private const string Tweaks = """
        package RTDB
        Item
        {
        	fk< ItemType > itemType = "ItemType.Gen_Misc";
        }

        ItemRecipe : Item
        {
        	CName[] tags =
        	[
        		"Recipe", "SkipActivityLogOnRemove", "HideInBackpackUI"
        	];
        	fk< CraftingResult > craftingResult = "";
        }

        WeaponItem : Item
        {
        }

        package Items
        using RTDB

        [ notQueryable ]
        Part : Item
        {
        	tags = [ "itemPart" ];
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
        BaseDeck : SystemReplacementModule
        {
        	displayName = "LocKey#10";
        	iconPath = "cw_deck";
        	fk< ItemBlueprint > blueprint = "Items.CyberdeckBlueprint2Slots";
        	fk< SlotItemPartListElement >[] slotPartList =
        	[
        		"Items.GenericRootSlotItem"
        	];
        	tags +=
        	[
        		"Cyberdeck"
        	];
        	fk< Quality > quality = "Quality.Common";
        }

        [ notQueryable ]
        AdvancedEpicDeck : BaseDeck
        {
        	quality = "Quality.Epic";
        	blueprint = "Items.CyberdeckBlueprint5Slots";
        }

        AdvancedArasakaShadowMKIVEpic : AdvancedEpicDeck
        {
        	friendlyName = "Arasaka MK IV";
        	displayName = "LocKey#11";
        	nextUpgradeItem = "Items.AdvancedArasakaShadowMKIVEpicPlus";
        }

        [ notQueryable ]
        AdvancedArasakaShadowMKIVEpicPlus : AdvancedEpicDeck
        {
        	displayName = "LocKey#11";
        	quality = "Quality.EpicPlus";
        }

        [ notQueryable ]
        [ EP1 ]
        HauntedCyberdeck : AdvancedEpicDeck
        {
        	displayName = "LocKey#12";
        	tags += [ "Haunted_Deck" ];
        	statModifiers += [ "Quality.IconicItem" ];
        }

        [ notQueryable ]
        [ EP1 ]
        HauntedCyberdeck_Rare : HauntedCyberdeck
        {
        	quality = "Quality.Rare";
        	nextUpgradeItem = "Items.HauntedCyberdeck_RarePlus";
        }

        [ notQueryable ]
        [ EP1 ]
        HauntedCyberdeck_RarePlus : HauntedCyberdeck
        {
        	quality = "Quality.RarePlus";
        }

        [ notQueryable ]
        FuyutsuiCommonMKI : BaseDeck
        {
        	displayName = "LocKey#13";
        }

        [ notQueryable ]
        CyberdeckProgram : Part
        {
        	iconPath = "program_generic";
        	itemType = "ItemType.Prt_Program";
        	placementSlots = [ "AttachmentSlots.CyberdeckProgram1" ];
        }

        OverheatLvl3Program : CyberdeckProgram
        {
        	displayName = "LocKey#20";
        	quality = "Quality.Epic";
        }

        OverheatLvl4Program : CyberdeckProgram
        {
        	displayName = "LocKey#20";
        	quality = "Quality.Legendary";
        }

        OverheatLvl4PlusPlusProgram : CyberdeckProgram
        {
        	displayName = "LocKey#20";
        	quality = "Quality.Legendary";
        }

        [ notQueryable ]
        Mod : Part
        {
        	fk< ItemType > itemType = "ItemType.Prt_Mod";
        	iconPath = "mod_generic";
        	placementSlots = [ "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1" ];
        }

        PowerMod2_Epic : Mod
        {
        	displayName = "LocKey#40";
        	quality = "Quality.Epic";
        }

        w_muzzle_brake_10 : Mod
        {
        	displayName = "LocKey#41";
        	fk< ItemType > itemType = "ItemType.Prt_Muzzle";
        	quality = "Quality.Random";
        	visualTags = [ "Default" ];
        	placementSlots = [ "AttachmentSlots.Muzzle" ];
        }

        w_muzzle_brake_10_legendary : w_muzzle_brake_10
        {
        }

        Preset_Igla_Sovereign : WeaponItem
        {
        	displayName = "LocKey#50";
        	quality = "Quality.Random";
        	tags = [ "IconicWeapon" ];
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_shotgun_igla_rcv"; }
        	];
        }

        Common_Igla_Sovereign : Preset_Igla_Sovereign
        {
        	tags += [ "CraftableIconic" ];
        }

        Legendary_Igla_Sovereign : Preset_Igla_Sovereign
        {
        	tags += [ "CraftableIconic" ];
        }

        Preset_Chao_Default : WeaponItem
        {
        	displayName = "LocKey#51";
        	quality = "Quality.Random";
        	visualTags = [ "Default" ];
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_handgun_chao_rcv"; }
        	];
        }

        Preset_Chao_Neon : Preset_Chao_Default
        {
        	visualTags = [ "Neon" ];
        }

        [ notQueryable ]
        Recipe_QuickHackBase : ItemRecipe
        {
        }

        Recipe_OverheatLvl3Program : Recipe_QuickHackBase
        {
        	displayName = "LocKey#20";
        	quality = "Quality.Epic";
        	craftingResult =
        	{
        		item = "Items.OverheatLvl3Program";
        	};
        }

        Recipe_OverheatLvl4PlusPlusProgram : Recipe_QuickHackBase
        {
        	displayName = "LocKey#20";
        	quality = "Quality.Legendary";
        	craftingResult =
        	{
        		item = "Items.OverheatLvl4PlusPlusProgram";
        		amount = 1;
        	};
        }

        WeaponMod_Recipe : ItemRecipe
        {
        }

        PowerMod2_Epic_Recipe : WeaponMod_Recipe
        {
        	craftingResult =
        	{
        		item = "Items.PowerMod2_Epic";
        	};
        }

        RecipeOwnIcon : ItemRecipe
        {
        	displayName = "LocKey#60";
        	iconPath = "recipe_own";
        	craftingResult =
        	{
        		item = "Items.PowerMod2_Epic";
        	};
        }

        RecipeNoIcon : ItemRecipe
        {
        	displayName = "LocKey#61";
        	craftingResult =
        	{
        		item = "Items.NotAnItem";
        	};
        }

        [ notQueryable ]
        Recipe_Hidden : ItemRecipe
        {
        	displayName = "LocKey#60";
        }

        QuestRecipe : ItemRecipe
        {
        	displayName = "LocKey#60";
        	tags += [ "Quest" ];
        }

        NoTextRecipe : ItemRecipe
        {
        	displayName = "LocKey#999";
        }

        package UIIcon
        cw_deck
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/cyberware.inkatlas";
        	atlasPartName = "cw_deck";
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

        recipe_own
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons4.inkatlas";
        	atlasPartName = "recipe_own";
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(10, "", "", "旧接入仓"),
        new(11, "", "", "荒坂 4 型"),
        new(12, "", "", "军用科技篇章 6 型"),
        new(13, "", "", "富乐成 1 型"),
        new(20, "", "", "义体过热"),
        new(40, "", "", "破洞百出"),
        new(41, "", "", "吃人婆婆"),
        new(50, "", "", "主权"),
        new(51, "", "", "超式"),
        new(60, "", "", "自带图标的配方"),
        new(61, "", "", "没有图标的配方")
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-t023-" + Guid.NewGuid().ToString("N"));
    private readonly TweakSourceIndex _index;
    private readonly Cp2077ItemNames _names;
    private readonly Cp2077Adapter _adapter;

    public FinalSprintTests()
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

    private IReadOnlyList<GearEntry> All => [.. _names.Gear, .. _names.Stackables];

    private GearEntry Entry(string record) => All.Single(entry => entry.RecordName == record);

    [Fact]
    public void Index_ReadsTheCraftingResultItem()
    {
        Assert.Equal("Items.OverheatLvl3Program", _index.FieldOf("Items.Recipe_OverheatLvl3Program", TweakSourceIndex.CraftingResultItem));
        Assert.Equal("Items.OverheatLvl4PlusPlusProgram", _index.FieldOf("Items.Recipe_OverheatLvl4PlusPlusProgram", TweakSourceIndex.CraftingResultItem));
        // 基类写的是空串 craftingResult = ""，没有 item。
        Assert.Null(_index.FieldOf("RTDB.ItemRecipe", TweakSourceIndex.CraftingResultItem));
        Assert.Null(_index.FieldOf("Items.NoTextRecipe", TweakSourceIndex.CraftingResultItem));
        // 别的块里的 item 字段不算。
        Assert.Null(_index.FieldOf("Items.PowerMod2_Epic", TweakSourceIndex.CraftingResultItem));
    }

    [Fact]
    public void Recipes_AreAStackableKind_WithNameTierTypeAndExclusions()
    {
        string[] recipes = [.. _names.Stackables.Where(entry => entry.Kind == GearKinds.Recipe).Select(entry => entry.RecordName).Order(StringComparer.Ordinal)];
        Assert.Equal(
            new[] { "Items.PowerMod2_Epic_Recipe", "Items.RecipeNoIcon", "Items.RecipeOwnIcon", "Items.Recipe_OverheatLvl3Program", "Items.Recipe_OverheatLvl4PlusPlusProgram" },
            recipes);
        Assert.True(GearKinds.IsStackable(GearKinds.Recipe));
        Assert.DoesNotContain(_names.Gear, entry => entry.Kind == GearKinds.Recipe);

        // 自己没有 displayName 的武器模组配方用产物的名字；品阶取产物名字末尾的 Epic。
        var mod = Entry("Items.PowerMod2_Epic_Recipe");
        Assert.Equal("破洞百出", mod.Label);
        Assert.Equal(GearQuality.Epic, mod.Quality);
        Assert.Equal("模组配方", mod.TypeLabel);
        Assert.Equal("Items.PowerMod2_Epic", mod.CraftedItem);

        // 快速破解配方：品质取产物的 quality，++ 取自产物名字。
        var plusPlus = Entry("Items.Recipe_OverheatLvl4PlusPlusProgram");
        Assert.Equal("快速破解配方", plusPlus.TypeLabel);
        Assert.Equal((GearQuality.Legendary, 2), (plusPlus.Quality, plusPlus.Plus));
        Assert.Equal("传说++", plusPlus.VariantLabel);
        Assert.Equal("史诗", Entry("Items.Recipe_OverheatLvl3Program").VariantLabel);
        Assert.Null(Entry("Items.RecipeNoIcon").TypeLabel);

        var path = WriteSave("recipes", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var kinds = document.GenerateKinds;
        Assert.Contains(kinds, kind => kind.Id == GearKinds.Recipe && kind.Label == "配方" && kind.Stackable);
        Assert.Equal(5, document.ListGeneratable("recipe", null, 0, 50).Total);
        Assert.Equal(5, document.ListGeneratable(null, "配方", 0, 50).Total);
        var item = document.FindGeneratable("Items.PowerMod2_Epic_Recipe")!;
        Assert.True(item.Stackable);
        Assert.Null(item.Held);
        Assert.Equal(Cp2077SaveDocument.DefaultStackMax, item.MaxQuantity);
        Assert.NotNull(document.StackResource("Items.PowerMod2_Epic_Recipe"));
    }

    [Fact]
    public async Task Recipes_ObtainAsA29ByteStack()
    {
        var saves = Path.Combine(_root, "saves");
        var path = GearGenerateTests.WriteSaveAt(saves, "ManualSave-0", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);
        var count = ((Cp2077SaveDocument)_adapter.Load(path)).PlayerItemCount;

        var report = library.Obtain(_adapter, path, "Items.PowerMod2_Epic_Recipe", null);
        Assert.True(report.Stackable);
        Assert.True(report.Inserted);
        Assert.Equal(1, report.After);
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(count + 1, after.PlayerItemCount);
        Assert.Equal(1, after.FindGeneratable("Items.PowerMod2_Epic_Recipe")!.Held);
        var stack = InventoryResearch.Read(_adapter, path, InventoryResearch.DefaultBagId).Items
            .Single(entry => entry.TweakDbId == Hex(TweakDbId.FromName("Items.PowerMod2_Epic_Recipe")));
        Assert.Equal(29, stack.Length);
        Assert.Equal(1u, stack.Quantity);
        Assert.Equal(2, library.Obtain(_adapter, path, "Items.PowerMod2_Epic_Recipe", 1).After);
    }

    [Fact]
    public void RecipeIcons_OwnIconPath_ThenTheProduct_ElsePlaceholder()
    {
        var resolver = _names.IconResolver!;
        Assert.Equal("recipe_own", resolver.Resolve("Items.RecipeOwnIcon", IconGender.Female)!.Icon.Name);
        Assert.Equal("mod_generic", resolver.Resolve("Items.PowerMod2_Epic_Recipe", IconGender.Female)!.Icon.Name);
        Assert.Equal("program_generic", resolver.Resolve("Items.Recipe_OverheatLvl3Program", IconGender.Female)!.Icon.Name);
        Assert.Null(resolver.Resolve("Items.RecipeNoIcon", IconGender.Female));
    }

    [Fact]
    public void Cyberdecks_AreCyberware_LabelledAndSearchable()
    {
        var gear = _names.Gear;
        string[] decks = [.. gear.Where(entry => entry.TypeLabel == "网络接入仓").Select(entry => entry.RecordName).Order(StringComparer.Ordinal)];
        // 可查询的 + 升级档 + 带 notQueryable 的接入仓升级链起点（配方做出的 HauntedCyberdeck_Rare）；旧接入仓和基类不收。
        Assert.Equal(
            new[] { "Items.AdvancedArasakaShadowMKIVEpic", "Items.AdvancedArasakaShadowMKIVEpicPlus", "Items.HauntedCyberdeck_Rare", "Items.HauntedCyberdeck_RarePlus" },
            decks);
        Assert.All(decks, record => Assert.Equal(GearKinds.Cyberware, Entry(record).Kind));
        Assert.Equal(GearKinds.Cyberware, _names.KindOf(TweakDbId.FromName("Items.FuyutsuiCommonMKI")));
        Assert.True(Entry("Items.HauntedCyberdeck_Rare").Iconic);
        Assert.Empty(_index.FindPresetParts("Items.AdvancedArasakaShadowMKIVEpic")!);

        var path = WriteSave("decks", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(4, document.ListGeneratable("cyberware", "网络接入仓", 0, 50).Total);
        Assert.Equal(2, document.ListGeneratableGroups(null, "网络接入仓", 0, 50).Total);
        Assert.Equal(2, document.ListGeneratable(null, "Arasaka", 0, 50).Total);
        Assert.Equal("Items.AdvancedArasakaShadowMKIVEpicPlus", Assert.Single(document.ListGeneratableGroups(null, "荒坂", 0, 50).Groups).Variants[1].Target);
    }

    [Fact]
    public void Cyberdecks_GenerateAsGenericItemRootOnly_91Bytes()
    {
        var path = WriteSave("deck-gen", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        foreach (var target in new[] { "Items.AdvancedArasakaShadowMKIVEpicPlus", "Items.HauntedCyberdeck_Rare" })
        {
            var document = (Cp2077SaveDocument)_adapter.Load(path);
            var applied = document.Generate(new GenerateRequest(target, 1));
            var record = applied.Records[0];
            Assert.Equal(91, record.Body.Length);
            Assert.Equal(TweakDbId.FromName(target), BitConverter.ToUInt64(record.Header));
            var tree = ItemPartTree.Parse(record.Body);
            Assert.Equal(TweakDbId.FromName("Items.GenericItemRoot"), tree.Root.Tweak);
            Assert.Equal(TweakDbId.FromName("AttachmentSlots.GenericItemRoot"), tree.Root.Slot);
            Assert.Equal(ItemGenerator.GenericRootSeed, tree.Root.Seed);
            Assert.Empty(tree.Root.Children);
            Assert.Equal(new[] { record.Seed }, record.Seeds);
            document.VerifyOnlyGenerated(Open(path), applied);
        }
    }

    [Fact]
    public void Groups_MergeSameNameVariants_WithLabelsOrderAndDefault()
    {
        var groups = _names.Groups;
        GearGroup Group(string kind, string label) => groups.Single(group => group.Kind == kind && group.Label == label);
        string[] Labels(GearGroup group) => [.. group.Variants.Select(entry => entry.VariantLabel!)];

        // 名字末尾品阶 / quality 字段：从低到高，默认最高。
        var deck = Group(GearKinds.Cyberware, "荒坂 4 型");
        Assert.Equal(new[] { "史诗", "史诗+" }, Labels(deck));
        Assert.Equal("品阶", deck.Noun);
        Assert.Equal("Items.AdvancedArasakaShadowMKIVEpicPlus", deck.Default.RecordName);
        Assert.Equal(new[] { "稀有", "稀有+" }, Labels(Group(GearKinds.Cyberware, "军用科技篇章 6 型")));

        // 快速破解：quality 都是 Legendary 时，++ 取自名字。
        var overheat = Group(GearKinds.Quickhack, "义体过热");
        Assert.Equal(new[] { "史诗", "传说", "传说++" }, Labels(overheat));
        Assert.Equal("Items.OverheatLvl4PlusPlusProgram", overheat.Default.RecordName);

        // 可制作标志性武器：Preset_ 是原版，排最前；默认传说。
        var sovereign = Group(GearKinds.Ranged, "主权");
        Assert.Equal(new[] { "原版", "普通", "传说" }, Labels(sovereign));
        Assert.Equal("Items.Legendary_Igla_Sovereign", sovereign.Default.RecordName);
        Assert.All(sovereign.Variants, entry => Assert.True(entry.Iconic));

        // 款式（visualTags）：默认款在前，名词是「款式」。
        var chao = Group(GearKinds.Ranged, "超式");
        Assert.Equal(new[] { "默认", "Neon" }, Labels(chao));
        Assert.Equal("款式", chao.Noun);
        Assert.Equal(0, chao.DefaultIndex);

        // 只有一条带品阶：另一条记为「标准」。
        var muzzle = Group(GearKinds.Mod, "吃人婆婆");
        Assert.Equal(new[] { "标准", "传说" }, Labels(muzzle));
        Assert.Equal("Items.w_muzzle_brake_10_legendary", muzzle.Default.RecordName);

        // 只有一条的卡片：没有选项标签和名词。
        var single = Group(GearKinds.Mod, "破洞百出");
        Assert.Null(single.Noun);
        Assert.Null(Assert.Single(single.Variants).VariantLabel);
        Assert.All(groups, group => Assert.Equal(group.Variants.Count, group.Variants.Select(entry => entry.VariantLabel).Distinct().Count()));
        Assert.All(groups, group => Assert.All(group.Variants, entry => Assert.Equal(group.Key, entry.GroupKey)));
    }

    [Fact]
    public void Groups_FallBackToRecordNamesAndDisambiguate()
    {
        static GearEntry Make(string record, string? quality = null, int plus = 0, string? nameTier = null, string[]? tags = null) =>
            new(record, TweakDbId.FromName(record), "同名", GearKinds.Melee, true)
            {
                Quality = quality,
                Plus = plus,
                FieldTier = quality is null ? null : quality + (plus == 2 ? "PlusPlus" : plus == 1 ? "Plus" : ""),
                NameTier = nameTier,
                VisualTags = tags ?? []
            };

        // 都没有品阶也没有不同款式：用记录名里不同的部分（Default 显示为默认）。
        var styles = Assert.Single(GearCatalog.Group([Make("Items.Preset_Knife_Default", tags: ["Military"]), Make("Items.Preset_Knife_Military", tags: ["Military"])]));
        Assert.Equal(new[] { "默认", "Military" }, styles.Variants.Select(entry => entry.VariantLabel));
        Assert.Equal("款式", styles.Noun);

        // 品阶重复：补上记录名里不同的部分，不重复的那条保持原样。
        var regina = Assert.Single(GearCatalog.Group(
        [
            Make("Items.SubdermalEpicPlus", GearQuality.Epic, 1),
            Make("Items.SubdermalEpicPlus_Regina", GearQuality.Epic, 1),
            Make("Items.SubdermalLegendary", GearQuality.Legendary)
        ]));
        Assert.Equal(new[] { "史诗+", "史诗+ · Regina", "传说" }, regina.Variants.Select(entry => entry.VariantLabel));
        Assert.Equal("Items.SubdermalLegendary", regina.Default.RecordName);

        Assert.Equal(new[] { "Memory", "Stamina" }, GearCatalog.DiffLabels(["RecipeMemoryBooster", "RecipeStaminaBooster"]));
        Assert.Equal(new[] { "Rogue", "Stash" }, GearCatalog.DiffLabels(["Preset_Masamune_Rogue", "Preset_Masamune_Rogue_Stash"]));
    }

    [Fact]
    public void GroupPages_CountAndPageByCard_SearchPicksTheMatchingVariant()
    {
        var path = WriteSave("pages", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var items = document.ListGeneratable(null, null, 0, 200);
        var all = document.ListGeneratableGroups(null, null, 0, 200);
        Assert.Equal(items.Total, all.ItemTotal);
        Assert.True(all.Total < all.ItemTotal);
        Assert.Equal(all.Total, _names.Groups.Count);

        // 分页按卡片。
        var first = document.ListGeneratableGroups("cyberware", null, 0, 1);
        var second = document.ListGeneratableGroups("cyberware", null, 1, 1);
        Assert.Equal(2, first.Total);
        Assert.Equal(4, first.ItemTotal);
        Assert.NotEqual(Assert.Single(first.Groups).Key, Assert.Single(second.Groups).Key);
        Assert.Empty(document.ListGeneratableGroups("cyberware", null, 2, 1).Groups);
        Assert.Throws<InvalidDataException>(() => document.ListGeneratableGroups(null, null, 0, 201));

        // 搜索命中非默认的一条：整张卡都列出，默认项改为命中的那条。
        var hit = Assert.Single(document.ListGeneratableGroups(null, "Common_Igla", 0, 50).Groups);
        Assert.Equal(3, hit.Variants.Count);
        Assert.Equal("Items.Common_Igla_Sovereign", hit.Variants[hit.DefaultIndex].Target);
        Assert.Equal("普通", hit.Variants[hit.DefaultIndex].VariantLabel);
        Assert.Equal("品阶", hit.VariantNoun);
        var plain = Assert.Single(document.ListGeneratableGroups(null, "主权", 0, 50).Groups);
        Assert.Equal("Items.Legendary_Igla_Sovereign", plain.Variants[plain.DefaultIndex].Target);
        Assert.Equal(GearQuality.Legendary, plain.Quality);
        Assert.True(plain.Iconic);
        // 选项标签也能搜。
        Assert.Equal(1, document.ListGeneratableGroups("quickhack", "传说++", 0, 50).Total);
        Assert.Equal(0, Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources).ListGeneratableGroups(null, null, 0, 50).Total);
    }

    [Fact]
    public void GroupCounts_PerKind_MatchTheGroupPages()
    {
        var path = WriteSave("counts", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var kinds = document.GenerateKinds;
        foreach (var query in new string?[] { null, "主权", "传说", "Common_Igla", "传说++", "不存在的物品" })
        {
            var counts = document.CountGeneratableGroups(query);
            // 键与分类顺序一致，没有命中的分类也在里面（为 0）。
            Assert.Equal(kinds.Select(kind => kind.Id), counts.Keys);
            foreach (var kind in kinds)
                Assert.Equal(document.ListGeneratableGroups(kind.Id, query, 0, 1).Total, counts[kind.Id]);
            Assert.Equal(document.ListGeneratableGroups(null, query, 0, 1).Total, counts.Values.Sum());
        }

        Assert.True(document.CountGeneratableGroups("传说").Count(pair => pair.Value > 0) >= 2);
        Assert.All(document.CountGeneratableGroups("不存在的物品").Values, value => Assert.Equal(0, value));
        // 名字表不可用：没有分类，计数为空。
        Assert.Empty(Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources).CountGeneratableGroups("传说"));
    }

    [Fact]
    public void QualityColours_FollowNameAndPrefixTiers_RandomStaysNeutral()
    {
        Assert.Equal((GearQuality.Legendary, 2), GearQuality.FromNameSuffix("Items.AdvancedRamUpgradeLegendaryPlusPlus"));
        Assert.Equal((GearQuality.Uncommon, 1), GearQuality.FromNameSuffix("Items.AdvancedArasakaShadowMKIIUncommonPlus"));
        Assert.Equal((GearQuality.Legendary, 0), GearQuality.FromNameSuffix("Items.w_att_scope_short_07_legendary"));
        Assert.Equal((GearQuality.Epic, 0), GearQuality.FromNameSuffix("Items.ShotgunMod1_Epic"));
        Assert.Null(GearQuality.FromNameSuffix("Items.QuickHackRareMaterial1"));
        Assert.Null(GearQuality.FromNameSuffix("Items.Preset_Chao_Default"));
        Assert.Null(GearQuality.FromNameSuffix("Items.Legendary"));
        Assert.Equal((GearQuality.Rare, 0), GearQuality.FromPrefix("Items.Rare_Igla_Sovereign"));
        Assert.Equal((GearQuality.PresetPrefix, 0), GearQuality.FromPrefix("Items.Preset_Igla_Sovereign"));
        Assert.Null(GearQuality.FromPrefix("Items.LegendaryMaterial1"));
        Assert.Equal("传说++", GearQuality.TierLabel(GearQuality.Legendary, 2));
        Assert.True(GearQuality.Rank(GearQuality.Legendary, 0) > GearQuality.Rank(GearQuality.Epic, 2));

        // 名字里有品阶的按品阶上色，即使 quality 是 Random；没有品阶的 Random 保持中性。
        Assert.Equal(GearQuality.Legendary, Entry("Items.w_muzzle_brake_10_legendary").Quality);
        Assert.Null(Entry("Items.w_muzzle_brake_10").Quality);
        Assert.Equal(GearQuality.Common, Entry("Items.Common_Igla_Sovereign").Quality);
        Assert.Null(Entry("Items.Preset_Igla_Sovereign").Quality);
        Assert.Null(Entry("Items.Preset_Chao_Neon").Quality);
        Assert.False(Entry("Items.Preset_Chao_Neon").Iconic);
        Assert.Equal((GearQuality.Epic, 1), (Entry("Items.AdvancedArasakaShadowMKIVEpicPlus").Quality, Entry("Items.AdvancedArasakaShadowMKIVEpicPlus").Plus));
        // 合并卡片取默认（最高）一条的品质。
        var path = WriteSave("colours", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        var muzzle = Assert.Single(document.ListGeneratableGroups("mod", "吃人婆婆", 0, 50).Groups);
        Assert.Equal((GearQuality.Legendary, false), (muzzle.Quality, muzzle.Iconic));
        Assert.Null(Assert.Single(document.ListGeneratableGroups("ranged", "超式", 0, 50).Groups).Quality);
    }

    [Fact]
    public async Task Bridge_LibraryCatalog_ReturnsGroups()
    {
        var saves = Path.Combine(_root, "bridge-saves");
        var save = GearGenerateTests.WriteSaveAt(saves, "ManualSave-0", (1, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var registry = new AdapterRegistry();
        registry.Register(_adapter);
        await using var bridge = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "bridge-backups"), Path.Combine(_root, "bridge-skins"));
        var slotJson = JsonSerializer.Serialize(save);

        using var page = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l1\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"kind\":\"ranged\",\"take\":1}}"));
        Assert.Equal("ok", page.RootElement.GetProperty("status").GetString());
        var data = page.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(5, data.GetProperty("itemTotal").GetInt32());
        var group = data.GetProperty("groups")[0];
        Assert.Equal("主权", group.GetProperty("label").GetString());
        Assert.Equal("品阶", group.GetProperty("variantNoun").GetString());
        Assert.Equal(2, group.GetProperty("defaultIndex").GetInt32());
        Assert.Equal("传说", group.GetProperty("variants")[2].GetProperty("variantLabel").GetString());
        Assert.Equal("ranged", data.GetProperty("kind").GetString());
        Assert.False(data.TryGetProperty("kindCounts", out _));

        // T024：kindFallback —— 没有 kind 或 kind 未知时改用第一个分类；有搜索词时附带各分类的命中卡片数。
        var document = (Cp2077SaveDocument)_adapter.Load(save);
        var first = document.GenerateKinds[0].Id;
        using var fallback = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l3\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"kindFallback\":true,\"take\":1}}"));
        var fallbackData = fallback.RootElement.GetProperty("data");
        Assert.Equal(first, fallbackData.GetProperty("kind").GetString());
        Assert.Equal(document.ListGeneratableGroups(first, null, 0, 1).Total, fallbackData.GetProperty("total").GetInt32());
        using var unknown = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l4\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"kind\":\"gone\",\"kindFallback\":true,\"query\":\"传说\",\"take\":1}}"));
        var unknownData = unknown.RootElement.GetProperty("data");
        Assert.Equal(first, unknownData.GetProperty("kind").GetString());
        var counts = unknownData.GetProperty("kindCounts");
        foreach (var kind in document.GenerateKinds)
            Assert.Equal(document.ListGeneratableGroups(kind.Id, "传说", 0, 1).Total, counts.GetProperty(kind.Id).GetInt32());
        using var strict = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l5\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"kind\":\"gone\",\"take\":1}}"));
        Assert.Equal("error", strict.RootElement.GetProperty("status").GetString());
        using var all = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l6\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"take\":1}}"));
        Assert.False(all.RootElement.GetProperty("data").TryGetProperty("kind", out _));

        using var bad = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"l2\",\"method\":\"library.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slotJson + ",\"take\":0}}"));
        Assert.Equal("bad_args", bad.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private string WriteSave(string name, params (ulong Bag, byte[] Body)[] items) => GearGenerateTests.WriteSaveAt(_root, name, items);

    private Cp2077SaveDocument Open(string path) =>
        Cp2077SaveDocument.Open(SaveContainer.Read(path), _adapter.Resources);

    private static string Hex(ulong value) => Convert.ToHexString(BitConverter.GetBytes(value)).ToLowerInvariant();
}
