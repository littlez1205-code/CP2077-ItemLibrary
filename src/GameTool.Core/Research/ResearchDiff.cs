namespace GameTool.Core;

public sealed record DiffSpan(int Start, int End, string Left, string Right);

public sealed record RegionChange(
    string Kind,
    string Name,
    int? LeftIndex,
    int? RightIndex,
    int? LeftLength,
    int? RightLength,
    int? FirstDifference,
    IReadOnlyList<DiffSpan> Spans);

public sealed record HeaderChange(string Name, string? Left, string? Right);

public sealed record ResearchDiffResult(IReadOnlyList<HeaderChange> Header, IReadOnlyList<RegionChange> Regions);

public static class ResearchDiff
{
    private const int MaxShownBytes = 32;

    public static ResearchDiffResult Compare(ResearchImage left, ResearchImage right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var header = DiffHeader(left.Header, right.Header);
        var leftRegions = RegionsOrWhole(left);
        var rightRegions = RegionsOrWhole(right);
        var pairs = Align(left.Payload, leftRegions, right.Payload, rightRegions);
        var matchedLeft = new HashSet<int>();
        var matchedRight = new HashSet<int>();
        var changes = new List<RegionChange>();
        foreach (var (leftIndex, rightIndex) in pairs)
        {
            matchedLeft.Add(leftIndex);
            matchedRight.Add(rightIndex);
            var change = CompareRegion(left.Payload, leftRegions[leftIndex], right.Payload, rightRegions[rightIndex]);
            if (change is not null)
                changes.Add(change);
        }

        for (var i = 0; i < leftRegions.Count; i++)
        {
            if (matchedLeft.Contains(i))
                continue;
            var region = leftRegions[i];
            changes.Add(new RegionChange("removed", region.Name, region.Index, null, region.Length, null, null, []));
        }

        for (var i = 0; i < rightRegions.Count; i++)
        {
            if (matchedRight.Contains(i))
                continue;
            var region = rightRegions[i];
            changes.Add(new RegionChange("added", region.Name, null, region.Index, null, region.Length, null, []));
        }

        var visible = changes.Where(change => !CoveredBySmallerChange(change, changes, leftRegions)).ToArray();
        return new ResearchDiffResult(header, visible);
    }

    private static IReadOnlyList<ResearchRegion> RegionsOrWhole(ResearchImage image)
    {
        if (image.Regions.Count > 0)
            return image.Regions;
        return [new ResearchRegion(0, "", 0, image.Payload.Length, null)];
    }

    private static List<HeaderChange> DiffHeader(IReadOnlyList<ResearchField> left, IReadOnlyList<ResearchField> right)
    {
        var changes = new List<HeaderChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in left)
        {
            seen.Add(field.Name);
            var match = right.FirstOrDefault(item => item.Name == field.Name);
            if (match is null)
                changes.Add(new HeaderChange(field.Name, field.Value, null));
            else if (match.Value != field.Value)
                changes.Add(new HeaderChange(field.Name, field.Value, match.Value));
        }

        foreach (var field in right)
        {
            if (!seen.Contains(field.Name))
                changes.Add(new HeaderChange(field.Name, null, field.Value));
        }

        return changes;
    }

    private static bool CoveredBySmallerChange(RegionChange change, List<RegionChange> changes, IReadOnlyList<ResearchRegion> leftRegions)
    {
        if (change.Kind != "changed" || change.LeftIndex is null || change.Spans.Count == 0)
            return false;
        var self = leftRegions.FirstOrDefault(region => region.Index == change.LeftIndex && region.Name == change.Name);
        if (self is null)
            return false;
        return change.Spans.All(span =>
        {
            var start = self.Offset + span.Start;
            var end = self.Offset + span.End;
            return changes.Any(other =>
            {
                if (other.Kind != "changed" || other.LeftIndex is null || other.LeftIndex == self.Index)
                    return false;
                var inner = leftRegions.FirstOrDefault(region => region.Index == other.LeftIndex && region.Name == other.Name);
                return inner is not null
                    && inner.Length < self.Length
                    && start >= inner.Offset
                    && end <= inner.Offset + inner.Length;
            });
        });
    }

    private static RegionChange? CompareRegion(byte[] leftPayload, ResearchRegion left, byte[] rightPayload, ResearchRegion right)
    {
        var leftBytes = Slice(leftPayload, left);
        var rightBytes = Slice(rightPayload, right);
        if (leftBytes.Length == rightBytes.Length)
        {
            if (leftBytes.AsSpan().SequenceEqual(rightBytes))
                return null;
            if (IndexShiftOnly(leftBytes, rightBytes))
                return new RegionChange("indexShiftOnly", left.Name, left.Index, right.Index, left.Length, right.Length, null, []);
            return new RegionChange("changed", left.Name, left.Index, right.Index, left.Length, right.Length, null, Spans(leftBytes, rightBytes));
        }

        var first = 0;
        var shared = Math.Min(leftBytes.Length, rightBytes.Length);
        while (first < shared && leftBytes[first] == rightBytes[first])
            first++;
        return new RegionChange("resized", left.Name, left.Index, right.Index, left.Length, right.Length, first, []);
    }

    private static bool IndexShiftOnly(byte[] left, byte[] right) =>
        left.Length > 4
        && !left.AsSpan(0, 4).SequenceEqual(right.AsSpan(0, 4))
        && left.AsSpan(4).SequenceEqual(right.AsSpan(4));

    private static List<DiffSpan> Spans(byte[] left, byte[] right)
    {
        var spans = new List<DiffSpan>();
        var index = 0;
        while (index < left.Length)
        {
            if (left[index] == right[index])
            {
                index++;
                continue;
            }

            var start = index;
            while (index < left.Length && left[index] != right[index])
                index++;
            var end = index - start < 4 ? Math.Min(left.Length, start + 4) : index;
            var count = Math.Min(MaxShownBytes, end - start);
            spans.Add(new DiffSpan(
                start,
                end,
                Convert.ToHexString(left.AsSpan(start, count)).ToLowerInvariant(),
                Convert.ToHexString(right.AsSpan(start, count)).ToLowerInvariant()));
            index = end;
        }

        return spans;
    }

    private static byte[] Slice(byte[] payload, ResearchRegion region)
    {
        if (region.Offset < 0 || region.Length < 0 || region.Offset > payload.Length || region.Length > payload.Length - region.Offset)
            throw new InvalidDataException($"区域 {region.Name} 超出解压数据。");
        return payload.AsSpan(region.Offset, region.Length).ToArray();
    }

    private static List<(int Left, int Right)> Align(
        byte[] leftPayload,
        IReadOnlyList<ResearchRegion> left,
        byte[] rightPayload,
        IReadOnlyList<ResearchRegion> right)
    {
        var cells = new Cell[left.Count + 1, right.Count + 1];
        var choice = new byte[left.Count + 1, right.Count + 1];
        for (var i = 1; i <= left.Count; i++)
        {
            for (var j = 1; j <= right.Count; j++)
            {
                var best = cells[i - 1, j];
                var from = (byte)1;
                var other = cells[i, j - 1];
                if (Better(other, best))
                {
                    best = other;
                    from = 2;
                }

                if (left[i - 1].Name == right[j - 1].Name)
                {
                    var diagonal = cells[i - 1, j - 1];
                    var matched = new Cell(diagonal.Matches + 1, diagonal.Quality + Quality(leftPayload, left[i - 1], rightPayload, right[j - 1]));
                    if (Better(matched, best) || (matched.Matches == best.Matches && matched.Quality == best.Quality))
                    {
                        best = matched;
                        from = 3;
                    }
                }

                cells[i, j] = best;
                choice[i, j] = from;
            }
        }

        var pairs = new List<(int, int)>();
        var row = left.Count;
        var column = right.Count;
        while (row > 0 && column > 0)
        {
            switch (choice[row, column])
            {
                case 3:
                    pairs.Add((row - 1, column - 1));
                    row--;
                    column--;
                    break;
                case 1:
                    row--;
                    break;
                default:
                    column--;
                    break;
            }
        }

        pairs.Reverse();
        return pairs;
    }

    private readonly record struct Cell(int Matches, int Quality);

    private static bool Better(Cell candidate, Cell current) =>
        candidate.Matches > current.Matches || (candidate.Matches == current.Matches && candidate.Quality > current.Quality);

    private static int Quality(byte[] leftPayload, ResearchRegion left, byte[] rightPayload, ResearchRegion right)
    {
        var leftBytes = Slice(leftPayload, left);
        var rightBytes = Slice(rightPayload, right);
        if (leftBytes.Length == rightBytes.Length && leftBytes.AsSpan().SequenceEqual(rightBytes))
            return 2;
        if (leftBytes.Length == rightBytes.Length && IndexShiftOnly(leftBytes, rightBytes))
            return 1;
        return 0;
    }
}
