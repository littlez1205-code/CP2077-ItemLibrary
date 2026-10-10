using System.Text.Json;

namespace GameTool.Core;

public static class ResourceCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<ResourceDescriptor> Load(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        var rows = JsonSerializer.Deserialize<List<ResourceRow>>(utf8Json, Options)
            ?? throw new InvalidDataException("resources.json 不是数组。");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResourceDescriptor>(rows.Count);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                throw new InvalidDataException("资源 id 为空。");
            if (!seen.Add(row.Id))
                throw new InvalidDataException($"资源 id 重复：{row.Id}。");
            if (row.Name is null || string.IsNullOrWhiteSpace(row.Name.Zh) || string.IsNullOrWhiteSpace(row.Name.En))
                throw new InvalidDataException($"资源 {row.Id} 缺少中文或英文名。");
            if (string.IsNullOrWhiteSpace(row.Group))
                throw new InvalidDataException($"资源 {row.Id} 缺少 group。");
            if (row.Max <= 0)
                throw new InvalidDataException($"资源 {row.Id} 的 max 必须大于 0。");
            if (row.DefaultFill is long fill && fill < 0)
                throw new InvalidDataException($"资源 {row.Id} 的 defaultFill 不能为负。");
            if (row.DefaultFill is long fillAbove && fillAbove > row.Max)
                throw new InvalidDataException($"资源 {row.Id} 的 defaultFill 大于 max。");
            if (string.IsNullOrWhiteSpace(row.Locator))
                throw new InvalidDataException($"资源 {row.Id} 的 locator 为空。");

            result.Add(new ResourceDescriptor(
                new ResourceId(row.Id),
                row.Name.Zh,
                row.Name.En,
                row.Group,
                row.Max,
                row.DefaultFill,
                row.Locator));
        }

        return result;
    }

    private sealed class ResourceRow
    {
        public string? Id { get; set; }
        public ResourceName? Name { get; set; }
        public string? Group { get; set; }
        public long Max { get; set; }
        public long? DefaultFill { get; set; }
        public string? Locator { get; set; }
    }

    private sealed class ResourceName
    {
        public string? Zh { get; set; }
        public string? En { get; set; }
    }
}
