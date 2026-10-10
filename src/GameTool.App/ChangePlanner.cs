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
                    throw new InvalidDataException($"资源 {resource.Id.Value} 没有 defaultFill。");
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
                throw new InvalidDataException("找不到 group 为 currency 的资源。");
            if (currency.Length > 1)
                throw new InvalidDataException("group 为 currency 的资源不止一个。");
            map[currency[0].Id] = moneyTarget;
        }

        if (explicitChanges is not null)
        {
            foreach (var change in explicitChanges)
            {
                if (!resources.Any(resource => resource.Id == change.Id))
                    throw new InvalidDataException($"未知资源 {change.Id.Value}。");
                map[change.Id] = change.TargetQuantity;
            }
        }

        return map.Select(pair => new ResourceChange(pair.Key, pair.Value)).ToArray();
    }
}
