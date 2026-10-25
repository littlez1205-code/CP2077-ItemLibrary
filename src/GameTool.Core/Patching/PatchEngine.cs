using System.Diagnostics;

namespace GameTool.Core;

public static class PatchEngine
{
    public static PatchReport Patch(IGameAdapter adapter, string saveFilePath, IReadOnlyList<ResourceChange> changes) =>
        Patch(adapter, saveFilePath, changes, new PatchOptions(saveFilePath + ".bak"));

    public static PatchReport Patch(
        IGameAdapter adapter,
        string saveFilePath,
        IReadOnlyList<ResourceChange> changes,
        PatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        var clock = Stopwatch.StartNew();
        RejectInvalidTargets(adapter, changes);
        var observed = new FileInfo(saveFilePath);
        var observedLength = observed.Length;
        var observedWriteTime = observed.LastWriteTimeUtc;

        var original = adapter.Load(saveFilePath);
        var before = ReadQuantities(original, adapter.Resources, out var ambiguous);
        var working = adapter.Load(saveFilePath);
        var results = working.Apply(changes);
        if (results.Any(result => result.Outcome == ChangeOutcome.Ambiguous))
            throw new PatchRejectedException("存在多个堆叠的资源，已整体拒绝，未写文件。");
        if (results.All(result => result.Outcome is not (ChangeOutcome.Changed or ChangeOutcome.Inserted)))
            return new PatchReport(PatchOutcome.NoChange, results, null, clock.ElapsedMilliseconds);

        return Commit(
            adapter,
            saveFilePath,
            options,
            observed,
            observedLength,
            observedWriteTime,
            before,
            ambiguous,
            results,
            clock,
            reloaded =>
            {
                if (reloaded is not IStrictVerifier verifier)
                    return;
                var changed = changes
                    .Where(change => results.Any(result => result.Id == change.Id && result.Outcome is ChangeOutcome.Changed or ChangeOutcome.Inserted))
                    .ToArray();
                verifier.VerifyOnlyTargetsChanged(original, changed);
            },
            working);
    }

    public static CloneReport Clone(IGameAdapter adapter, string saveFilePath, CloneRequest request, PatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (request.Copies is < 1 or > 10)
            throw new PatchRejectedException("复制件数必须在 1 到 10 之间。");
        var clock = Stopwatch.StartNew();
        var observed = new FileInfo(saveFilePath);
        var observedLength = observed.Length;
        var observedWriteTime = observed.LastWriteTimeUtc;
        var original = adapter.Load(saveFilePath);
        var before = ReadQuantities(original, adapter.Resources, out var ambiguous);
        var working = adapter.Load(saveFilePath);
        if (working is not IItemCloneCapable cloneable || original is not IItemCloneCapable)
            throw new PatchRejectedException("这个存档不能复制装备。");
        var applied = cloneable.Clone(request);
        var expectedCount = cloneable.PlayerItemCount;
        var patch = Commit(
            adapter,
            saveFilePath,
            options,
            observed,
            observedLength,
            observedWriteTime,
            before,
            ambiguous,
            [],
            clock,
            reloaded =>
            {
                if (reloaded is not IItemCloneCapable check || check.PlayerItemCount != expectedCount)
                    throw new PatchRejectedException("回读校验失败：玩家物品数没有增加对应的件数。");
                check.VerifyOnlyCloned(original, applied);
            },
            working);
        return new CloneReport(patch, applied.Seeds, applied.AddedBytes);
    }

    public static GenerateReport Generate(IGameAdapter adapter, string saveFilePath, GenerateRequest request, PatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (string.IsNullOrWhiteSpace(request.Target))
            throw new PatchRejectedException("缺少要生成的目标。");
        if (request.Copies is < 1 or > 10)
            throw new PatchRejectedException("生成件数必须在 1 到 10 之间。");
        var clock = Stopwatch.StartNew();
        var observed = new FileInfo(saveFilePath);
        var observedLength = observed.Length;
        var observedWriteTime = observed.LastWriteTimeUtc;
        var original = adapter.Load(saveFilePath);
        var before = ReadQuantities(original, adapter.Resources, out var ambiguous);
        var working = adapter.Load(saveFilePath);
        if (working is not IItemGenerateCapable generator || original is not IItemGenerateCapable source)
            throw new PatchRejectedException("这个存档不能生成物品。");
        var applied = generator.Generate(request);
        var expectedPlayerCount = request.ToStash ? source.PlayerItemCount : source.PlayerItemCount + request.Copies;
        var expectedStashCount = request.ToStash ? source.StashItemCount + request.Copies : source.StashItemCount;
        if (generator.PlayerItemCount != expectedPlayerCount
            || (request.ToStash && generator.StashItemCount != expectedStashCount)
            || applied.Copies != request.Copies
            || applied.Records.Count != request.Copies)
            throw new PatchRejectedException("生成结果与请求的件数不一致。");
        var patch = Commit(
            adapter,
            saveFilePath,
            options,
            observed,
            observedLength,
            observedWriteTime,
            before,
            ambiguous,
            [],
            clock,
            reloaded =>
            {
                if (reloaded is not IItemGenerateCapable check)
                    throw new PatchRejectedException("回读校验失败：无法作为物品生成文档加载。");
                if (request.ToStash)
                {
                    if (check.PlayerItemCount != expectedPlayerCount || check.StashItemCount != expectedStashCount)
                        throw new PatchRejectedException("回读校验失败：储藏箱物品数没有增加对应的件数。");
                }
                else
                {
                    if (check.PlayerItemCount != expectedPlayerCount)
                        throw new PatchRejectedException("回读校验失败：玩家物品数没有增加对应的件数。");
                }
                check.VerifyOnlyGenerated(original, applied);
            },
            working);
        return new GenerateReport(patch, applied.Seeds, applied.AddedBytes, applied.TemplateKey, applied.TemplateLabel);
    }

    private static PatchReport Commit(
        IGameAdapter adapter,
        string saveFilePath,
        PatchOptions options,
        FileInfo observed,
        long observedLength,
        DateTime observedWriteTime,
        Dictionary<ResourceId, long?> before,
        HashSet<ResourceId> ambiguous,
        IReadOnlyList<ResourceChangeResult> results,
        Stopwatch clock,
        Action<ISaveDocument> verifyStrict,
        ISaveDocument working)
    {
        var temporary = saveFilePath + ".tmp";
        var backup = options.BackupPath;
        if (File.Exists(temporary))
            File.Delete(temporary);
        DateTime? written = null;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                working.Save(stream);

            var reloaded = adapter.Load(temporary);
            VerifyReloaded(adapter, reloaded, before, ambiguous, results);
            verifyStrict(reloaded);

            observed.Refresh();
            if (!observed.Exists || observed.Length != observedLength || observed.LastWriteTimeUtc != observedWriteTime)
                throw new SaveChangedDuringPatchException();

            File.Replace(temporary, saveFilePath, backup);
            written = File.GetLastWriteTimeUtc(saveFilePath);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }

        return new PatchReport(PatchOutcome.Patched, results, backup, clock.ElapsedMilliseconds, written);
    }

    public static RestoreReport Restore(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        var clock = Stopwatch.StartNew();
        var backup = saveFilePath + ".bak";
        if (!File.Exists(backup))
            throw new PatchRejectedException("找不到备份文件。");
        _ = adapter.Load(backup);

        var temporary = saveFilePath + ".tmp";
        if (File.Exists(temporary))
            File.Delete(temporary);
        try
        {
            File.Copy(backup, temporary);
            File.Replace(temporary, saveFilePath, destinationBackupFileName: null);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }

        return new RestoreReport(saveFilePath, clock.ElapsedMilliseconds);
    }

    private static void RejectInvalidTargets(IGameAdapter adapter, IReadOnlyList<ResourceChange> changes)
    {
        foreach (var change in changes)
        {
            var resource = adapter.Resources.FirstOrDefault(item => item.Id == change.Id);
            if (resource is null)
                throw new PatchRejectedException($"未知资源 {change.Id.Value}。");
            if (change.TargetQuantity < 0 || change.TargetQuantity > resource.Max)
            {
                throw new PatchRejectedException(
                    $"资源 {change.Id.Value} 的目标值 {change.TargetQuantity} 超出安全范围 0..{resource.Max}。");
            }
        }
    }

    private static Dictionary<ResourceId, long?> ReadQuantities(
        ISaveDocument document,
        IReadOnlyList<ResourceDescriptor> resources,
        out HashSet<ResourceId> ambiguous)
    {
        var values = new Dictionary<ResourceId, long?>();
        ambiguous = [];
        foreach (var resource in resources)
        {
            try
            {
                values[resource.Id] = document.GetQuantity(resource.Id);
            }
            catch (AmbiguousResourceException)
            {
                ambiguous.Add(resource.Id);
            }
        }

        return values;
    }

    private static void VerifyReloaded(
        IGameAdapter adapter,
        ISaveDocument reloaded,
        Dictionary<ResourceId, long?> before,
        HashSet<ResourceId> ambiguous,
        IReadOnlyList<ResourceChangeResult> results)
    {
        foreach (var resource in adapter.Resources)
        {
            if (ambiguous.Contains(resource.Id))
                continue;
            long? actual;
            try
            {
                actual = reloaded.GetQuantity(resource.Id);
            }
            catch (AmbiguousResourceException ex)
            {
                throw new PatchRejectedException(ex.Message);
            }

            var result = results.FirstOrDefault(item => item.Id == resource.Id);
            var expected = result?.Outcome is ChangeOutcome.Changed or ChangeOutcome.Inserted ? result.AppliedQuantity : before[resource.Id];
            if (actual != expected)
            {
                throw new PatchRejectedException(
                    $"回读校验失败：{resource.Id.Value} 期望 {expected?.ToString() ?? "未持有"}，实际 {actual?.ToString() ?? "未持有"}。");
            }
        }
    }
}
