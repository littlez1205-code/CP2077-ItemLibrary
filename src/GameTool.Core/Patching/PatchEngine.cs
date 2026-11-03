using System.Diagnostics;
using System.Globalization;

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
            throw new PatchRejectedException(UiText.T("存在多个堆叠的资源，已整体拒绝，未写文件。", "存在多個堆疊的資源，已整體拒絕，未寫檔案。", "Some resources have multiple stacks, so the whole change was rejected and nothing was written."));
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
            throw new PatchRejectedException(UiText.T("复制件数必须在 1 到 10 之间。", "複製件數必須在 1 到 10 之間。", "The number of copies must be between 1 and 10."));
        var clock = Stopwatch.StartNew();
        var observed = new FileInfo(saveFilePath);
        var observedLength = observed.Length;
        var observedWriteTime = observed.LastWriteTimeUtc;
        var original = adapter.Load(saveFilePath);
        var before = ReadQuantities(original, adapter.Resources, out var ambiguous);
        var working = adapter.Load(saveFilePath);
        if (working is not IItemCloneCapable cloneable || original is not IItemCloneCapable)
            throw new PatchRejectedException(UiText.T("这个存档不能复制装备。", "這個存檔不能複製裝備。", "This save does not support copying gear."));
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
                    throw new PatchRejectedException(UiText.T("回读校验失败：玩家物品数没有增加对应的件数。", "回讀校驗失敗：玩家物品數沒有增加對應的件數。", "Read-back check failed: the player item count did not increase by the expected amount."));
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
            throw new PatchRejectedException(UiText.T("缺少要生成的目标。", "缺少要生成的目標。", "No item to generate was given."));
        if (request.Copies is < 1 or > 10)
            throw new PatchRejectedException(UiText.T("生成件数必须在 1 到 10 之间。", "生成件數必須在 1 到 10 之間。", "The number of items to generate must be between 1 and 10."));
        var clock = Stopwatch.StartNew();
        var observed = new FileInfo(saveFilePath);
        var observedLength = observed.Length;
        var observedWriteTime = observed.LastWriteTimeUtc;
        var original = adapter.Load(saveFilePath);
        var before = ReadQuantities(original, adapter.Resources, out var ambiguous);
        var working = adapter.Load(saveFilePath);
        if (working is not IItemGenerateCapable generator || original is not IItemGenerateCapable source)
            throw new PatchRejectedException(UiText.T("这个存档不能生成物品。", "這個存檔不能生成物品。", "This save does not support generating items."));
        var applied = generator.Generate(request);
        var expectedPlayerCount = request.ToStash ? source.PlayerItemCount : source.PlayerItemCount + request.Copies;
        var expectedStashCount = request.ToStash ? source.StashItemCount + request.Copies : source.StashItemCount;
        if (generator.PlayerItemCount != expectedPlayerCount
            || (request.ToStash && generator.StashItemCount != expectedStashCount)
            || applied.Copies != request.Copies
            || applied.Records.Count != request.Copies)
            throw new PatchRejectedException(UiText.T("生成结果与请求的件数不一致。", "生成結果與請求的件數不一致。", "The generated result does not match the requested count."));
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
                    throw new PatchRejectedException(UiText.T("回读校验失败：无法作为物品生成文档加载。", "回讀校驗失敗：無法作為物品生成文件載入。", "Read-back check failed: the written save could not be loaded for item generation."));
                if (request.ToStash)
                {
                    if (check.PlayerItemCount != expectedPlayerCount || check.StashItemCount != expectedStashCount)
                        throw new PatchRejectedException(UiText.T("回读校验失败：储藏箱物品数没有增加对应的件数。", "回讀校驗失敗：儲藏箱物品數沒有增加對應的件數。", "Read-back check failed: the stash item count did not increase by the expected amount."));
                }
                else
                {
                    if (check.PlayerItemCount != expectedPlayerCount)
                        throw new PatchRejectedException(UiText.T("回读校验失败：玩家物品数没有增加对应的件数。", "回讀校驗失敗：玩家物品數沒有增加對應的件數。", "Read-back check failed: the player item count did not increase by the expected amount."));
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
        long writtenLength;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                working.Save(stream);
                writtenLength = stream.Length;
            }

            var reloaded = adapter.Load(temporary);
            VerifyReloaded(adapter, reloaded, before, ambiguous, results);
            verifyStrict(reloaded);

            observed.Refresh();
            if (!observed.Exists || observed.Length != observedLength || observed.LastWriteTimeUtc != observedWriteTime)
                throw new SaveChangedDuringPatchException();

            File.Replace(temporary, saveFilePath, backup);
            SaveFileWrites.Record();
            written = File.GetLastWriteTimeUtc(saveFilePath);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }

        return new PatchReport(PatchOutcome.Patched, results, backup, clock.ElapsedMilliseconds, written) { WrittenLength = writtenLength };
    }

    public static RestoreReport Restore(IGameAdapter adapter, string saveFilePath)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        var clock = Stopwatch.StartNew();
        var backup = saveFilePath + ".bak";
        if (!File.Exists(backup))
            throw new PatchRejectedException(UiText.T("找不到备份文件。", "找不到備份檔案。", "Backup file not found."));
        _ = adapter.Load(backup);

        var temporary = saveFilePath + ".tmp";
        if (File.Exists(temporary))
            File.Delete(temporary);
        try
        {
            File.Copy(backup, temporary);
            File.Replace(temporary, saveFilePath, destinationBackupFileName: null);
            SaveFileWrites.Record();
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
                throw new PatchRejectedException(UiText.T($"未知资源 {change.Id.Value}。", $"未知資源 {change.Id.Value}。", $"Unknown resource {change.Id.Value}."));
            if (change.TargetQuantity < 0 || change.TargetQuantity > resource.Max)
            {
                throw new PatchRejectedException(
                    UiText.T($"资源 {change.Id.Value} 的目标值 {change.TargetQuantity} 超出安全范围 0..{resource.Max}。", $"資源 {change.Id.Value} 的目標值 {change.TargetQuantity} 超出安全範圍 0..{resource.Max}。", $"Target value {change.TargetQuantity} for resource {change.Id.Value} is outside the safe range 0..{resource.Max}."));
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
                    UiText.T($"回读校验失败：{resource.Id.Value} 期望 {QuantityText(expected, "未持有")}，实际 {QuantityText(actual, "未持有")}。", $"回讀校驗失敗：{resource.Id.Value} 期望 {QuantityText(expected, "未持有")}，實際 {QuantityText(actual, "未持有")}。", $"Read-back check failed: {resource.Id.Value} expected {QuantityText(expected, "not held")}, got {QuantityText(actual, "not held")}."));
            }
        }
    }

    private static string QuantityText(long? quantity, string missing) =>
        quantity?.ToString(CultureInfo.InvariantCulture) ?? missing;
}
