using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Icons;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>T020：图标名规则（预研第 3 节的样例）和标志性武器进目录的规则，全部用合成的 tweak 文本。</summary>
public class ItemIconResolverTests
{
    private const string Tweaks = """
        package RTDB
        Item
        {
        	itemType = "ItemType.Gen_Misc";
        	itemCategory = "ItemCategory.General";
        }

        WeaponItem : Item
        {
        	itemType = "ItemType.Wea_Handgun";
        	itemCategory = "ItemCategory.Weapon";
        	CName appearanceResourceName = "";
        	CName[] tags = [ "Weapon" ];
        }

        Clothing : Item
        {
        	itemType = "ItemType.Clo_OuterChest";
        	itemCategory = "ItemCategory.Clothing";
        	CName[] tags = [ "Clothing" ];
        }

        package Items
        using RTDB

        Jacket : Clothing
        {
        	CName entityName = "player_outer_torso_item";
        	fk< ItemsFactoryAppearanceSuffixBase >[] appearanceSuffixes =
        	[
        		"itemsFactoryAppearanceSuffix.Gender", "itemsFactoryAppearanceSuffix.Camera"
        	];
        }

        Jacket_01_basic_01 : Jacket
        {
        	displayName = "LocKey#1";
        	CName appearanceName = "t2_jacket_01_basic_01_";
        }

        Boots : Clothing
        {
        	itemType = "ItemType.Clo_Feet";
        	CName entityName = "player_feet_item";
        	appearanceSuffixes = [ "itemsFactoryAppearanceSuffix.Gender", "itemsFactoryAppearanceSuffix.Partial" ];
        }

        Boots_05_basic_01 : Boots
        {
        	CName appearanceName = "s1_boots_05_basic_01_";
        }

        Base_Ajax : WeaponItem
        {
        	itemType = "ItemType.Wea_AssaultRifle";
        }

        Preset_Ajax_Default : Base_Ajax
        {
        	displayName = "LocKey#2";
        	CName appearanceResourceName = "Preset_Ajax_Default";
        	CName[] visualTags =
        	[
        		"Default"
        	];
        	fk< SlotItemPartPreset >[] slotPartListPreset =
        	[
        		{
        			slot = "AttachmentSlots.Receiver";
        			fk< Item > itemPartPreset = "Items.ajax_rcv";
        		}
        	];
        }

        Preset_Unity_Default : WeaponItem
        {
        	CName appearanceResourceName = "Preset_V_Unity";
        	visualTags = [ "Default" ];
        }

        Preset_Unity_Neon : Preset_Unity_Default
        {
        	visualTags = [ "Neon" ];
        }

        Preset_Unity_Tagged : Preset_Unity_Default
        {
        	visualTags += [ "Unknown" ];
        }

        GoodQualityFood1 : Item
        {
        	string iconPath = "food_fresh_fruits";
        }

        PrefixedIcon : Item
        {
        	iconPath = "UIIcon.food_fresh_fruits";
        }

        DanglingIcon : Item
        {
        	iconPath = "icon_money";
        }

        [ notQueryable ]
        Preset_Masamune_Rogue : Base_Ajax
        {
        	displayName = "LocKey#3";
        	CName appearanceResourceName = "Preset_Base_Masamune";
        	visualTags = [ "Rogue_Masamune" ];
        	tags += [ "IconicWeapon" ];
        	slotPartListPreset =
        	[
        		{
        			slot = "AttachmentSlots.Receiver";
        			fk< Item > itemPartPreset = "Items.masamune_rcv";
        		},
        		{
        			slot = "AttachmentSlots.IconicWeaponModLegendary";
        			itemPartPreset = "Items.RogueMasamuneWeaponMod";
        		}
        	];
        }

        [ notQueryable ]
        VHard_50_TechCool_Weapon1 : Preset_Masamune_Rogue
        {
        }

        [ notQueryable ]
        Preset_Masamune_Rogue_NPC : Preset_Masamune_Rogue
        {
        }

        [ notQueryable ]
        mq011_rogue_gun : Preset_Masamune_Rogue
        {
        }

        [ notQueryable ]
        Preset_Masamune_Rogue_Legendary : Preset_Masamune_Rogue
        {
        	tags += [ "DeprecatedIconic" ];
        }

        [ notQueryable ]
        Preset_Masamune_Rogue_NoIcon : Preset_Masamune_Rogue
        {
        	CName appearanceResourceName = "Preset_Missing";
        }

        [ notQueryable ]
        [ Debug ]
        Preset_Masamune_Rogue_Debug : Preset_Masamune_Rogue
        {
        }

        [ notQueryable ]
        Preset_Ajax_Hidden : Preset_Ajax_Default
        {
        	displayName = "LocKey#4";
        }

        package UIIcon
        using RTDB
        food_fresh_fruits : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons5.inkatlas";
        	atlasPartName = "food_fresh_fruits";
        }

        weapon_Preset_Ajax_Default_None__Default : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons2.inkatlas";
        	atlasPartName = "weapon_Preset_Ajax_Default_None__Default";
        }

        weapon_Preset_V_Unity_None__Neon : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons1.inkatlas";
        	atlasPartName = "weapon_Preset_V_Unity_None__Neon";
        }

        weapon_Preset_V_Unity_None__Default : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons1.inkatlas";
        	atlasPartName = "weapon_Preset_V_Unity_None__Default";
        }

        weapon_Preset_Base_Masamune_None__Rogue_Masamune : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons2.inkatlas";
        	atlasPartName = "weapon_Preset_Base_Masamune_None__Rogue_Masamune";
        }

        clothing_player_outer_torso_item_t2_jacket_01_basic_01__Female_TPP_ : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons7.inkatlas";
        	atlasPartName = "clothing_player_outer_torso_item_t2_jacket_01_basic_01__Female_TPP_";
        }

        clothing_player_feet_item_s1_boots_05_basic_01__Female_ : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons0.inkatlas";
        	atlasPartName = "clothing_player_feet_item_s1_boots_05_basic_01__Female_";
        }

        clothing_player_feet_item_s1_boots_05_basic_01__Male_ : UIIcon
        {
        	atlasResourcePath = "base/gameplay/gui/common/icons/items/item_icons0.inkatlas";
        	atlasPartName = "clothing_player_feet_item_s1_boots_05_basic_01__Male_";
        }
        """;

    private readonly TweakSourceIndex _index = TweakSourceIndex.Parse([("a.tweak", Tweaks)]);

    private ItemIconResolver Resolver => ItemIconResolver.Build(_index);

    [Fact]
    public void Index_KeepsIconFields_AndInheritedLists()
    {
        Assert.Equal("Preset_V_Unity", _index.FieldOf("Items.Preset_Unity_Neon", "appearanceResourceName"));
        Assert.Equal(new[] { "Neon" }, _index.ListOf("Items.Preset_Unity_Neon", "visualTags"));
        Assert.Equal(new[] { "Default", "Unknown" }, _index.ListOf("Items.Preset_Unity_Tagged", "visualTags"));
        Assert.Equal(
            new[] { "itemsFactoryAppearanceSuffix.Gender", "itemsFactoryAppearanceSuffix.Camera" },
            _index.ListOf("Items.Jacket_01_basic_01", "appearanceSuffixes"));
        Assert.Equal("t2_jacket_01_basic_01_", _index.FieldOf("Items.Jacket_01_basic_01", "appearanceName"));
        Assert.Equal("player_outer_torso_item", _index.FieldOf("Items.Jacket_01_basic_01", "entityName"));
        Assert.Equal("base/gameplay/gui/common/icons/items/item_icons5.inkatlas", _index.FieldOf("UIIcon.food_fresh_fruits", "atlasResourcePath"));
        Assert.Equal("food_fresh_fruits", _index.FieldOf("UIIcon.food_fresh_fruits", "atlasPartName"));
        Assert.Equal(new[] { "Weapon", "IconicWeapon" }, _index.TagsOf("Items.Preset_Masamune_Rogue"));
    }

    [Theory]
    [InlineData("Items.Preset_Ajax_Default", "weapon_Preset_Ajax_Default_None__Default")]
    [InlineData("Items.Preset_Unity_Neon", "weapon_Preset_V_Unity_None__Neon")]
    [InlineData("Items.Preset_Masamune_Rogue", "weapon_Preset_Base_Masamune_None__Rogue_Masamune")]
    [InlineData("Items.Jacket_01_basic_01", "clothing_player_outer_torso_item_t2_jacket_01_basic_01__Female_TPP_")]
    [InlineData("Items.Boots_05_basic_01", "clothing_player_feet_item_s1_boots_05_basic_01__Female_")]
    public void GeneratedName_FollowsNativeRule(string record, string expected)
    {
        Assert.Equal(expected, ItemIconResolver.GeneratedName(_index, record, IconGender.Female));
        var resolved = Resolver.Resolve(record, IconGender.Female);
        Assert.NotNull(resolved);
        Assert.Equal(expected, resolved.Icon.Name);
        Assert.Equal(IconNameSource.Generated, resolved.Source);
    }

    [Fact]
    public void Gender_UsesMaleIcon_WhenPresent_OtherwiseFemale()
    {
        Assert.Equal("clothing_player_feet_item_s1_boots_05_basic_01__Male_", Resolver.Resolve("Items.Boots_05_basic_01", IconGender.Male)!.Icon.Name);
        Assert.Equal("clothing_player_outer_torso_item_t2_jacket_01_basic_01__Male_TPP_", ItemIconResolver.GeneratedName(_index, "Items.Jacket_01_basic_01", IconGender.Male));
        Assert.Equal("clothing_player_outer_torso_item_t2_jacket_01_basic_01__Female_TPP_", Resolver.Resolve("Items.Jacket_01_basic_01", IconGender.Male)!.Icon.Name);
    }

    [Fact]
    public void IconPath_WinsOverGenerated_AndPackagePrefixIsOptional()
    {
        var food = Resolver.Resolve("Items.GoodQualityFood1", IconGender.Female);
        Assert.Equal("food_fresh_fruits", food!.Icon.Name);
        Assert.Equal(IconNameSource.IconPath, food.Source);
        Assert.Equal("base/gameplay/gui/common/icons/items/item_icons5.inkatlas", food.Icon.AtlasPath);
        Assert.Equal("food_fresh_fruits", Resolver.Resolve("Items.PrefixedIcon", IconGender.Female)!.Icon.Name);
    }

    [Fact]
    public void Fallbacks_AndMisses()
    {
        // 标签 Unknown 拼出来的名字不存在，去掉标签也不存在，退到 _Default。
        var tagged = Resolver.Resolve("Items.Preset_Unity_Tagged", IconGender.Female);
        Assert.Equal("weapon_Preset_V_Unity_None__Default", tagged!.Icon.Name);
        Assert.Equal(IconNameSource.Fallback, tagged.Source);
        Assert.Null(Resolver.Resolve("Items.DanglingIcon", IconGender.Female));
        Assert.Null(Resolver.Resolve("Items.Preset_Masamune_Rogue_NoIcon", IconGender.Female));
        Assert.Null(Resolver.Resolve("Items.Missing", IconGender.Female));
        Assert.Equal(8, Resolver.IconCount);
    }

    [Fact]
    public void Catalog_AdmitsObtainableIconicWeapons_Only()
    {
        var catalog = ItemNameCatalog.FromParts(_index,
        [
            new LocEntry(1, "", "夹克", ""),
            new LocEntry(2, "", "阿贾克斯", ""),
            new LocEntry(3, "", "偏见", ""),
            new LocEntry(4, "", "隐藏", "")
        ]);
        var ranged = catalog.Gear!.Entries.Where(entry => entry.Kind == GearKinds.Ranged).Select(entry => entry.RecordName).ToArray();
        Assert.Equal(new[] { "Items.Preset_Ajax_Default", "Items.Preset_Masamune_Rogue" }, ranged.Order(StringComparer.Ordinal));
        var rogue = catalog.Gear.Entries.Single(entry => entry.RecordName == "Items.Preset_Masamune_Rogue");
        Assert.True(rogue.Iconic);
        Assert.Equal("偏见", rogue.Label);
        Assert.Contains(catalog.PresetParts("Items.Preset_Masamune_Rogue")!, part => part.Slot == "AttachmentSlots.IconicWeaponModLegendary" && part.Item == "Items.RogueMasamuneWeaponMod");
        // 继承了同名中文名的变体都被规则挡掉，所以“偏见”只有一条，不标同名。
        Assert.False(rogue.LabelShared);
    }

    [Fact]
    public void Catalog_WithoutIconCheck_KeepsOldNotQueryableRule()
    {
        var catalog = ItemNameCatalog.FromParts(_index, [new LocEntry(2, "", "阿贾克斯", ""), new LocEntry(3, "", "偏见", "")]);
        var gear = GearCatalog.Build(_index, catalog.Resolve, ItemNameCatalog.IsInternalRecord);
        Assert.Equal(new[] { "Items.Preset_Ajax_Default" }, gear.Entries.Where(entry => entry.Kind == GearKinds.Ranged).Select(entry => entry.RecordName));
    }

    [Theory]
    [InlineData("Items.Preset_Masamune_Rogue", "notQueryable", "IconicWeapon", true)]
    [InlineData("Items.Preset_Achilles_Collectible", "notQueryable,EP1", "IconicWeapon", true)]
    [InlineData("Items.Legendary_Igla_Sovereign", "notQueryable", "IconicWeapon,CraftableIconic", true)]
    [InlineData("Items.Preset_Masamune_Rogue", "", "IconicWeapon", false)]
    [InlineData("Items.Preset_Masamune_Rogue", "notQueryable,Debug", "IconicWeapon", false)]
    [InlineData("Items.Preset_Ajax_Default", "notQueryable", "Weapon", false)]
    [InlineData("Items.Preset_Kenshin_Royce_Epic", "notQueryable", "IconicWeapon,DeprecatedIconic", false)]
    [InlineData("Items.VHard_50_IntTech_Weapon3", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Weak_20_Cool_Weapon4", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Avg_10_Int_Netrunner_Weapon5", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.mq007_skippy", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Preset_Q001_Lexington", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Sniper_Nest_Version_Tech_Sniper_Rifle", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Preset_Warden_Boris_Hologram", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Preset_Nue_Jackie_Left_Hand", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Preset_HMG_Boss", "notQueryable", "IconicWeapon", false)]
    [InlineData("Items.Vehicle_Missile_Launcher_Iconic", "notQueryable", "IconicWeapon", false)]
    [InlineData("TEST.GoldenGun", "notQueryable", "IconicWeapon", false)]
    public void ObtainableIconic_Rule(string record, string attributes, string tags, bool expected)
    {
        var attributeList = attributes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, GearCatalog.IsObtainableIconic(record, attributeList, tags.Split(',')));
    }
}
