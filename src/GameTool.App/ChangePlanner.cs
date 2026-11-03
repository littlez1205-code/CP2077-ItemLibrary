using GameTool.Core;

namespace GameTool.App;

public static class ChangePlanner
{
    public static IReadOnlyList<ResourceChange> Plan(
        IReadOnlyList<ResourceDescriptor> resources,
        bool fill,
        long? money,
        long? materials,
        IReadOnlyList<ResourceChange>? explicitChanges = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var map = new Dictionary<ResourceId, long>();
        if (fill)
        {
            foreach (var resource in resources)
            {
                if (resource.DefaultFill is not long target)
                    throw new InvalidDataException(UiText.T($"资源 {resource.Id.Value} 没有 defaultFill。", $"資源 {resource.Id.Value} 沒有 defaultFill。", $"Resource {resource.Id.Value} has no defaultFill."));
                map[resource.Id] = target;
            }
        }

        if (materials is long materialTarget)
        {
            foreach (var resource in resources.Where(item => item.Group == "material"))
                map[resource.Id] = materialTarget;
        }

        if (money is long moneyTarget)
        {
            var currency = resources.Where(item => item.Group == "currency").ToArray();
            if (currency.Length == 0)
                throw new InvalidDataException(UiText.T("找不到 group 为 currency 的资源。", "找不到 group 為 currency 的資源。", "No resource in the currency group was found."));
            if (currency.Length > 1)
                throw new InvalidDataException(UiText.T("group 为 currency 的资源不止一个。", "group 為 currency 的資源不止一個。", "More than one resource is in the currency group."));
            map[currency[0].Id] = moneyTarget;
        }

        if (explicitChanges is not null)
        {
            foreach (var change in explicitChanges)
            {
                if (!resources.Any(resource => resource.Id == change.Id))
                    throw new InvalidDataException(UiText.T($"未知资源 {change.Id.Value}。", $"未知資源 {change.Id.Value}。", $"Unknown resource {change.Id.Value}."));
                map[change.Id] = change.TargetQuantity;
            }
        }

        return map.Select(pair => new ResourceChange(pair.Key, pair.Value)).ToArray();
    }
}
