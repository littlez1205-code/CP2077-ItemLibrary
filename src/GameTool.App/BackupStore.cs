using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GameTool.Core;

namespace GameTool.App;

public sealed record BackupFileEntry(string Name, string Sha256, long Size);

public sealed record BackupManifest(string SourceSlot, DateTime TimeUtc, IReadOnlyList<BackupFileEntry> Files);

public sealed record BackupEntry(string Id, string SlotName, DateTime TimeUtc, long Size);

public sealed record BackupResourceView(string Id, string NameZh, long? BackupQuantity, long? CurrentQuantity);

public sealed record BackupView(string SlotName, DateTime TimeUtc, long Size, IReadOnlyList<BackupResourceView> Resources);

public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupStore(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTool",
            "backups");
    }

    public string Root { get; }

    public BackupEntry Add(IGameAdapter adapter, string slotDirectory)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotDirectory);
        var slot = FindSlot(adapter, slotDirectory);
        if (!adapter.IsComplete(slot.SaveFilePath))
            throw new PatchRejectedException(UiText.T("存档不完整或无法读取。", "存檔不完整或無法讀取。", "The save is incomplete or cannot be read."));
        try
        {
            _ = adapter.Load(slot.SaveFilePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or UnsupportedSaveException)
        {
            throw new PatchRejectedException(UiText.T("存档不完整或无法读取。", "存檔不完整或無法讀取。", "The save is incomplete or cannot be read."));
        }

        var slotName = slot.SlotName;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var destination = Path.Combine(Root, adapter.GameId, slotName, stamp);
        var suffix = 2;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(Root, adapter.GameId, slotName, stamp + "-" + suffix);
            suffix++;
        }

        Directory.CreateDirectory(destination);
        var files = new List<BackupFileEntry>();
        foreach (var source in Directory.EnumerateFiles(slot.SlotDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(source);
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = Path.GetRelativePath(slot.SlotDirectory, source);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
            files.Add(new BackupFileEntry(relative.Replace('\\', '/'), Hash(target), new FileInfo(target).Length));
        }

        var copiedSave = Path.Combine(destination, Path.GetFileName(slot.SaveFilePath));
        try
        {
            if (!adapter.IsComplete(copiedSave))
                throw new PatchRejectedException(UiText.T("存档不完整或无法读取。", "存檔不完整或無法讀取。", "The save is incomplete or cannot be read."));
            _ = adapter.Load(copiedSave);
        }
        catch (Exception ex)
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            if (ex is PatchRejectedException)
                throw;
            throw new PatchRejectedException(UiText.T("存档不完整或无法读取。", "存檔不完整或無法讀取。", "The save is incomplete or cannot be read."));
        }

        var manifest = new BackupManifest(Path.GetFullPath(slot.SaveFilePath), DateTime.UtcNow, files);
        File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return ToEntry(destination, manifest);
    }

    public IReadOnlyList<BackupEntry> List(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var gameRoot = Path.Combine(Root, gameId);
        if (!Directory.Exists(gameRoot))
            return [];
        var entries = new List<BackupEntry>();
        foreach (var manifestPath in Directory.EnumerateFiles(gameRoot, "manifest.json", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(manifestPath)!;
            var manifest = ReadManifest(directory);
            entries.Add(ToEntry(directory, manifest));
        }

        return entries
            .OrderByDescending(entry => entry.TimeUtc)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public BackupView Describe(IGameAdapter adapter, string backupId, string saveRoot)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        var directory = Resolve(backupId);
        var manifest = ReadManifest(directory);
        var backupFile = Confine(directory, Path.GetFileName(manifest.SourceSlot), UiText.T("备份文件路径超出备份目录。", "備份檔案路徑超出備份目錄。", "A backup file path points outside the backup folder."));
        var backupInfo = SaveQueryService.GetInfo(adapter, backupFile);
        var current = ReadCurrent(adapter, manifest.SourceSlot, saveRoot);
        var resources = backupInfo.Resources.Select(resource => new BackupResourceView(
            resource.Id,
            resource.NameZh,
            resource.Quantity,
            current is not null && current.TryGetValue(resource.Id, out var quantity) ? quantity : null)).ToArray();
        var entry = ToEntry(directory, manifest);
        return new BackupView(entry.SlotName, entry.TimeUtc, entry.Size, resources);
    }

    public void Restore(IGameAdapter adapter, string backupId, string saveRoot, Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        ArgumentNullException.ThrowIfNull(isEnabled);
        var directory = Resolve(backupId);
        var manifest = ReadManifest(directory);
        var source = Path.GetFullPath(manifest.SourceSlot);
        var root = Path.GetFullPath(saveRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new PatchRejectedException(UiText.T("备份的目标槽位不在存档根目录内。", "備份的目標槽位不在存檔根目錄內。", "The backup's target slot is not inside the save folder."));
        var known = adapter.DiscoverSaves(root)
            .Any(slot => Path.GetFullPath(slot.SaveFilePath).Equals(source, StringComparison.OrdinalIgnoreCase));
        if (!known)
            throw new PatchRejectedException(UiText.T("备份的目标槽位不是当前发现的存档。", "備份的目標槽位不是當前發現的存檔。", "The backup's target slot is not one of the saves currently found."));
        if (isEnabled(source))
            throw new PatchRejectedException(UiText.T("该存档正处于使能状态，拒绝恢复。", "該存檔正處於使能狀態，拒絕恢復。", "Auto-apply is enabled for this save, so it cannot be restored."));

        var slotDirectory = Path.GetDirectoryName(source)!;
        var staged = new List<(string Temporary, string Destination)>();
        try
        {
            foreach (var file in manifest.Files)
            {
                var from = Confine(directory, file.Name, UiText.T("备份文件路径超出备份目录。", "備份檔案路徑超出備份目錄。", "A backup file path points outside the backup folder."));
                var destination = Confine(slotDirectory, file.Name, UiText.T("备份文件路径超出槽位目录。", "備份檔案路徑超出槽位目錄。", "A backup file path points outside the save slot folder."));
                var temporary = destination + ".tmp";
                if (File.Exists(temporary))
                    File.Delete(temporary);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(from, temporary, overwrite: false);
                var hash = Hash(temporary);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) || new FileInfo(temporary).Length != file.Size)
                    throw new PatchRejectedException(UiText.T($"备份文件 {file.Name} 的哈希与清单不一致。", $"備份檔案 {file.Name} 的雜湊與清單不一致。", $"The hash of backup file {file.Name} does not match the manifest."));
                staged.Add((temporary, destination));
            }

            var saveName = Path.GetFileName(source);
            foreach (var item in staged.OrderBy(item => Path.GetFileName(item.Destination).Equals(saveName, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                if (File.Exists(item.Destination))
                    File.Replace(item.Temporary, item.Destination, destinationBackupFileName: null);
                else
                    File.Move(item.Temporary, item.Destination);
                SaveFileWrites.Record();
            }
        }
        catch
        {
            foreach (var item in staged)
            {
                if (File.Exists(item.Temporary))
                    File.Delete(item.Temporary);
            }

            throw;
        }
    }

    public void Delete(string backupId)
    {
        var directory = Resolve(backupId);
        if (!File.Exists(Path.Combine(directory, "manifest.json")))
            throw new PatchRejectedException(UiText.T("找不到备份。", "找不到備份。", "Backup not found."));
        Directory.Delete(directory, recursive: true);
    }

    private static Dictionary<string, long?>? ReadCurrent(IGameAdapter adapter, string sourceSlot, string saveRoot)
    {
        string source;
        try
        {
            source = Path.GetFullPath(sourceSlot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var root = Path.GetFullPath(saveRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        var known = adapter.DiscoverSaves(Path.GetFullPath(saveRoot))
            .Any(slot => Path.GetFullPath(slot.SaveFilePath).Equals(source, StringComparison.OrdinalIgnoreCase));
        if (!known)
            return null;
        if (adapter is ISaveSupportProbe probe && !probe.Probe(source).Supported)
            return null;
        try
        {
            return SaveQueryService.GetInfo(adapter, source).Resources.ToDictionary(resource => resource.Id, resource => resource.Quantity);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or UnsupportedSaveException)
        {
            return null;
        }
    }

    private static SaveSlotInfo FindSlot(IGameAdapter adapter, string slotDirectory)
    {
        var full = Path.GetFullPath(slotDirectory);
        var matches = new List<SaveSlotInfo>();
        var parent = Path.GetDirectoryName(full);
        if (parent is not null)
            matches.AddRange(adapter.DiscoverSaves(parent));
        matches.AddRange(adapter.DiscoverSaves(full));
        var slot = matches.FirstOrDefault(item =>
            Path.GetFullPath(item.SlotDirectory).Equals(full, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
            throw new PatchRejectedException(UiText.T("找不到要备份的槽位。", "找不到要備份的槽位。", "The save slot to back up was not found."));
        return slot;
    }

    private static string Confine(string root, string relative, string message)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new PatchRejectedException(message);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new PatchRejectedException(message);
        return full;
    }

    private string Resolve(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId))
            throw new PatchRejectedException(UiText.T("缺少备份编号。", "缺少備份編號。", "Missing backup id."));
        if (backupId.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(backupId))
            throw new PatchRejectedException(UiText.T("备份路径超出备份根目录。", "備份路徑超出備份根目錄。", "The backup path points outside the backup folder."));
        var combined = Path.GetFullPath(Path.Combine(Root, backupId.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) || combined.Equals(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            throw new PatchRejectedException(UiText.T("备份路径超出备份根目录。", "備份路徑超出備份根目錄。", "The backup path points outside the backup folder."));
        return combined;
    }

    private static BackupManifest ReadManifest(string directory)
    {
        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path))
            throw new PatchRejectedException(UiText.T("找不到备份。", "找不到備份。", "Backup not found."));
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), JsonOptions);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.SourceSlot))
            throw new PatchRejectedException(UiText.T("备份清单无法读取。", "備份清單無法讀取。", "The backup manifest cannot be read."));
        return manifest;
    }

    private BackupEntry ToEntry(string directory, BackupManifest manifest)
    {
        var id = Path.GetRelativePath(Root, directory).Replace('\\', '/');
        var size = manifest.Files.Sum(file => file.Size);
        var slotName = Path.GetFileName(Path.GetDirectoryName(directory)) ?? "";
        return new BackupEntry(id, slotName, manifest.TimeUtc, size);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
