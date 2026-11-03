using System.Text.Json;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>物品库（T019）：堆叠物品目录、品质与标签继承、LibraryService 的获得（不做备份）、Bridge library.obtain。</summary>
public sealed class LibraryTests : IDisposable
{
    private const string Tweaks = """
        package RTDB
        BaseObject
        {
        }

        Item : BaseObject
        {
        	quality = "Quality.Common";
        	itemType = "ItemType.Gen_Misc";
        }

        ConsumableItem : Item
        {
        	tags =
        	[
        		"Preload", "Consumable"
        	];
        	itemType = "";
        }

        Clothing : Item
        {
        	fk< Quality > quality = "Quality.Random";
        	itemType = "ItemType.Clo_OuterChest";
        	CName[] tags = [ "Clothing" ];
        }

        WeaponItem : Item
        {
        	quality = "Quality.Random";
        	itemType = "ItemType.Wea_Handgun";
        }

        package Items
        using RTDB
        money : Item
        {
        	displayName = "LocKey#10";
        	CName[] tags = [ "Currency" ];
        }

        LegendaryMaterial1 : Item
        {
        	displayName = "LocKey#11";
        	quality = "Quality.Legendary";
        	itemType = "ItemType.Gen_CraftingMaterial";
        }

        QuickHackRareMaterial1 : LegendaryMaterial1
        {
        	displayName = "LocKey#12";
        	quality = "Quality.RarePlus";
        	tags += [ "QuickhackCraftingMaterials" ];
        }

        FoodBase : ConsumableItem
        {
        	itemType = "ItemType.Con_Edible";
        	tags +=
        	[
        		"Food"
        	];
        }

        GoodQualityFood1 : FoodBase
        {
        	displayName = "LocKey#13";
        }

        GoodQualityDrink1 : FoodBase
        {
        	displayName = "LocKey#14";
        	tags = [ "Consumable", "Drink" ];
        }

        [ notQueryable ]
        HiddenDrink : FoodBase
        {
        	displayName = "LocKey#14";
        }

        quest_note : ConsumableItem
        {
        	displayName = "LocKey#15";
        	tags = [ "Quest" ];
        }

        Implant : Item
        {
        	displayName = "LocKey#16";
        	tags = [ "Cyberware" ];
        }

        Jacket_01_basic_01 : Clothing
        {
        	displayName = "LocKey#1";
        }

        Jacket_Twin : Clothing
        {
        	displayName = "LocKey#1";
        }

        Preset_Lexington_Default : WeaponItem
        {
        	displayName = "LocKey#2";
        	slotPartListPreset =
        	[
        		{ slot = "AttachmentSlots.Receiver"; itemPartPreset = "Items.w_handgun__lexington__rcv1"; }
        	];
        }

        Preset_Lexington_Wilson : Preset_Lexington_Default
        {
        	statModifiers +=
        	[
        		"Quality.IconicItem"
        	];
        }
        """;

    private static readonly LocEntry[] Texts =
    [
        new(1, "", "", "夹克"),
        new(2, "", "", "列克星敦"),
        new(10, "", "", "欧元"),
        new(11, "", "", "5 阶物品组件"),
        new(12, "", "", "3 阶快速破解组件"),
        new(13, "", "", "汉堡"),
        new(14, "", "", "保健水"),
        new(15, "", "", "便条"),
        new(16, "", "", "义体")
    ];

    private const string Food = "Items.GoodQualityFood1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-library-" + Guid.NewGuid().ToString("N"));
    private readonly TweakSourceIndex _index;
    private readonly Cp2077ItemNames _names;
    private readonly Cp2077Adapter _adapter;

    public LibraryTests()
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
    public void StackableRule_FollowsTypeChainAndTags()
    {
        Assert.True(GearCatalog.IsStackableRecord(_index, "Items.money"));
        Assert.True(GearCatalog.IsStackableRecord(_index, Food));
        Assert.True(GearCatalog.IsStackableRecord(_index, "Items.LegendaryMaterial1"));
        // ConsumableItem 把 itemType 置空：直接继承它的任务物品也是堆叠。
        Assert.True(GearCatalog.IsStackableRecord(_index, "Items.quest_note"));
        Assert.False(GearCatalog.IsStackableRecord(_index, "Items.Implant"));
        Assert.False(GearCatalog.IsStackableRecord(_index, "Items.Jacket_01_basic_01"));
        Assert.False(GearCatalog.IsStackableRecord(_index, "Items.Preset_Lexington_Default"));
        Assert.False(GearCatalog.IsStackableRecord(_index, "Items.Missing"));

        var stackables = _names.Stackables;
        Assert.Equal(
            new[] { "Items.GoodQualityDrink1", "Items.GoodQualityFood1" },
            stackables.Where(entry => entry.Kind == GearKinds.Consumable).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "Items.LegendaryMaterial1", "Items.QuickHackRareMaterial1" },
            stackables.Where(entry => entry.Kind == GearKinds.Material).Select(entry => entry.RecordName).Order(StringComparer.Ordinal));
        Assert.Equal("Items.money", Assert.Single(stackables, entry => entry.Kind == GearKinds.Currency).RecordName);
        Assert.All(stackables, entry => Assert.True(entry.Stackable));
        // 排除：notQueryable、Quest 标签、不属于任何堆叠分类的义体。
        Assert.DoesNotContain(stackables, entry => entry.RecordName is "Items.HiddenDrink" or "Items.quest_note" or "Items.Implant");
        Assert.All(_names.Gear, entry => Assert.False(entry.Stackable));
        Assert.Null(_names.KindOf(TweakDbId.FromName(Food)));
    }

    [Fact]
    public void QualityTagsAndIconic_AreInherited()
    {
        Assert.Equal("Quality.Common", _index.FieldOf(Food, "quality"));
        Assert.Equal("ItemType.Con_Edible", _index.FieldOf(Food, "itemType"));
        Assert.Equal("", _index.FieldOf("Items.quest_note", "itemType"));
        Assert.Equal("Quality.Random", _index.FieldOf("Items.Jacket_01_basic_01", "quality"));
        Assert.Null(_index.FieldOf("Items.Missing", "quality"));
        Assert.Equal(new[] { "Preload", "Consumable", "Food" }, _index.TagsOf(Food));
        Assert.Equal(new[] { "Consumable", "Drink" }, _index.TagsOf("Items.GoodQualityDrink1"));
        Assert.Equal(new[] { "QuickhackCraftingMaterials" }, _index.TagsOf("Items.QuickHackRareMaterial1"));
        Assert.True(_index.MentionsIconicItem("Items.Preset_Lexington_Wilson"));
        Assert.False(_index.MentionsIconicItem("Items.Preset_Lexington_Default"));

        Assert.Equal(GearQuality.Rare, GearQuality.Normalize("Quality.RarePlus"));
        Assert.Equal(GearQuality.Legendary, GearQuality.Normalize("Quality.LegendaryPlusPlus"));
        Assert.Null(GearQuality.Normalize("Quality.Random"));
        Assert.Null(GearQuality.Normalize(null));

        var all = _names.Gear.Concat(_names.Stackables).ToDictionary(entry => entry.RecordName);
        Assert.Equal(GearQuality.Common, all[Food].Quality);
        Assert.Equal("食物", all[Food].TypeLabel);
        Assert.Equal("饮料", all["Items.GoodQualityDrink1"].TypeLabel);
        Assert.Equal(GearQuality.Legendary, all["Items.LegendaryMaterial1"].Quality);
        Assert.Equal("制作组件", all["Items.LegendaryMaterial1"].TypeLabel);
        Assert.Equal(GearQuality.Rare, all["Items.QuickHackRareMaterial1"].Quality);
        Assert.Equal("快速破解组件", all["Items.QuickHackRareMaterial1"].TypeLabel);
        Assert.Null(all["Items.Jacket_01_basic_01"].Quality);
        Assert.Equal("外衣", all["Items.Jacket_01_basic_01"].TypeLabel);
        Assert.True(all["Items.Preset_Lexington_Wilson"].Iconic);
        Assert.False(all["Items.Preset_Lexington_Default"].Iconic);
        Assert.Equal("手枪", all["Items.Preset_Lexington_Default"].TypeLabel);
    }

    [Fact]
    public void Catalog_ListsStackableKinds_HeldQuantities_AndQualitySearch()
    {
        var path = Save("catalog");
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(
            new[] { "clothing", "ranged", "consumable", "material", "currency" },
            document.GenerateKinds.Select(kind => kind.Id));
        Assert.All(document.GenerateKinds.Skip(2), kind => Assert.True(kind.Stackable));

        var money = Assert.Single(document.ListGeneratable("currency", null, 0, 50).Items);
        Assert.True(money.Stackable);
        Assert.True(money.Owned);
        Assert.Equal(100, money.Held);
        Assert.Equal(2147483647, money.MaxQuantity);
        var food = Assert.Single(document.ListGeneratable("consumable", "汉堡", 0, 50).Items);
        Assert.False(food.Owned);
        Assert.Null(food.Held);
        Assert.Equal(Cp2077SaveDocument.DefaultStackMax, food.MaxQuantity);

        // 「传说组件」拆成品质词 + 名字：命中 5 阶物品组件。
        Assert.Equal(new[] { "传说", "组件" }, Cp2077SaveDocument.SearchWords("传说组件"));
        Assert.Equal("Items.LegendaryMaterial1", Assert.Single(document.ListGeneratable(null, "传说组件", 0, 50).Items).Target);
        Assert.Equal(2, document.ListGeneratable(null, "组件", 0, 50).Total);
        Assert.Single(document.ListGeneratable(null, "标志性", 0, 50).Items);

        Assert.Equal("money", document.StackResource("Items.money")!.Id.Value);
        var synthetic = document.StackResource(Food)!;
        Assert.Equal("tdb:" + Food, synthetic.Locator);
        Assert.Equal(Cp2077SaveDocument.DefaultStackMax, synthetic.Max);
        Assert.Null(document.StackResource("Items.Jacket_Twin"));
        Assert.Null(document.FindGeneratable("Items.HiddenDrink"));
        Assert.True(document.FindGeneratable("Items.Jacket_01_basic_01")!.Owned);
    }

    [Fact]
    public void WithResources_RejectsDuplicatesAndNonTweakLocators()
    {
        var extra = new ResourceDescriptor(new ResourceId("tdb:" + Food), "汉堡", Food, "consumable", 99999, null, "tdb:" + Food);
        var extended = _adapter.WithResources([extra]);
        Assert.Equal(_adapter.Resources.Count + 1, extended.Resources.Count);
        Assert.Equal(_adapter.Resources.Count, new Cp2077Adapter(() => _names).Resources.Count);
        Assert.Throws<InvalidDataException>(() => _adapter.WithResources([extra with { Id = new ResourceId("money") }]));
        Assert.Throws<InvalidDataException>(() => _adapter.WithResources([extra with { Locator = "tdb:Items.money" }]));
        Assert.Throws<InvalidDataException>(() => _adapter.WithResources([extra with { Locator = "Items.X" }]));
    }

    [Fact]
    public async Task Obtain_GearAndStacks_NeverCreateBackups()
    {
        var saves = Path.Combine(_root, "saves");
        var path = Save("saves", "ManualSave-0");
        var other = Save("saves", "ManualSave-1");
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);
        var count = ((Cp2077SaveDocument)_adapter.Load(path)).PlayerItemCount;
        // 手动备份一份（加入备份区），之后的获得不再产生任何备份（D019）。
        var manual = backups.Add(_adapter, Path.GetDirectoryName(path)!);

        // 已持有：原数量 + 输入值。
        var money = library.Obtain(_adapter, path, "Items.money", 50);
        Assert.True(money.Stackable);
        Assert.Equal(100, money.Before);
        Assert.Equal(150, money.After);
        Assert.False(money.Inserted);
        Assert.Equal(manual.Id, Assert.Single(backups.List(_adapter.GameId)).Id);
        Assert.Equal(100, backups.Describe(_adapter, manual.Id, saves).Resources.Single(item => item.Id == "money").BackupQuantity);
        Assert.Equal(150, _adapter.Load(path).GetQuantity(new ResourceId("money")));

        // 未持有：插入新堆叠（目标不在内置资源表里，走临时资源描述）。
        var inserted = library.Obtain(_adapter, path, Food, 3);
        Assert.Null(inserted.Before);
        Assert.Equal(3, inserted.After);
        Assert.True(inserted.Inserted);
        Assert.Equal(3, Held(path, Food));

        var added = library.Obtain(_adapter, path, Food, 2);
        Assert.Equal(3, added.Before);
        Assert.Equal(5, added.After);
        Assert.False(added.Inserted);
        Assert.Equal(5, Held(path, Food));

        // 装备：走生成（1 件），模板是背包里的夹克。
        var gear = library.Obtain(_adapter, path, "Items.Jacket_Twin", null);
        Assert.False(gear.Stackable);
        Assert.Equal(1, gear.Quantity);
        Assert.Equal("夹克", gear.TemplateLabel);
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(count + 2, after.PlayerItemCount);
        Assert.Equal(150, after.GetQuantity(new ResourceId("money")));
        Assert.Single(backups.List(_adapter.GameId));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.bak"));

        // 另一个存档、新的实例：同样不备份，备份区只有手动那一份。
        Assert.Equal(7, library.Obtain(_adapter, other, "Items.LegendaryMaterial1", 7).After);
        Assert.Equal(6, new LibraryService(backups, enable).Obtain(_adapter, path, Food, 1).After);
        Assert.Equal(manual.Id, Assert.Single(backups.List(_adapter.GameId)).Id);
        Assert.False(Directory.Exists(Path.Combine(_root, "backups", _adapter.GameId, "ManualSave-1")));
    }

    [Fact]
    public async Task Obtain_ToStash_PutsGearInStash_AndRejectsStackable()
    {
        var saves = Path.Combine(_root, "stash-saves");
        var path = GearGenerateTests.WriteSaveAt(
            saves,
            "ManualSave-0",
            (1UL, GearGenerateTests.Clothing("Items.Jacket_01_basic_01", generic: true)),
            (ItemInserter.StashBagId, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var backups = new BackupStore(Path.Combine(_root, "stash-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var docBefore = (Cp2077SaveDocument)_adapter.Load(path);
        var initialPlayerCount = docBefore.PlayerItemCount;
        var initialStashCount = docBefore.StashItemCount;
        Assert.Equal(1, initialStashCount);

        // 堆叠物品不支持存入储藏箱
        var stackRejected = Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, "Items.money", 10, toStash: true));
        Assert.Contains("堆叠物品不支持直接存入储藏箱", stackRejected.Message);

        // 装备存入储藏箱
        var report = library.Obtain(_adapter, path, "Items.Jacket_Twin", null, toStash: true);
        Assert.True(report.ToStash);
        Assert.False(report.Stackable);
        Assert.Equal(1, report.Quantity);

        var docAfter = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(initialPlayerCount, docAfter.PlayerItemCount);
        Assert.Equal(initialStashCount + 1, docAfter.StashItemCount);
    }

    [Fact]
    public async Task Obtain_Rejections_LeaveTheSaveUnchanged()
    {
        var path = Save("saves", "ManualSave-0");
        var original = File.ReadAllBytes(path);
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        Assert.Contains("1 件", Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, "Items.Jacket_Twin", 2)).Message);
        Assert.Contains("上限", Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, "Items.money", 2147483647)).Message);
        Assert.Contains("不能超过", Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, Food, 100000)).Message);
        Assert.Contains("至少为 1", Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, Food, 0)).Message);
        Assert.Contains("没有", Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, "Items.HiddenDrink", 1)).Message);
        Assert.Throws<PatchRejectedException>(() => library.Obtain(_adapter, path, " ", 1));
        // 背包里没有任何装备（只有欧元）：远程武器连前缀来源都没有，生成拒绝，存档不变，也不产生备份。
        var bare = GearGenerateTests.WriteSaveAt(Path.Combine(_root, "saves"), "ManualSave-1");
        var bareOriginal = File.ReadAllBytes(bare);
        Assert.Contains("能解析的装备", Assert.Throws<InvalidDataException>(() => library.Obtain(_adapter, bare, "Items.Preset_Lexington_Default", 1)).Message);
        Assert.Equal(bareOriginal, File.ReadAllBytes(bare));
        Assert.False(File.Exists(bare + ".tmp"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(backups.List(_adapter.GameId));

        var unnamed = new Cp2077Adapter(() => Cp2077ItemNames.FromCatalog(null));
        Assert.Contains("REDmod", Assert.Throws<PatchRejectedException>(() => library.Obtain(unnamed, path, Food, 1)).Message);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Obtain_RangedWithOnlyClothing_BorrowsTheJacketPrefix()
    {
        // T030：存档里一把枪都没有，物品库「获得」莱克星顿仍能生成（夹克作前缀来源），通过严格校验，不产生备份。
        var path = Save("saves", "ManualSave-0");
        var count = ((Cp2077SaveDocument)_adapter.Load(path)).PlayerItemCount;
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var gun = library.Obtain(_adapter, path, "Items.Preset_Lexington_Default", null);
        Assert.False(gun.Stackable);
        Assert.Equal(1, gun.Quantity);
        Assert.Equal("夹克", gun.TemplateLabel);
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(count + 1, after.PlayerItemCount);
        Assert.True(after.FindGeneratable("Items.Preset_Lexington_Default")!.Owned);
        Assert.Empty(backups.List(_adapter.GameId));
    }

    [Fact]
    public async Task Obtain_ModWithMultipleCopies_GeneratesMultiple()
    {
        var modTweaks = Tweaks + """

            WeaponMod1 : Item
            {
            	displayName = "LocKey#17";
            	tags = [ "itemPart" ];
            	itemType = "ItemType.Prt_Mod";
            	placementSlots = [ "AttachmentSlots.GenericWeaponMod" ];
            }
            """;
        var texts = Texts.Append(new LocEntry(17, "", "", "武器插件")).ToArray();
        var index = TweakSourceIndex.Parse([("mod.tweak", modTweaks)]);
        var names = Cp2077ItemNames.FromCatalog(ItemNameCatalog.FromParts(index, texts));
        var adapter = new Cp2077Adapter(() => names);

        var path = Save("saves", "ManualSave-0");
        var count = ((Cp2077SaveDocument)adapter.Load(path)).PlayerItemCount;
        var backups = new BackupStore(Path.Combine(_root, "backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var report = library.Obtain(adapter, path, "Items.WeaponMod1", 3);
        Assert.False(report.Stackable);
        Assert.Equal(3, report.Quantity);
        var after = (Cp2077SaveDocument)adapter.Load(path);
        Assert.Equal(count + 3, after.PlayerItemCount);
    }

    [Fact]
    public async Task Bridge_LibraryObtain()
    {
        var saves = Path.Combine(_root, "bridge-saves");
        var path = Save("bridge-saves", "ManualSave-0");
        var registry = new AdapterRegistry();
        registry.Register(_adapter);
        await using var bridge = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "bridge-backups"), Path.Combine(_root, "bridge-skins"));
        var slot = JsonSerializer.Serialize(path);

        using var first = Call(bridge, "\"target\":\"" + Food + "\",\"quantity\":3", slot);
        Assert.Equal("ok", first.RootElement.GetProperty("status").GetString());
        var data = first.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("stackable").GetBoolean());
        Assert.True(data.GetProperty("inserted").GetBoolean());
        Assert.Equal(3, data.GetProperty("after").GetInt64());
        Assert.Equal("汉堡", data.GetProperty("label").GetString());
        Assert.False(data.TryGetProperty("before", out _));
        Assert.False(data.TryGetProperty("backupId", out _));

        using var second = Call(bridge, "\"target\":\"" + Food + "\"", slot);
        Assert.Equal(4, second.RootElement.GetProperty("data").GetProperty("after").GetInt64());
        Assert.False(second.RootElement.GetProperty("data").TryGetProperty("backupId", out _));

        using var catalog = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"c\",\"method\":\"generate.catalog\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slot + ",\"kind\":\"consumable\",\"query\":\"汉堡\"}}"));
        var item = catalog.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(4, item.GetProperty("held").GetInt64());
        Assert.Equal("Common", item.GetProperty("quality").GetString());
        Assert.Equal("食物", item.GetProperty("typeLabel").GetString());

        using var gear = Call(bridge, "\"target\":\"Items.Jacket_Twin\"", slot);
        Assert.Equal("ok", gear.RootElement.GetProperty("status").GetString());
        Assert.False(gear.RootElement.GetProperty("data").GetProperty("stackable").GetBoolean());

        using var badQuantity = Call(bridge, "\"target\":\"" + Food + "\",\"quantity\":\"3\"", slot);
        Assert.Equal("bad_args", badQuantity.RootElement.GetProperty("error").GetProperty("code").GetString());
        using var missing = Call(bridge, "\"quantity\":1", slot);
        Assert.Equal("bad_args", missing.RootElement.GetProperty("error").GetProperty("code").GetString());
        using var tooMany = Call(bridge, "\"target\":\"Items.money\",\"quantity\":2147483647", slot);
        Assert.Equal("failed", tooMany.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(Path.Combine(_root, "bridge-backups", "cp2077")));
        using var listed = JsonDocument.Parse(bridge.Dispatch("{\"id\":\"b\",\"method\":\"backup.list\",\"params\":{\"gameId\":\"cp2077\"}}"));
        Assert.Equal(0, listed.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public void Groups_OwnershipFilter_DistinguishesUnownedAndOwned()
    {
        var path = Save("ownership-saves");
        var doc = (Cp2077SaveDocument)_adapter.Load(path);

        // 全部
        var allPage = doc.ListGeneratableGroups("clothing", null, 0, 50, "all");
        // 未拥有
        var unownedPage = doc.ListGeneratableGroups("clothing", null, 0, 50, "unowned");
        // 已拥有
        var ownedPage = doc.ListGeneratableGroups("clothing", null, 0, 50, "owned");

        Assert.Equal(allPage.Total, unownedPage.Total + ownedPage.Total);
        Assert.True(ownedPage.Total >= 1); // 初始背包里有 Jacket_01_basic_01

        var counts = doc.CountGeneratableGroups(null, "unowned");
        Assert.Equal(unownedPage.Total, counts["clothing"]);
    }

    [Fact]
    public async Task BatchObtain_MultipleItems_AndBridgeDispatch_Succeeds()
    {
        var saves = Path.Combine(_root, "batch-saves");
        var path = Save("batch-saves", "ManualSave-1");
        var registry = new AdapterRegistry();
        registry.Register(_adapter);
        await using var bridge = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "batch-backups"), Path.Combine(_root, "batch-skins"));
        var slot = JsonSerializer.Serialize(path);

        var batchPayload = "{\"id\":\"b1\",\"method\":\"library.batchObtain\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slot + ",\"destinationMode\":\"smart\",\"items\":[{\"target\":\"Items.Jacket_Twin\"},{\"target\":\"" + Food + "\",\"quantity\":5}]}}";
        using var resp = JsonDocument.Parse(bridge.Dispatch(batchPayload));
        Assert.Equal("ok", resp.RootElement.GetProperty("status").GetString());
        var data = resp.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalRequested").GetInt32());
        Assert.Equal(2, data.GetProperty("succeeded").GetInt32());
        Assert.Equal(0, data.GetProperty("failed").GetInt32());

        // 验证写回后食物数量是 5
        var doc = (Cp2077SaveDocument)_adapter.Load(path);
        var food = doc.FindGeneratable(Food);
        Assert.Equal(5, food!.Held);

        // 验证 Jacket_Twin 现在是已拥有
        var ownedClothing = doc.ListGeneratableGroups("clothing", "Twin", 0, 10, "owned");
        Assert.True(ownedClothing.Total >= 1);
    }

    [Fact]
    public async Task BatchObtain_Smart_WithoutStash_PutsIconicInBackpack_InsteadOfFailing()
    {
        // 存档里还没有储藏箱记录：智能模式下标志性武器退回背包，不再整件失败。
        var path = Save("batch-nostash", "ManualSave-0");
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(0, before.StashItemCount);
        var backups = new BackupStore(Path.Combine(_root, "batch-nostash-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var report = library.BatchObtain(
            _adapter,
            path,
            [new BatchItemRequest("Items.Preset_Lexington_Wilson"), new BatchItemRequest("Items.Jacket_Twin")]);

        Assert.Equal(2, report.Succeeded);
        Assert.All(report.Results, result => Assert.False(result.Report!.ToStash));
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(before.PlayerItemCount + 2, after.PlayerItemCount);
        Assert.Equal(0, after.StashItemCount);
        Assert.False(GenerateService.Groups(_adapter, path, null, null, 0, 10, kindFallback: true).StashAvailable);
    }

    [Fact]
    public async Task BatchObtain_Smart_WithStash_IconicToStash_OthersToBackpack()
    {
        var path = GearGenerateTests.WriteSaveAt(
            Path.Combine(_root, "batch-stash"),
            "ManualSave-0",
            (1UL, GearGenerateTests.Clothing("Items.Jacket_01_basic_01", generic: true)),
            (ItemInserter.StashBagId, GearGenerateTests.Clothing("Items.SQ031_Samurai_Jacket", generic: true)));
        var before = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(1, before.StashItemCount);
        Assert.True(GenerateService.Groups(_adapter, path, null, null, 0, 10, kindFallback: true).StashAvailable);
        var backups = new BackupStore(Path.Combine(_root, "batch-stash-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var report = library.BatchObtain(
            _adapter,
            path,
            [
                new BatchItemRequest("Items.Preset_Lexington_Wilson"),
                new BatchItemRequest("Items.Jacket_Twin"),
                new BatchItemRequest(Food, 4)
            ],
            "smart");

        Assert.Equal(3, report.Succeeded);
        Assert.True(report.Results[0].Report!.ToStash);
        Assert.False(report.Results[1].Report!.ToStash);
        Assert.False(report.Results[2].Report!.ToStash);
        var after = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.Equal(before.StashItemCount + 1, after.StashItemCount);
        Assert.Equal(4, after.FindGeneratable(Food)!.Held);
    }

    [Fact]
    public async Task BatchObtain_StashModeWithoutStash_AndBadDestination_RejectBeforeWriting()
    {
        var path = Save("batch-reject", "ManualSave-0");
        var original = File.ReadAllBytes(path);
        var backups = new BackupStore(Path.Combine(_root, "batch-reject-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);
        BatchItemRequest[] items = [new("Items.Jacket_Twin"), new(Food, 2)];

        var noStash = Assert.Throws<PatchRejectedException>(() => library.BatchObtain(_adapter, path, items, "stash"));
        Assert.Contains("储藏室记录", noStash.Message);
        Assert.Throws<PatchRejectedException>(() => library.BatchObtain(_adapter, path, items, "trunk"));
        Assert.Equal(original, File.ReadAllBytes(path));

        // 只有堆叠物品时，「全部放入储藏室」照旧把它们放进背包，不因为没有储藏箱而拒绝。
        var stacks = library.BatchObtain(_adapter, path, [new(Food, 2)], "stash");
        Assert.Equal(1, stacks.Succeeded);
        Assert.False(stacks.Results[0].Report!.ToStash);
    }

    [Fact]
    public async Task BatchObtain_Failures_CarryTheCatalogLabel_AndBridgeRejectsBadQuantity()
    {
        var saves = Path.Combine(_root, "batch-fail-saves");
        var path = Save("batch-fail-saves", "ManualSave-1");
        var backups = new BackupStore(Path.Combine(_root, "batch-fail-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);

        var report = library.BatchObtain(
            _adapter,
            path,
            [new BatchItemRequest(Food, Cp2077SaveDocument.DefaultStackMax + 1), new BatchItemRequest("Items.Jacket_Twin")]);
        Assert.Equal(1, report.Succeeded);
        Assert.Equal(1, report.Failed);
        var failure = report.Results[0];
        Assert.False(failure.Success);
        Assert.Equal("汉堡", failure.Label);
        Assert.Contains("数量不能超过", failure.Error);

        var registry = new AdapterRegistry();
        registry.Register(_adapter);
        await using var bridge = new BridgeDispatcher(registry, saves, _ => { }, Path.Combine(_root, "batch-fail-bridge-backups"), Path.Combine(_root, "batch-fail-skins"));
        var slot = JsonSerializer.Serialize(path);
        var before = File.ReadAllBytes(path);
        using var bad = JsonDocument.Parse(bridge.Dispatch(
            "{\"id\":\"b2\",\"method\":\"library.batchObtain\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slot + ",\"items\":[{\"target\":\"" + Food + "\",\"quantity\":\"lots\"}]}}"));
        Assert.Equal("bad_args", bad.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Catalog_AfterEveryWrite_ShowsTheNewState()
    {
        // 目录查询复用解析好的存档（CatalogDocumentCache）：每次写入（插入新堆叠、原地改数量，文件长度可能不变、
        // 两次写入可能落在同一个文件时间刻度里）以及外部改写文件之后，下一次查询都必须看到新状态。
        var path = Save("catalog-cache", "ManualSave-0");
        var backups = new BackupStore(Path.Combine(_root, "catalog-cache-backups"));
        await using var enable = new EnableService(backups, _ => { });
        var library = new LibraryService(backups, enable);
        long? HeldInCatalog() =>
            GenerateService.Groups(_adapter, path, GearKinds.Consumable, "汉堡", 0, 10).Groups.Single().Variants.Single().Held;

        Assert.Null(HeldInCatalog());
        Assert.Null(HeldInCatalog());
        library.Obtain(_adapter, path, Food, 3);
        Assert.Equal(3, HeldInCatalog());
        for (var round = 1; round <= 5; round++)
        {
            library.Obtain(_adapter, path, Food, 1);
            Assert.Equal(3 + round, HeldInCatalog());
        }

        // 两次写入落在同一个文件时间刻度、文件长度也没变（原地改数量）：把修改时间改回写入前，
        // 这时只有本进程的写入计数（SaveFileWrites）能看出存档变了。
        var stamp = File.GetLastWriteTimeUtc(path);
        var length = new FileInfo(path).Length;
        Assert.Equal(8, HeldInCatalog());
        library.Obtain(_adapter, path, Food, 1);
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal(length, new FileInfo(path).Length);
        Assert.Equal(9, HeldInCatalog());

        // 两件夹克同名，合并在同一张卡里：看那一条的「已拥有」。
        bool TwinOwned() =>
            GenerateService.Groups(_adapter, path, GearKinds.Clothing, null, 0, 10).Groups
                .SelectMany(group => group.Variants).Single(item => item.Target == "Items.Jacket_Twin").Owned;
        Assert.False(TwinOwned());
        library.Obtain(_adapter, path, "Items.Jacket_Twin", null);
        Assert.True(TwinOwned());

        // 外部（比如游戏）把同一个文件换成另一份存档。
        File.Copy(Save("catalog-cache-other", "ManualSave-0"), path, overwrite: true);
        Assert.Null(HeldInCatalog());
    }

    [Fact]
    public void Document_DerivedState_FollowsGenerateAndQuantityEdits()
    {
        // 文档内缓存的「已拥有装备」「持有的堆叠」在生成和原地改数量之后都要跟着变。
        var path = Save("derived", "ManualSave-0");
        var document = (Cp2077SaveDocument)_adapter.Load(path);
        Assert.False(document.FindGeneratable("Items.Jacket_Twin")!.Owned);
        Assert.Equal(1, document.ListGeneratableGroups(GearKinds.Currency, null, 0, 10, "owned").Total);

        document.Generate(new GenerateRequest("Items.Jacket_Twin", 1));
        Assert.True(document.FindGeneratable("Items.Jacket_Twin")!.Owned);

        var results = document.Apply([new ResourceChange(new ResourceId("money"), 0)]);
        Assert.Equal(ChangeOutcome.Changed, Assert.Single(results).Outcome);
        Assert.Equal(0, document.ListGeneratableGroups(GearKinds.Currency, null, 0, 10, "owned").Total);
        Assert.Equal(1, document.ListGeneratableGroups(GearKinds.Currency, null, 0, 10, "unowned").Total);
    }

    private static JsonDocument Call(BridgeDispatcher bridge, string fields, string slot) =>
        JsonDocument.Parse(bridge.Dispatch("{\"id\":\"o\",\"method\":\"library.obtain\",\"params\":{\"gameId\":\"cp2077\",\"slot\":" + slot + "," + fields + "}}"));

    private long? Held(string path, string target) =>
        ((Cp2077SaveDocument)_adapter.Load(path)).FindGeneratable(target)!.Held;

    /// <summary>玩家背包：100 欧元 + 一件夹克（服装模板）。</summary>
    private string Save(string folder, string slot = "slot") =>
        GearGenerateTests.WriteSaveAt(Path.Combine(_root, folder), slot, (1, GearGenerateTests.Clothing("Items.Jacket_01_basic_01", generic: true)));
}
