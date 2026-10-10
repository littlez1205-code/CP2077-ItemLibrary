using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GameTool.Core;

namespace GameTool.App;

public sealed class ResearchRejectedException : Exception
{
    public ResearchRejectedException(string message) : base(message)
    {
    }
}

public sealed record FindHit(int Offset, string? Region, int? RegionIndex, int Relative);

public sealed record ResearchFindResult(int Total, bool Truncated, IReadOnlyList<FindHit> Hits);

public sealed record DiffSummaryRow(
    string Kind,
    string Name,
    int? LeftIndex,
    int? RightIndex,
    int? LeftLength,
    int? RightLength,
    int ChangedBytes,
    int SpanCount);

public sealed record DiffSummary(
    IReadOnlyList<DiffSummaryRow> Changes,
    IReadOnlyList<RegionChange> Added,
    IReadOnlyList<RegionChange> Removed,
    int IndexShiftOnly,
    int ChangedCount,
    int AddedCount,
    int RemovedCount);

public enum ResearchNeedleKind
{
    U32,
    Tdb,
    Hex,
    Ascii
}

public sealed record ResearchNeedle(ResearchNeedleKind Kind, string Text);

public static class ResearchService
{
    public const int MaxHits = 200;
    private const int MaxShownBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ResearchImage Open(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (adapter is IResearchProvider provider)
            return provider.OpenForResearch(saveFilePath);
        return new ResearchImage(File.ReadAllBytes(saveFilePath), [], []);
    }

    public static void Dump(IGameAdapter adapter, string saveFilePath, string outputDirectory, bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var image = Open(adapter, saveFilePath);
        var output = Path.GetFullPath(outputDirectory);
        var root = SaveRoot(saveFilePath);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.Equals(root, StringComparison.OrdinalIgnoreCase) || output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ResearchRejectedException("输出目录不能位于存档根目录内。");
        if (File.Exists(output))
            throw new ResearchRejectedException("输出路径是文件。");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() && !force)
            throw new ResearchRejectedException("输出目录不为空。");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "payload.bin"), image.Payload);
        var regions = image.Regions.Select(region => new
        {
            region.Index,
            region.Name,
            region.Offset,
            region.Length,
            region.Parent
        });
        File.WriteAllText(Path.Combine(output, "regions.json"), JsonSerializer.Serialize(regions, JsonOptions));
        using var headerStream = File.Create(Path.Combine(output, "header.json"));
        using var writer = new Utf8JsonWriter(headerStream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var field in image.Header)
            writer.WriteString(field.Name, field.Value);
        writer.WriteEndObject();
    }

    public static ResearchDiffResult Diff(IGameAdapter adapter, string leftPath, string rightPath) =>
        Diff(Open(adapter, leftPath), Open(adapter, rightPath));

    /// <summary>
    /// 先把已达标的已持有项改低 1，再只拉满已持有项。未持有的资源不进入变更集。
    /// </summary>
    public static void PrepareHeldQuantityPair(IGameAdapter adapter, string originalSave, string before, string after)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        File.Copy(originalSave, before);
        var document = adapter.Load(before);
        var held = adapter.Resources.Where(resource => document.GetQuantity(resource.Id) is not null).Select(resource => resource.Id).ToHashSet();
        var lower = new List<ResourceChange>();
        foreach (var resource in adapter.Resources)
        {
            if (!held.Contains(resource.Id) || resource.DefaultFill is not long target)
                continue;
            if (document.GetQuantity(resource.Id) == target)
                lower.Add(new ResourceChange(resource.Id, target - 1));
        }

        if (lower.Count > 0)
            PatchService.Patch(adapter, before, lower);
        File.Copy(before, after);
        var fill = ChangePlanner.Plan(adapter.Resources, fill: true, money: null, materials: null)
            .Where(change => held.Contains(change.Id))
            .ToArray();
        if (fill.Length > 0)
            PatchService.Patch(adapter, after, fill);
    }

    public static string DescribeHeldQuantityResearch(IGameAdapter adapter, string before, string after)
    {
        var document = adapter.Load(before);
        var held = adapter.Resources.Count(resource => document.GetQuantity(resource.Id) is not null);
        var diff = Diff(adapter, before, after);
        var changed = diff.Regions.Where(region => region.Kind == "changed").ToArray();
        var quantities = held > 0
            && changed.Length == held
            && changed.All(region => region.Name == "itemData" && region.Spans.Count == 1 && region.Spans[0].Start == 25 && region.Spans[0].End == 29)
            && diff.Regions.All(region => region.Kind == "changed")
            && diff.Header.Count == 0;
        var found = Find(adapter, after, new ResearchNeedle(ResearchNeedleKind.Tdb, "Items.money"));
        var image = Open(adapter, after);
        var inItem = false;
        if (adapter is ITweakDbIdSource tweaks)
        {
            var money = BitConverter.GetBytes(tweaks.TweakId("Items.money"));
            inItem = image.Regions.Any(region =>
                region.Name == "itemData"
                && region.Length >= 12
                && image.Payload.AsSpan(region.Offset + 4, 8).SequenceEqual(money));
        }

        if (quantities && found.Total > 0 && inItem)
            return "pass";
        var kinds = string.Join(",", diff.Regions.Select(region => region.Kind + ":" + region.Name).Distinct());
        var spans = string.Join(",", changed.Take(12).Select(region => region.Name + (region.Spans.Count == 0 ? "" : $"[{region.Spans[0].Start},{region.Spans[0].End})")));
        return $"changed={changed.Length};held={held};header={diff.Header.Count};kinds={kinds};spans={spans};moneyHits={found.Hits.Count}";
    }

    public static ResearchDiffResult Diff(ResearchImage left, ResearchImage right) => ResearchDiff.Compare(left, right);

    public static ResearchDiffResult SelectRegions(ResearchDiffResult diff, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (names.Count == 0)
            return diff;
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return new ResearchDiffResult(diff.Header, diff.Regions.Where(region => wanted.Contains(region.Name)).ToArray());
    }

    public static DiffSummary Summarize(ResearchDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var changes = new List<DiffSummaryRow>();
        var added = new List<RegionChange>();
        var removed = new List<RegionChange>();
        var shifts = 0;
        foreach (var region in diff.Regions)
        {
            switch (region.Kind)
            {
                case "added":
                    added.Add(region);
                    break;
                case "removed":
                    removed.Add(region);
                    break;
                case "indexShiftOnly":
                    shifts++;
                    break;
                case "changed":
                    changes.Add(Row(region, region.Spans.Sum(span => span.End - span.Start), region.Spans.Count));
                    break;
                case "resized":
                    changes.Add(Row(region, Math.Abs((region.LeftLength ?? 0) - (region.RightLength ?? 0)), 0));
                    break;
                default:
                    throw new ResearchRejectedException($"未知的差异类型 {region.Kind}。");
            }
        }

        return new DiffSummary(changes, added, removed, shifts, changes.Count, added.Count, removed.Count);
    }

    public static void ValidateInsertOutput(string sourceSaveFile, string outputDirectory, bool intoSaveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSaveFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var full = Path.GetFullPath(outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (File.Exists(full) || Directory.Exists(full))
            throw new ResearchRejectedException("输出目录已经存在。");
        var root = SaveRoot(sourceSaveFile);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var inside = full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        if (!inside)
            return;
        if (!intoSaveRoot)
            throw new ResearchRejectedException("输出目录位于存档根目录内，必须同时带 --into-save-root。");
        var name = Path.GetFileName(full);
        if (name.Length != "ManualSave-90".Length || !name.StartsWith("ManualSave-9", StringComparison.Ordinal) || !char.IsDigit(name[^1]))
            throw new ResearchRejectedException("写入存档根目录时，目录名必须是 ManualSave-90 到 ManualSave-99。");
    }

    public static void RequireSingleItemInsertion(ResearchDiffResult diff, int addedBytes)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var added = diff.Regions.Where(region => region.Kind == "added").ToArray();
        if (added.Length != 1 || added[0].Name != "itemData")
            throw new ResearchRejectedException("自检失败：应当只新增 1 个 itemData。");
        var inventory = diff.Regions.SingleOrDefault(region => region.Kind == "resized" && region.Name == "inventory");
        if (inventory is null || inventory.RightLength - inventory.LeftLength != addedBytes)
            throw new ResearchRejectedException("自检失败：inventory 的长度增量不对。");
        if (diff.Regions.Any(region => region.Kind == "resized" && region.Name != "inventory"))
            throw new ResearchRejectedException("自检失败：除 inventory 外还有长度变化。");
        if (diff.Regions.Any(region => region.Kind is not ("added" or "resized" or "indexShiftOnly" or "changed")))
            throw new ResearchRejectedException("自检失败：出现了预期之外的差异类型。");
        var unexpected = diff.Regions.Where(region => region.Kind == "changed" && !IndexBumpOnly(region)).Take(8).ToArray();
        if (unexpected.Length > 0)
        {
            var detail = string.Join("; ", unexpected.Select(region => $"{region.Name}#{region.LeftIndex}"));
            throw new ResearchRejectedException($"自检失败：出现了预期之外的内容变化（{detail}）。");
        }
        RequireHeader(diff, "nodeCount", 1);
        RequireHeader(diff, "decompressedLength", addedBytes);
        if (diff.Header.Any(field => field.Name is not ("nodeCount" or "decompressedLength")))
            throw new ResearchRejectedException("自检失败：头部还有其他字段变化。");
    }

    public static void RequireSingleStackRemoval(ResearchImage before, ResearchImage after, int removedBytes, int removedNode, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var diff = ResearchDiff.Compare(before, after);
        var removed = diff.Regions.Where(region => region.Kind == "removed").ToArray();
        if (removed.Length != 1 || removed[0].Name != "itemData" || removed[0].LeftIndex != removedNode || removed[0].LeftLength != 29)
            throw new ResearchRejectedException("自检失败：应当只删除 1 个 29 字节 itemData。");
        var inventory = diff.Regions.SingleOrDefault(region => region.Kind == "resized" && region.Name == "inventory");
        if (inventory is null || inventory.LeftLength - inventory.RightLength != removedBytes)
            throw new ResearchRejectedException("自检失败：inventory 的长度减量不对。");
        if (diff.Regions.Any(region => region.Kind == "resized" && region.Name != "inventory"))
            throw new ResearchRejectedException("自检失败：除 inventory 外还有长度变化。");
        if (diff.Regions.Any(region => region.Kind == "added"))
            throw new ResearchRejectedException("自检失败：删除时不应当新增节点。");
        foreach (var region in diff.Regions)
        {
            if (region.Kind is "removed" or "resized")
                continue;
            if (region.Kind == "indexShiftOnly")
            {
                if (!DroppedSelfIndex(before, after, region, removedNode))
                    throw new ResearchRejectedException($"自检失败：{region.Name} 的节点号减量不对。");
                continue;
            }

            if (region.Kind != "changed" || region.Spans.Any(span => !DroppedNodeSpan(before, region, span, removedNode)))
                throw new ResearchRejectedException($"自检失败：{region.Name} 出现了预期之外的字节变化。");
        }

        RequireHeader(diff, "nodeCount", -1);
        RequireHeader(diff, "decompressedLength", -removedBytes);
        foreach (var field in diff.Header)
        {
            if (field.Name is "nodeCount" or "decompressedLength")
                continue;
            if (field.Name == "chunkCount" && ChunkCountMatches(after, field.Right, chunkSize))
                continue;
            throw new ResearchRejectedException("自检失败：头部还有其他字段变化。");
        }
    }

    private static bool ChunkCountMatches(ResearchImage image, string? actual, int chunkSize)
    {
        var length = image.Header.First(field => field.Name == "decompressedLength").Value;
        var expected = (long.Parse(length, CultureInfo.InvariantCulture) + chunkSize - 1) / chunkSize;
        return actual == expected.ToString(CultureInfo.InvariantCulture);
    }

    private static bool DroppedSelfIndex(ResearchImage before, ResearchImage after, RegionChange region, int removedNode)
    {
        var left = before.Regions.First(item => item.Index == region.LeftIndex && item.Name == region.Name);
        var right = after.Regions.First(item => item.Index == region.RightIndex && item.Name == region.Name);
        var leftValue = BinaryPrimitives.ReadInt32LittleEndian(before.Payload.AsSpan(left.Offset, 4));
        var rightValue = BinaryPrimitives.ReadInt32LittleEndian(after.Payload.AsSpan(right.Offset, 4));
        return leftValue == left.Index && leftValue > removedNode && rightValue == leftValue - 1;
    }

    private static bool DroppedNodeSpan(ResearchImage before, RegionChange region, DiffSpan span, int removedNode)
    {
        if (span.End - span.Start != 4 || span.Left.Length < 8 || span.Right.Length < 8)
            return false;
        var owner = before.Regions.FirstOrDefault(item => item.Index == region.LeftIndex && item.Name == region.Name);
        if (owner is null)
            return false;
        var start = owner.Offset + span.Start;
        var oldValue = ReadInt(span.Left);
        var newValue = ReadInt(span.Right);
        return oldValue > removedNode
            && newValue == oldValue - 1
            && before.Regions.Any(item => item.Offset == start && item.Index == oldValue);
    }

    private static int ReadInt(string hex)
    {
        var bytes = Convert.FromHexString(hex[..8]);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static void RequireHeader(ResearchDiffResult diff, string name, long delta)
    {
        var field = diff.Header.SingleOrDefault(item => item.Name == name);
        if (field?.Left is null || field.Right is null)
            throw new ResearchRejectedException($"自检失败：头部缺少 {name}。");
        if (!long.TryParse(field.Left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            || !long.TryParse(field.Right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var right)
            || right - left != delta)
            throw new ResearchRejectedException($"自检失败：头部 {name} 的增量不是 {delta}。");
    }

    private static bool IndexBumpOnly(RegionChange region) =>
        region.Spans.Count > 0
        && region.Spans.All(span => span.End - span.Start == 4 && IsPlusOne(span.Left, span.Right));

    private static bool IsPlusOne(string leftHex, string rightHex)
    {
        if (leftHex.Length < 8 || rightHex.Length < 8)
            return false;
        var leftBytes = Convert.FromHexString(leftHex[..8]);
        var rightBytes = Convert.FromHexString(rightHex[..8]);
        var left = BinaryPrimitives.ReadInt32LittleEndian(leftBytes);
        var right = BinaryPrimitives.ReadInt32LittleEndian(rightBytes);
        return right == left + 1;
    }

    public static ResearchFindResult Find(IGameAdapter adapter, string saveFilePath, ResearchNeedle needle, string? inRegion = null)
    {
        var image = Open(adapter, saveFilePath);
        var pattern = NeedleBytes(adapter, needle);
        var hits = new List<FindHit>();
        var payload = image.Payload;
        if (pattern.Length == 0 || pattern.Length > payload.Length)
            return new ResearchFindResult(0, false, hits);

        var total = 0;
        for (var offset = 0; offset <= payload.Length - pattern.Length; offset++)
        {
            if (!payload.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
                continue;
            var region = SmallestRegion(image.Regions, offset, pattern.Length);
            if (inRegion is not null && !string.Equals(region?.Name, inRegion, StringComparison.OrdinalIgnoreCase))
                continue;
            total++;
            if (hits.Count >= MaxHits)
                continue;
            var relative = region is null ? offset : offset - region.Offset;
            hits.Add(new FindHit(offset, region?.Name, region?.Index, relative));
        }

        return new ResearchFindResult(total, total > MaxHits, hits);
    }

    private static DiffSummaryRow Row(RegionChange region, int changedBytes, int spanCount) =>
        new(region.Kind, region.Name, region.LeftIndex, region.RightIndex, region.LeftLength, region.RightLength, changedBytes, spanCount);

    public static string SaveRoot(string saveFilePath)
    {
        var full = Path.GetFullPath(saveFilePath);
        var directory = Path.GetDirectoryName(full) ?? throw new ResearchRejectedException("无法确定存档根目录。");
        if (Path.GetFileName(full).Equals("sav.dat", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(root))
                throw new ResearchRejectedException("无法确定存档根目录。");
            return root;
        }

        return directory;
    }

    private static byte[] NeedleBytes(IGameAdapter adapter, ResearchNeedle needle)
    {
        switch (needle.Kind)
        {
            case ResearchNeedleKind.U32:
                if (!uint.TryParse(needle.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    throw new ResearchRejectedException("u32 不是整数。");
                return BitConverter.GetBytes(number);
            case ResearchNeedleKind.Tdb:
                if (adapter is not ITweakDbIdSource source)
                    throw new ResearchRejectedException("这个游戏不能换算 TweakDBID。");
                return BitConverter.GetBytes(source.TweakId(needle.Text));
            case ResearchNeedleKind.Hex:
                var hex = needle.Text.Replace(" ", "", StringComparison.Ordinal);
                if (hex.Length == 0 || hex.Length % 2 != 0)
                    throw new ResearchRejectedException("hex 长度必须是偶数。");
                var bytes = new byte[hex.Length / 2];
                for (var i = 0; i < bytes.Length; i++)
                {
                    if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                        throw new ResearchRejectedException("hex 含有非十六进制字符。");
                }

                return bytes;
            case ResearchNeedleKind.Ascii:
                if (needle.Text.Any(value => value > 127))
                    throw new ResearchRejectedException("ascii 只能包含 ASCII 字符。");
                return Encoding.ASCII.GetBytes(needle.Text);
            default:
                throw new ResearchRejectedException("未知的搜索类型。");
        }
    }

    private static ResearchRegion? SmallestRegion(IReadOnlyList<ResearchRegion> regions, int offset, int length)
    {
        ResearchRegion? best = null;
        foreach (var region in regions)
        {
            if (offset < region.Offset || offset + length > region.Offset + region.Length)
                continue;
            if (best is null || region.Length < best.Length || (region.Length == best.Length && region.Offset > best.Offset))
                best = region;
        }

        return best;
    }
}
