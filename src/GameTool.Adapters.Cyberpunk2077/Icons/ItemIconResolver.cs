using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Core;

namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>UIIcon 记录：图集资源路径（深度路径，原样）和图集里的部件名。</summary>
public sealed record UiIcon(string Name, string AtlasPath, string PartName);

/// <summary>图标名的来源：iconPath、按规则生成的名字，或生成名的回退。</summary>
public enum IconNameSource
{
    IconPath,
    Generated,
    Fallback
}

public sealed record ResolvedIcon(UiIcon Icon, IconNameSource Source);

/// <summary>
/// 物品记录 → UIIcon（T020 预研第 3 节）。游戏脚本 ResolveItemIconName：iconPath 有效就用 UIIcon.&lt;iconPath&gt;，
/// 否则用原生 TranslateItemToIconName 生成的名字。原生规则是从 items_icons.tweak 的 4,775 个生成名反推的：
/// <c>{kind}_{entity}_{appearanceName 或 None}_{每个后缀值加 _}[_{visualTags 用 _ 连接}]</c>。
/// kind = weapon / clothing / outfit / undefined；entity 武器取 appearanceResourceName，服装取 entityName；
/// 后缀 Gender → Female|Male，Camera → TPP，Partial / HairType 不出现。
/// 每个候选都对照已知的 UIIcon 记录；查不到时依次试：去掉 visualTags、换成 Default 标签、同前缀的唯一或 _Default 兄弟。
/// </summary>
public sealed class ItemIconResolver
{
    public const string UiIconPackage = "UIIcon.";

    private readonly TweakSourceIndex _index;
    private readonly Dictionary<string, UiIcon> _icons;
    private readonly string[] _sorted;

    private ItemIconResolver(TweakSourceIndex index, Dictionary<string, UiIcon> icons)
    {
        _index = index;
        _icons = icons;
        _sorted = icons.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>已知的 UIIcon 数（带图集路径和部件名的记录）。</summary>
    public int IconCount => _icons.Count;

    public static ItemIconResolver Build(TweakSourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var icons = new Dictionary<string, UiIcon>(StringComparer.Ordinal);
        foreach (var record in index.RecordNames)
        {
            if (!record.StartsWith(UiIconPackage, StringComparison.Ordinal))
                continue;
            var atlas = index.FieldOf(record, "atlasResourcePath");
            var part = index.FieldOf(record, "atlasPartName");
            if (string.IsNullOrWhiteSpace(atlas) || string.IsNullOrWhiteSpace(part))
                continue;
            var name = record[UiIconPackage.Length..];
            icons[name] = new UiIcon(name, atlas, part);
        }

        return new ItemIconResolver(index, icons);
    }

    public UiIcon? FindIcon(string name) => _icons.TryGetValue(StripPackage(name), out var icon) ? icon : null;

    /// <summary>记录的图标；记录不存在或找不到对应的 UIIcon 时返回 null。</summary>
    public ResolvedIcon? Resolve(string recordName, IconGender gender)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        if (_index.Ancestors(recordName).Count == 0)
            return null;
        var iconPath = _index.FieldOf(recordName, "iconPath");
        if (!string.IsNullOrWhiteSpace(iconPath) && _icons.TryGetValue(StripPackage(iconPath), out var direct))
            return new ResolvedIcon(direct, IconNameSource.IconPath);

        // 配方（T023）：自己的 iconPath 没有对应图标时用产物的图标，产物也没有就留给占位图；不按配方自己的实体名生成。
        var crafted = _index.FieldOf(recordName, TweakSourceIndex.CraftingResultItem);
        if (!string.IsNullOrEmpty(crafted))
            return crafted != recordName && _index.FieldOf(crafted, TweakSourceIndex.CraftingResultItem) is null or "" ? Resolve(crafted, gender) : null;

        var resolved = ResolveGenerated(recordName, gender);
        if (resolved is null && gender == IconGender.Male)
            resolved = ResolveGenerated(recordName, IconGender.Female);
        return resolved;
    }

    private ResolvedIcon? ResolveGenerated(string recordName, IconGender gender)
    {
        foreach (var stem in Stems(_index, recordName, gender))
        {
            var tags = _index.ListOf(recordName, "visualTags");
            var full = tags.Count == 0 ? stem : stem + "_" + string.Join("_", tags);
            if (_icons.TryGetValue(full, out var icon))
                return new ResolvedIcon(icon, IconNameSource.Generated);
            if (tags.Count > 0 && _icons.TryGetValue(stem, out icon))
                return new ResolvedIcon(icon, IconNameSource.Fallback);
            if (_icons.TryGetValue(stem + "_Default", out icon))
                return new ResolvedIcon(icon, IconNameSource.Fallback);
            var siblings = WithPrefix(stem + "_");
            if (siblings.Count == 1)
                return new ResolvedIcon(_icons[siblings[0]], IconNameSource.Fallback);
            var preferred = siblings.FirstOrDefault(name => name.EndsWith("__Default", StringComparison.Ordinal));
            if (preferred is not null)
                return new ResolvedIcon(_icons[preferred], IconNameSource.Fallback);
        }

        return null;
    }

    /// <summary>按原生规则生成的完整图标名（含 visualTags）；推不出实体名时返回 null。</summary>
    public static string? GeneratedName(TweakSourceIndex index, string recordName, IconGender gender)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        var stem = Stems(index, recordName, gender).FirstOrDefault();
        if (stem is null)
            return null;
        var tags = index.ListOf(recordName, "visualTags");
        return tags.Count == 0 ? stem : stem + "_" + string.Join("_", tags);
    }

    /// <summary>图标种类：itemType 的 Clo_Outfit / Clo_* / Wea_* 优先，其次 itemCategory，都不是就是 undefined。</summary>
    public static string KindOf(TweakSourceIndex index, string recordName)
    {
        ArgumentNullException.ThrowIfNull(index);
        var type = index.FieldOf(recordName, "itemType") ?? "";
        if (type == "ItemType.Clo_Outfit")
            return "outfit";
        if (type.StartsWith("ItemType.Clo_", StringComparison.Ordinal))
            return "clothing";
        if (type.StartsWith("ItemType.Wea_", StringComparison.Ordinal))
            return "weapon";
        return index.FieldOf(recordName, "itemCategory") switch
        {
            "ItemCategory.Weapon" => "weapon",
            "ItemCategory.Clothing" => "clothing",
            _ => "undefined"
        };
    }

    /// <summary>不带 visualTags 的前半段：先用这一种类的主实体名，再试另一个实体字段。</summary>
    private static IEnumerable<string> Stems(TweakSourceIndex index, string recordName, IconGender gender)
    {
        var kind = KindOf(index, recordName);
        var appearance = index.FieldOf(recordName, "appearanceName");
        if (string.IsNullOrEmpty(appearance))
            appearance = "None";
        var suffixes = string.Concat(index.ListOf(recordName, "appearanceSuffixes")
            .Select(suffix => SuffixValue(suffix, gender))
            .Where(value => value is not null)
            .Select(value => value + "_"));
        var resource = index.FieldOf(recordName, "appearanceResourceName");
        var entity = index.FieldOf(recordName, "entityName");
        var order = kind == "weapon" ? new[] { resource, entity } : [entity, resource];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in order)
        {
            if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                continue;
            yield return kind + "_" + candidate + "_" + appearance + "_" + suffixes;
        }
    }

    private static string? SuffixValue(string suffix, IconGender gender)
    {
        if (suffix.EndsWith(".Gender", StringComparison.Ordinal))
            return gender == IconGender.Male ? "Male" : "Female";
        if (suffix.EndsWith(".Camera", StringComparison.Ordinal))
            return "TPP";
        return null;
    }

    private List<string> WithPrefix(string prefix)
    {
        var found = new List<string>();
        var start = Array.BinarySearch(_sorted, prefix, StringComparer.Ordinal);
        if (start < 0)
            start = ~start;
        for (var i = start; i < _sorted.Length && _sorted[i].StartsWith(prefix, StringComparison.Ordinal); i++)
            found.Add(_sorted[i]);
        return found;
    }

    private static string StripPackage(string name) =>
        name.StartsWith(UiIconPackage, StringComparison.Ordinal) ? name[UiIconPackage.Length..] : name;
}
