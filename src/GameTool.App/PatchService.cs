using GameTool.Core;

namespace GameTool.App;

public static class PatchService
{
    public static PatchReport Patch(IGameAdapter adapter, string saveFilePath, IReadOnlyList<ResourceChange> changes) =>
        PatchEngine.Patch(adapter, saveFilePath, changes);

    public static RestoreReport Restore(IGameAdapter adapter, string saveFilePath) =>
        PatchEngine.Restore(adapter, saveFilePath);
}
