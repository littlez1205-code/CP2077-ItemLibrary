using GameTool.Core;

namespace GameTool.App;

public static class SaveQueryService
{
    public static IReadOnlyList<SaveSlotInfo> ListSlots(IGameAdapter adapter, string saveRoot)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter.DiscoverSaves(saveRoot).ToArray();
    }

    public static SaveInfoData GetInfo(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var document = adapter.Load(saveFilePath);
        var diagnostics = document is ISaveDiagnostics source ? source.Fields : [];

        var inspector = document as IStackInspector;
        var resources = adapter.Resources.Select(resource =>
        {
            var stacks = inspector?.GetStacks(resource.Id) ?? [];
            return new ResourceInfo(
                resource.Id.Value,
                resource.NameZh,
                stacks.Count == 1 ? stacks[0] : null,
                stacks,
                stacks.Count > 1);
        }).ToArray();

        return new SaveInfoData(
            document.Version.GameVersion,
            document.Version.SaveVersion,
            diagnostics,
            resources);
    }
}
