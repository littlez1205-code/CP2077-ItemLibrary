using GameTool.Core;

namespace GameTool.App;

public static class CloneService
{
    public static IReadOnlyList<CloneListItem> List(IGameAdapter adapter, string saveFilePath)
    {
        var document = Require(adapter, saveFilePath);
        return document.ListCloneable();
    }

    public static CloneReport Run(
        IGameAdapter adapter,
        string saveFilePath,
        CloneRequest request,
        bool backupFirst,
        BackupStore backups,
        EnableService enable)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(enable);
        Require(adapter, saveFilePath);
        if (backupFirst)
            backups.Add(adapter, Path.GetDirectoryName(saveFilePath)!);
        // 桌面端按 D006 不在存档目录生成 .bak；备份走备份区。
        var report = PatchEngine.Clone(adapter, saveFilePath, request, new PatchOptions(null));
        enable.AcceptOwnWrite(saveFilePath, report.Patch.WrittenLastWriteTimeUtc);
        return report;
    }

    private static IItemCloneCapable Require(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (adapter.Load(saveFilePath) is not IItemCloneCapable document)
            throw new PatchRejectedException("这个存档不能复制装备。");
        return document;
    }
}
