using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;

namespace GameTool.Tests;

public class TweakSourceTests
{
    [Fact]
    public void Package_Using_AndCrossPackageParent()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package RTDB
                Base
                {
                	displayName = "LocKey#7";
                }

                package Items
                using RTDB
                mq008_male_crowd.ent : Base
                {
                }
                """)
        ]);

        var child = index.Find(TweakDbId.FromName("Items.mq008_male_crowd.ent"));
        Assert.NotNull(child);
        Assert.Equal("LocKey#7", child.DisplayName);
    }

    [Fact]
    public void Inheritance_WalksParents_AndStopsOnCycles()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package Items
                Grand
                {
                	displayName = "LocKey#9";
                }
                Mid : Grand
                {
                }
                Child : Mid
                {
                }
                A : B
                {
                }
                B : A
                {
                }
                """)
        ]);

        Assert.Equal("LocKey#9", index.Find(TweakDbId.FromName("Items.Child"))!.DisplayName);
        Assert.Null(index.Find(TweakDbId.FromName("Items.A"))!.DisplayName);
    }

    [Fact]
    public void DisplayName_IgnoresNestedAndAcceptsTypePrefix()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package Items
                Nested
                {
                	inner =
                	{
                		displayName = "LocKey#99";
                	};
                }
                Typed
                {
                	CName displayName = "Gameplay-Status-Hack";
                }
                """)
        ]);

        Assert.Null(index.Find(TweakDbId.FromName("Items.Nested"))!.DisplayName);
        Assert.Equal("Gameplay-Status-Hack", index.Find(TweakDbId.FromName("Items.Typed"))!.DisplayName);
    }

    [Fact]
    public void Ep1_OverridesBase()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("base.tweak", """
                package Items
                Food
                {
                	displayName = "LocKey#1";
                }
                """),
            ("ep1.tweak", """
                package Items
                Food
                {
                	displayName = "LocKey#2";
                }
                """)
        ]);

        Assert.Equal(1, index.Overridden);
        Assert.Equal("LocKey#2", index.Find(TweakDbId.FromName("Items.Food"))!.DisplayName);
    }

    [Fact]
    public void Catalog_ResolvesLocKeyAndSecondaryKey()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package Items
                Fruit
                {
                	displayName = "LocKey#4922";
                }
                Hack
                {
                	displayName = "Gameplay-Status-Hack";
                }
                Plain
                {
                }
                """)
        ]);
        var entries = new LocEntry[]
        {
            new(4922, "fruit", "", "真果男"),
            new(8, "Gameplay-Status-Hack", "已破解", "男")
        };
        var catalog = ItemNameCatalog.FromParts(index, entries);
        var fruit = catalog.Resolve("Items.Fruit");
        Assert.True(fruit.Resolved);
        Assert.Equal("LocKey#4922", fruit.DisplayName);
        Assert.Equal("真果男", fruit.Text);
        Assert.Equal("真果男", fruit.Label);
        Assert.Equal("已破解", catalog.Resolve("Items.Hack").Text);
        var plain = catalog.Resolve("Items.Plain");
        Assert.False(plain.Resolved);
        Assert.Equal("Items.Plain", plain.RecordName);
        var missing = catalog.Resolve("Items.Missing");
        Assert.False(missing.Resolved);
        Assert.Null(missing.RecordName);
        Assert.Equal(Convert.ToHexString(BitConverter.GetBytes(TweakDbId.FromName("Items.Missing"))).ToLowerInvariant(), missing.Label);
    }

    [Fact]
    public void MissingRedmod_DoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "gt-noredmod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = ItemNameCatalog.Load(root);
        Assert.False(catalog.Available);
        Assert.False(catalog.Resolve("Items.Food").Resolved);
    }

    [Fact]
    public void Key_AcceptsSixteenHexAndRecordNames()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package Items
                Fruit
                {
                	displayName = "LocKey#1";
                }
                """)
        ]);
        var catalog = ItemNameCatalog.FromParts(index, [new LocEntry(1, "fruit", "真果", "")]);
        var id = TweakDbId.FromName("Items.Fruit");
        var hex = Convert.ToHexString(BitConverter.GetBytes(id)).ToLowerInvariant();

        Assert.True(ItemNameCatalog.TryParseKey(hex, out var bare));
        Assert.True(bare.IsId);
        Assert.Equal(id, bare.Id);
        Assert.True(ItemNameCatalog.TryParseKey("0x" + hex, out var prefixed));
        Assert.Equal(id, prefixed.Id);
        Assert.Equal("真果", catalog.Resolve(hex).Label);
        Assert.Equal("真果", catalog.Resolve("Items.Fruit").Label);

        Assert.False(ItemNameCatalog.TryParseKey("0x", out _));
        Assert.False(ItemNameCatalog.TryParseKey("1234567890abcdef01", out _));
    }

    [Fact]
    public void PresetParts_InlineEntries_ParentChain_AndAppend()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("preset.tweak", """
                package Items
                using RTDB

                Preset_Ajax_Default : Base_Ajax
                {
                	CName appearanceResourceName = "Preset_Ajax_Default";
                	displayName = "LocKey#5";
                	fk< SlotItemPartPreset >[] slotPartListPreset =
                	[
                		{
                			slot = "AttachmentSlots.Receiver";
                			fk< Item > itemPartPreset = "Items.w_rifle_assault__militech_ajax__rcv1";
                		},
                		{
                			slot = "AttachmentSlots.Barrel";
                			fk< Item > itemPartPreset = "Items.w_rifle_assault__militech_ajax__base1";
                		},
                		{
                			slot = "AttachmentSlots.Magazine";
                			fk< Item > itemPartPreset = "Items.w_rifle_assault__militech_ajax__mag_std";
                		},
                		{
                			slot = "AttachmentSlots.MagazineEmpty";
                			fk< Item > itemPartPreset = "Items.w_rifle_assault__militech_ajax__mag_stdr";
                		},
                		"Items.RifleScopesPartList", {
                			slot = "AttachmentSlots.Power_AR_SMG_LMG_WeaponMod1";
                		} : Power_AR_SMG_LMG_ModList_Slot1,
                		{
                			slot = "AttachmentSlots.Scope";
                			nested = { itemPartPreset = "Items.Hidden"; };
                		}
                	];
                	tags = [ "a" ];
                }

                Preset_Ajax_Neon : Preset_Ajax_Default
                {
                	CName[] visualTags =
                	[
                		"Neon"
                	];
                }

                Preset_Ajax_Plus : Preset_Ajax_Neon
                {
                	slotPartListPreset += [ { slot = "AttachmentSlots.Muzzle"; itemPartPreset = "Items.Muzzle1"; } ];
                }

                Base_Ajax
                {
                }
                """)
        ]);

        string[] expected =
        [
            "AttachmentSlots.Receiver=Items.w_rifle_assault__militech_ajax__rcv1",
            "AttachmentSlots.Barrel=Items.w_rifle_assault__militech_ajax__base1",
            "AttachmentSlots.Magazine=Items.w_rifle_assault__militech_ajax__mag_std",
            "AttachmentSlots.MagazineEmpty=Items.w_rifle_assault__militech_ajax__mag_stdr"
        ];
        static string[] Flat(IReadOnlyList<TweakPresetPart>? parts) => parts!.Select(part => part.Slot + "=" + part.Item).ToArray();
        Assert.Equal(expected, Flat(index.FindPresetParts("Items.Preset_Ajax_Default")));
        Assert.Equal(expected, Flat(index.FindPresetParts("Items.Preset_Ajax_Neon")));
        Assert.Equal([.. expected, "AttachmentSlots.Muzzle=Items.Muzzle1"], Flat(index.FindPresetParts("Items.Preset_Ajax_Plus")));
        Assert.Empty(index.FindPresetParts("Items.Base_Ajax")!);
        Assert.Null(index.FindPresetParts("Items.Missing"));
        Assert.Equal("LocKey#5", index.Find(TweakDbId.FromName("Items.Preset_Ajax_Neon"))!.DisplayName);
        Assert.Equal(4, index.Count);
    }
}
