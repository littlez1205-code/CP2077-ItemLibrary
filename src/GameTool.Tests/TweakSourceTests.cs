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

    [Fact]
    public void LocalizedDescription_And_EffectDescription()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("a.tweak", """
                package Items
                using RTDB

                BaseGun
                {
                    displayName = "LocKey#1";
                    localizedDescription = "LocKey#100";
                    primaryTriggerMode = "TriggerMode.FullAuto";
                    ammo = "Ammo.RifleAmmo";
                }

                Preset_Gun_Iconic : BaseGun
                {
                    displayName = "LocKey#2";
                    slotPartListPreset =
                    [
                        {
                            slot = "AttachmentSlots.IconicWeaponModLegendary";
                            itemPartPreset = "Items.GunIconicMod";
                        }
                    ];
                }

                GunIconicMod
                {
                    OnAttach = [ "Items.GunIconicModAbility" ];
                }

                GunIconicModAbility
                {
                    UIData =
                    {
                        localizedDescription = "LocKey#200";
                    };
                }

                CyberwareBase
                {
                    displayName = "LocKey#3";
                    localizedDescription = "LocKey#300";
                    variants = [ "Variants.Humanity15Cost" ];
                    OnEquip =
                    [
                        {
                            UIData =
                            {
                                localizedDescription = "LocKey#301";
                            };
                        }
                    ];
                }
                """)
        ]);

        Assert.Equal("LocKey#100", index.LocalizedDescriptionOf("Items.Preset_Gun_Iconic"));
        Assert.Equal("LocKey#200", index.EffectDescriptionOf("Items.Preset_Gun_Iconic"));
        Assert.Equal("TriggerMode.FullAuto", index.FieldOf("Items.Preset_Gun_Iconic", "primaryTriggerMode"));
        Assert.Equal("Ammo.RifleAmmo", index.FieldOf("Items.Preset_Gun_Iconic", "ammo"));

        Assert.Equal("LocKey#300", index.LocalizedDescriptionOf("Items.CyberwareBase"));
        Assert.Equal("LocKey#301", index.EffectDescriptionOf("Items.CyberwareBase"));
    }

    [Fact]
    public void StatModifiers_And_WeaponConstants()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("weapons.tweak", """
                package Items
                using RTDB

                BaseGun
                {
                    evolution = "WeaponEvolution.Power";
                    baseReloadTime = 1.35f;
                    statModifierGroups = [ "Items.GunHandlingStats" ];
                }

                GunHandlingStats
                {
                    statModifiers =
                    [
                        {
                            value = 0.25f;
                        } : CycleTimeModifier,
                        {
                            value = 15.f;
                        } : MagazineCapacityModifier,
                        {
                            value = 8.f;
                        } : ProjectilesPerShotModifier
                    ];
                }

                Preset_MyGun : BaseGun
                {
                }

                BaseSword
                {
                    statModifierGroups = [ "Items.SwordStats" ];
                }

                SwordStats
                {
                    statModifiers =
                    [
                        {
                            statType = "BaseStats.MeleeAttackDuration";
                            modifierType = "Additive";
                            value = 0.45f;
                        } : ConstantStatModifier
                    ];
                }
                """)
        ]);

        Assert.Equal("WeaponEvolution.Power", index.FieldOf("Items.Preset_MyGun", "evolution"));
        Assert.Equal("1.35", index.FieldOf("Items.Preset_MyGun", "baseReloadTime"));
        Assert.Equal("0.25", index.StatModifierValueOf("Items.Preset_MyGun", "CycleTime"));
        Assert.Equal("15", index.StatModifierValueOf("Items.Preset_MyGun", "MagazineCapacity"));
        Assert.Equal("8", index.StatModifierValueOf("Items.Preset_MyGun", "ProjectilesPerShot"));

        Assert.Equal("0.45", index.StatModifierValueOf("Items.BaseSword", "MeleeAttackDuration"));
    }

    [Fact]
    public void EffectInfo_And_PlaceholderInterpolation()
    {
        var index = TweakSourceIndex.Parse(
        [
            ("gadgets.tweak", """
                package Items
                using RTDB

                GrenadeFlashBase
                {
                    displayName = "LocKey#5166";
                    localizedDescription = "LocKey#5182";
                }

                GrenadeFlashRegular : GrenadeFlashBase
                {
                    OnEquip =
                    [
                        {
                            UIData =
                            {
                                localizedDescription = "LocKey#34280";
                                intValues =
                                [
                                    3
                                ];
                            };
                        }
                    ];
                }

                GrenadeFlashCommonPlus : GrenadeFlashRegular
                {
                    OnEquip =
                    [
                        {
                            UIData =
                            {
                                localizedDescription = "LocKey#34280";
                                intValues =
                                [
                                    4
                                ];
                            };
                        }
                    ];
                }

                Preset_Satara_Brick
                {
                    slotPartListPreset =
                    [
                        {
                            slot = "AttachmentSlots.IconicWeaponMod";
                            itemPartPreset = "Items.BrickSataraWeaponMod";
                        }
                    ];
                }

                BrickSataraWeaponMod
                {
                    OnAttach =
                    [
                        "Items.BrickSataraWeaponModAbility"
                    ];
                }

                BrickSataraWeaponModAbility
                {
                    UIData =
                    {
                        localizedDescription = "LocKey#84936";
                        floatValues =
                        [
                            66
                        ];
                    };
                }
                """)
        ]);

        var flashInfo = index.EffectInfoOf("Items.GrenadeFlashCommonPlus");
        Assert.NotNull(flashInfo);
        Assert.Equal("LocKey#34280", flashInfo.LocKey);
        Assert.Equal("4", flashInfo.IntValues);

        var formattedFlash = GearCatalog.FormatEffectText("施加失明效果，持续 {int_0} 秒。", flashInfo.IntValues, flashInfo.FloatValues);
        Assert.Equal("施加失明效果，持续 4 秒。", formattedFlash);

        var brickInfo = index.EffectInfoOf("Items.Preset_Satara_Brick");
        Assert.NotNull(brickInfo);
        Assert.Equal("LocKey#84936", brickInfo.LocKey);
        Assert.Equal("66", brickInfo.FloatValues);

        var formattedBrick = GearCatalog.FormatEffectText("充能可以达到 {float_0}%，并且可以在更短的时间内完成充能。", brickInfo.IntValues, brickInfo.FloatValues);
        Assert.Equal("充能可以达到 66%，并且可以在更短的时间内完成充能。", formattedBrick);

        // 测试格式化边界：带空格的 {float_0 }、未提供数值的残留清理
        var formattedKiroshi = GearCatalog.FormatEffectText("高亮 {float_0 } 米内的敌人。持续 {int_0} 秒。未提供: {int_1} {stat_0}。", "5", "8.5");
        Assert.Equal("高亮 8.5 米内的敌人。持续 5 秒。未提供: 。", formattedKiroshi);
    }
}
