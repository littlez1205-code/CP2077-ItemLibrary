using System.Text;
using GameTool.Adapters.Cyberpunk2077.Localization;

namespace GameTool.Tests;

public class LocalizationTests
{
    [Fact]
    public void Fnv_KnownOnscreensPath()
    {
        Assert.Equal(0x2FA6CB0EAEDEB9D3UL, Fnv1a64.HashPath(@"base\localization\zh-cn\onscreens\onscreens.json"));
    }

    [Fact]
    public void Archive_ReadsEveryMatchingOnscreensFile()
    {
        var first = Cr2w((40, "Gameplay-Devices-Computers-Common-NewsFeed", "新闻", null));
        var second = Cr2w((7, "short", new string('测', 3), "男"), (8, "long", new string('长', 200), new string('更', 9000)));
        var archive = RdarArchive.Parse(Archive(first, second));
        var oodle = new MapOodle(new Dictionary<byte, byte[]> { [1] = first, [2] = second });
        var entries = LocalizationCatalog.ReadOnscreens(archive, "zh-cn", oodle);

        Assert.Equal(3, entries.Count);
        Assert.Equal(40UL, entries[0].PrimaryKey);
        Assert.Equal("新闻", entries[0].FemaleVariant);
        Assert.Equal("", entries[0].MaleVariant);
        Assert.Equal("男", entries[1].MaleVariant);
        Assert.Equal(200, entries[2].FemaleVariant.Length);
        Assert.Equal(9000, entries[2].MaleVariant.Length);
        Assert.Single(LocalizationCatalog.Find(entries, "LocKey#40"));
        Assert.Single(LocalizationCatalog.Find(entries, "short"));
    }

    [Fact]
    public void Merge_Ep1ReplacesTheSamePrimaryKey()
    {
        var basis = new LocEntry[]
        {
            new(1, "base-only", "本体甲", ""),
            new(2, "shared", "本体乙", "")
        };
        var expansion = new LocEntry[]
        {
            new(2, "shared", "往日之影乙", ""),
            new(2, "shared", "往日之影乙-后写", ""),
            new(3, "ep1-only", "往日之影丙", "")
        };
        var (entries, overridden) = LocalizationCatalog.Merge(basis, expansion);
        Assert.Equal(1, overridden);
        Assert.Equal(3, entries.Count);
        Assert.Equal("本体甲", entries[0].FemaleVariant);
        Assert.Equal("往日之影乙-后写", entries[1].FemaleVariant);
        Assert.Equal("往日之影丙", entries[2].FemaleVariant);
    }

    [Fact]
    public void Load_Ep1TextArchiveIsOptional()
    {
        // D023：没买往日之影的游戏目录没有 archive\pc\ep1\lang_*_text.archive，名字只用本体文本，Ep1Count 为 0。
        var game = Path.Combine(Path.GetTempPath(), "gt-loc-ep1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Cr2w((1, "base-a", "本体甲", null), (2, "shared", "本体乙", null));
            var second = Cr2w((3, "base-c", "本体丙", null));
            var bytes = Archive(first, second);
            var oodle = new MapOodle(new Dictionary<byte, byte[]> { [1] = first, [2] = second });
            var content = LocalizationCatalog.ArchivePath(game, "zh-cn", "content");
            Directory.CreateDirectory(Path.GetDirectoryName(content)!);
            File.WriteAllBytes(content, bytes);

            var baseOnly = LocalizationCatalog.Load(game, "zh-cn", oodle);
            Assert.Equal(3, baseOnly.BaseCount);
            Assert.Equal(0, baseOnly.Ep1Count);
            Assert.Equal(0, baseOnly.Overridden);
            Assert.Equal(new[] { "本体甲", "本体乙", "本体丙" }, baseOnly.Entries.Select(entry => entry.FemaleVariant));

            // 有 ep1 文本包时照常合并（这里用同一份归档：三条全部被 ep1 覆盖）。
            var expansion = LocalizationCatalog.ArchivePath(game, "zh-cn", "ep1");
            Directory.CreateDirectory(Path.GetDirectoryName(expansion)!);
            File.WriteAllBytes(expansion, bytes);
            var merged = LocalizationCatalog.Load(game, "zh-cn", oodle);
            Assert.Equal(3, merged.BaseCount);
            Assert.Equal(3, merged.Ep1Count);
            Assert.Equal(3, merged.Overridden);
            Assert.Equal(3, merged.Entries.Count);

            // 本体文本包仍是必需的。
            File.Delete(content);
            Assert.Contains("找不到本地化归档", Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(game, "zh-cn", oodle)).Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(game))
                Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void Archive_ConcatenatesSegmentsOfOneFile()
    {
        var whole = Cr2w((1, "joined", "甲", null));
        var split = whole.Length / 2;
        var left = whole[..split];
        var right = whole[split..];
        var archive = RdarArchive.Parse(Archive(left, right, oneFile: true));
        var entries = LocalizationCatalog.ReadOnscreens(archive, "zh-cn", new MapOodle(new Dictionary<byte, byte[]> { [1] = left, [2] = right }));
        Assert.Equal("甲", Assert.Single(entries).FemaleVariant);
    }

    [Fact]
    public void ExportDirectory_RejectsGameAndSaveRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "gt-loc-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var save = Path.Combine(root, "saves");
        var outside = Path.Combine(root, "out");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(save);
        LocalizationCatalog.RequireExportDirectory(outside, game, save);
        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.RequireExportDirectory(Path.Combine(game, "nested"), game, save));
        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.RequireExportDirectory(Path.Combine(save, "nested"), game, save));
        Directory.CreateDirectory(outside);
        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.RequireExportDirectory(outside, game, save));
    }

    [Fact]
    public void Oodle_MissingDllIsExplicit()
    {
        var missing = Path.Combine(Path.GetTempPath(), "gt-missing-oo2ext.dll");
        var error = Assert.Throws<InvalidDataException>(() => OodleNativeDecompressor.Load(missing));
        Assert.Contains("找不到 Oodle 组件", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SteamLibraries_FindTheGameFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "gt-steam-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "lib");
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Cyberpunk 2077"));
        var vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"D:\\Steam"
                }
                "1"
                {
                    "path"		"PLACE"
                }
            }
            """.Replace("PLACE", library.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
        var paths = SteamGameLocator.ReadLibraryPaths(vdf);
        Assert.Equal(@"D:\Steam", paths[0]);
        Assert.Equal(library, paths[1]);
        Assert.Equal(Path.Combine(library, "steamapps", "common", "Cyberpunk 2077"), SteamGameLocator.FindInLibraries([library]));
    }

    private static byte[] Archive(byte[] first, byte[] second, bool oneFile = false)
    {
        var seg0 = Wrap(first, 1);
        var seg1 = Wrap(second, 2);
        const int count = 2;
        var indexOffset = 40 + seg0.Length + seg1.Length;
        var indexSize = 28 + count * 72;
        var bytes = new byte[indexOffset + indexSize];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'A';
        bytes[3] = (byte)'R';
        BitConverter.GetBytes(12).CopyTo(bytes, 4);
        BitConverter.GetBytes((ulong)indexOffset).CopyTo(bytes, 8);
        BitConverter.GetBytes(indexSize).CopyTo(bytes, 16);
        BitConverter.GetBytes((ulong)bytes.Length).CopyTo(bytes, 32);
        seg0.CopyTo(bytes, 40);
        seg1.CopyTo(bytes, 40 + seg0.Length);
        WriteFile(bytes, indexOffset, 0, Fnv1a64.HashPath(@"base\localization\zh-cn\onscreens\onscreens.json"), 0, oneFile ? 2 : 1);
        if (!oneFile)
            WriteFile(bytes, indexOffset, 1, Fnv1a64.HashPath(@"base\localization\zh-cn\onscreens\onscreens_0.json"), 1, 2);
        var table = indexOffset + 8 + count * 56 + 4 + 16;
        WriteSegment(bytes, table, 0, 40, seg0.Length, first.Length);
        WriteSegment(bytes, table, 1, 40 + seg0.Length, seg1.Length, second.Length);
        return bytes;
    }

    private static byte[] Wrap(byte[] payload, byte token)
    {
        var raw = new byte[9];
        raw[0] = (byte)'K';
        raw[1] = (byte)'A';
        raw[2] = (byte)'R';
        raw[3] = (byte)'K';
        BitConverter.GetBytes(payload.Length).CopyTo(raw, 4);
        raw[8] = token;
        return raw;
    }

    private static void WriteFile(byte[] bytes, int indexOffset, int index, ulong hash, int first, int next)
    {
        var at = indexOffset + 8 + index * 56;
        BitConverter.GetBytes(hash).CopyTo(bytes, at + 20);
        BitConverter.GetBytes(first).CopyTo(bytes, at + 40);
        BitConverter.GetBytes(next).CopyTo(bytes, at + 44);
    }

    private static void WriteSegment(byte[] bytes, int table, int index, int offset, int compressed, int uncompressed)
    {
        var at = table + index * 16;
        BitConverter.GetBytes((ulong)offset).CopyTo(bytes, at);
        BitConverter.GetBytes(compressed).CopyTo(bytes, at + 8);
        BitConverter.GetBytes(uncompressed).CopyTo(bytes, at + 12);
    }

    private static byte[] Cr2w(params (ulong Key, string Secondary, string Female, string? Male)[] entries)
    {
        var names = new[] { "", "primaryKey", "Uint64", "secondaryKey", "String", "femaleVariant", "maleVariant" };
        var pool = new List<byte>();
        var offsets = new int[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            offsets[i] = pool.Count;
            pool.AddRange(Encoding.UTF8.GetBytes(names[i]));
            pool.Add(0);
        }

        var header = 40 + 2 * 12;
        var poolAt = header;
        var namesAt = poolAt + pool.Count;
        var bodyAt = namesAt + names.Length * 8;
        var body = new List<byte>();
        foreach (var entry in entries)
        {
            WriteU64(body, 1, 2, entry.Key);
            WriteText(body, 3, 4, entry.Secondary);
            WriteText(body, 5, 4, entry.Female);
            if (entry.Male is not null)
                WriteText(body, 6, 4, entry.Male);
            body.Add(0);
            body.Add(0);
            body.Add(0);
        }

        var file = new byte[bodyAt + body.Count];
        Encoding.ASCII.GetBytes("CR2W").CopyTo(file, 0);
        BitConverter.GetBytes(195).CopyTo(file, 4);
        BitConverter.GetBytes(file.Length).CopyTo(file, 24);
        BitConverter.GetBytes(file.Length).CopyTo(file, 28);
        BitConverter.GetBytes(2).CopyTo(file, 36);
        BitConverter.GetBytes(poolAt).CopyTo(file, 40);
        BitConverter.GetBytes(pool.Count).CopyTo(file, 44);
        BitConverter.GetBytes(namesAt).CopyTo(file, 52);
        BitConverter.GetBytes(names.Length).CopyTo(file, 56);
        pool.CopyTo(file, poolAt);
        for (var i = 0; i < names.Length; i++)
            BitConverter.GetBytes(offsets[i]).CopyTo(file, namesAt + i * 8);
        body.CopyTo(file, bodyAt);
        return file;
    }

    private static void WriteU64(List<byte> body, int name, int type, ulong value)
    {
        body.Add((byte)name);
        body.Add(0);
        body.Add((byte)type);
        body.Add(0);
        body.AddRange(BitConverter.GetBytes(12));
        body.AddRange(BitConverter.GetBytes(value));
    }

    private static void WriteText(List<byte> body, int name, int type, string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var prefix = EncodeLength(text.Length);
        body.Add((byte)name);
        body.Add(0);
        body.Add((byte)type);
        body.Add(0);
        body.AddRange(BitConverter.GetBytes(4 + prefix.Length + text.Length));
        body.AddRange(prefix);
        body.AddRange(text);
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 128)
            return [(byte)(0x80 | length)];
        var bytes = new List<byte> { (byte)(0xC0 | (length & 0x3F)) };
        var rest = length >> 6;
        while (rest > 0x7F)
        {
            bytes.Add((byte)(0x80 | (rest & 0x7F)));
            rest >>= 7;
        }

        bytes.Add((byte)rest);
        return bytes.ToArray();
    }

    private sealed class MapOodle(Dictionary<byte, byte[]> payloads) : IOodleDecompressor
    {
        public byte[] Decompress(ReadOnlySpan<byte> compressed, int uncompressedSize)
        {
            var payload = payloads[compressed[0]];
            Assert.Equal(uncompressedSize, payload.Length);
            return payload;
        }
    }

    [Fact]
    public void Test_Catalog_Effect_Placeholders()
    {
        var game = @"D:\steam\steamapps\common\Cyberpunk 2077";
        if (!Directory.Exists(game)) return;
        var names = GameTool.Adapters.Cyberpunk2077.Names.Cp2077ItemNames.ForGame(game);
        if (!names.Available) return;

        var flash = names.Gear.FirstOrDefault(g => g.RecordName == "Items.GrenadeFlashCommonPlus");
        Assert.NotNull(flash);
        Assert.NotNull(flash.EffectDesc);
        Assert.DoesNotContain("{int_0}", flash.EffectDesc);
        Assert.Contains("4", flash.EffectDesc);

        var brick = names.Gear.FirstOrDefault(g => g.RecordName == "Items.Preset_Satara_Brick");
        Assert.NotNull(brick);
        Assert.NotNull(brick.EffectDesc);
        Assert.DoesNotContain("{float_0}", brick.EffectDesc);
        Assert.Contains("66", brick.EffectDesc);

        // 剧毒单分子线等手臂义体：解析官方阶位异常施加几率（Uncommon = 20%），杜绝显示 naked "% 中毒几率"
        var nanowire = names.Gear.FirstOrDefault(g => g.RecordName == "Items.AdvancedNanoWiresChemicalUncommon");
        Assert.NotNull(nanowire);
        Assert.NotNull(nanowire.EffectDesc);
        Assert.Contains("20% 中毒几率", nanowire.EffectDesc);
        Assert.DoesNotContain("\n% 中毒几率", nanowire.EffectDesc);

        // 武器插件：ShotgunMod1（Common：20% 肢解几率，持续 3 秒；Uncommon：40% 肢解几率，持续 3.5 秒）
        var modCommon = names.Gear.FirstOrDefault(g => g.RecordName == "Items.ShotgunMod1_Common");
        Assert.NotNull(modCommon);
        Assert.NotNull(modCommon.EffectDesc);
        Assert.Contains("20% 肢解几率", modCommon.EffectDesc);
        Assert.Contains("持续 3 秒", modCommon.EffectDesc);

        var modUncommon = names.Gear.FirstOrDefault(g => g.RecordName == "Items.ShotgunMod1_Uncommon");
        Assert.NotNull(modUncommon);
        Assert.NotNull(modUncommon.EffectDesc);
        Assert.Contains("40% 肢解几率", modUncommon.EffectDesc);
        Assert.Contains("持续 3.5 秒", modUncommon.EffectDesc);

        // 义体同调词条：静态目录清理未装备时的动态运行时 "当前总量：+%"
        var healOnKill = names.Gear.FirstOrDefault(g => g.RecordName == "Items.AdvancedHealOnKillRare");
        Assert.NotNull(healOnKill);
        Assert.NotNull(healOnKill.EffectDesc);
        Assert.DoesNotContain("当前总量", healOnKill.EffectDesc);

        // 武器插件 CurveStatModifier（weapon_mods.curveset）+ CombinedStatModifier 解析
        var throwMod1 = names.Gear.FirstOrDefault(g => g.RecordName == "Items.ThrowMod1_Common");
        Assert.NotNull(throwMod1);
        Assert.Contains("20% 的几率立即回收武器", throwMod1.EffectDesc);

        var shotgunMod3 = names.Gear.FirstOrDefault(g => g.RecordName == "Items.ShotgunMod3_Common");
        Assert.NotNull(shotgunMod3);
        Assert.Contains("+15% 弹丸散射收束", shotgunMod3.EffectDesc);
        Assert.Contains("-20% 每颗弹丸伤害", shotgunMod3.EffectDesc);

        // 武器插件内联 UIData 模板继承（PowerMod1UIData / HGMod1UIData）与多源 CombinedStatModifier 累加
        var powerMod1 = names.Gear.FirstOrDefault(g => g.RecordName == "Items.PowerMod1_Common");
        Assert.NotNull(powerMod1);
        Assert.Contains("15% 转化为热能伤害", powerMod1.EffectDesc);
        Assert.Contains("3% 的几率施加燃烧效果", powerMod1.EffectDesc);

        var hgMod1 = names.Gear.FirstOrDefault(g => g.RecordName == "Items.HGMod1_Common");
        Assert.NotNull(hgMod1);
        Assert.Contains("-4% 子弹散射，持续 5 秒。可以叠加 2 次。", hgMod1.EffectDesc);

        // 精英/首领图标标签 <Image id="UIIcon.q000_tutorial_icon_boss" ...> 转换为文字
        var rangedMod3 = names.Gear.FirstOrDefault(g => g.RecordName == "Items.RangedMod3_Common");
        Assert.NotNull(rangedMod3);
        Assert.Contains("对骷髅头敌人 +4% 伤害", rangedMod3.EffectDesc);

        // 全库检查：EffectDesc 与 Description 中绝不包含未解析的代码占位符或裸露百分号
        var brokenEffect = names.Gear.Where(g => g.EffectDesc?.Contains("{int_") == true
            || g.EffectDesc?.Contains("{float_") == true
            || g.EffectDesc?.Contains("{stat_") == true).ToList();
        Assert.Empty(brokenEffect);

        var brokenDesc = names.Gear.Where(g => g.Description?.Contains("{int_") == true
            || g.Description?.Contains("{float_") == true
            || g.Description?.Contains("{stat_") == true).ToList();
        Assert.Empty(brokenDesc);

        var danglingPercent = names.Gear
            .Where(g => g.EffectDesc is not null && System.Text.RegularExpressions.Regex.IsMatch(g.EffectDesc, @"(?<!\d)%"))
            .ToList();
        Assert.Empty(danglingPercent);
    }
}
