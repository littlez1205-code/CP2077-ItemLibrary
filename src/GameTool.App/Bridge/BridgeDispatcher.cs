using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GameTool.Core;

namespace GameTool.App;

public sealed class BridgeArgumentException : Exception
{
    public BridgeArgumentException(string message) : base(message)
    {
    }
}

/// <summary>
/// 页面消息桥。输入请求 JSON，返回带 id 的标准信封。推送是 <c>{event, data}</c>。本类不抛出到调用方。
/// </summary>
public sealed class BridgeDispatcher : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AdapterRegistry _registry;
    private readonly string? _saveRootOverride;
    private readonly Action<string> _push;
    private readonly BackupStore _backups;
    private readonly EnableService _enable;
    private readonly LibraryService _library;
    private readonly SettingsStore? _settings;
    private readonly string? _gameDirOverride;
    private readonly Func<string, string?, string?>? _pickFolder;
    private readonly PathDisplay _display;
    private readonly ConcurrentDictionary<string, GamePaths> _paths = new(StringComparer.Ordinal);
    private readonly string _version;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly Lock _rootWatchGate = new();
    private readonly Dictionary<string, SaveRootWatcher> _rootWatchers = new(StringComparer.Ordinal);
    private int? _rootWatchDebounce;
    private bool _disposed;
    private volatile bool _helpShownInMemory;
    private volatile string _languageInMemory = "zh-cn";

    /// <param name="saveRootOverride">命令行 --save-root（自检用），优先于设置文件。</param>
    /// <param name="settings">用户设置（D020）；桌面端必传，自检传临时文件，不碰真实设置。</param>
    /// <param name="gameDirOverride">命令行 --game-dir（自检用），优先于设置文件。</param>
    /// <param name="pickFolder">原生选择文件夹（标题, 初始目录）→ 选中的目录，取消为 null。页面只能拿到选中的路径。</param>
    /// <param name="display">
    /// 路径缩写（D021）：所有应答、推送和错误信息里的字符串都按它把用户目录缩写成 %USERPROFILE% / %LOCALAPPDATA%，
    /// 页面送回的缩写路径（槽位、设置路径）再按它还原。默认用当前用户的目录；测试可以换成假的用户目录。
    /// </param>
    /// <param name="appVersion">「关于」页显示的版本号（D022），默认取入口程序集的版本（<see cref="AppVersion.Current"/>）。</param>
    /// <param name="urlLauncher">
    /// app.openAuthorPage 用的浏览器启动器（D024），只会以 <see cref="AuthorPage.Url"/> 调用。
    /// 不传时该方法报错、不打开任何东西；桌面端正常运行传 <see cref="ShellUrlLauncher"/>，测试和自检传假的。
    /// </param>
    public BridgeDispatcher(
        AdapterRegistry registry,
        string? saveRootOverride,
        Action<string> push,
        string? backupRoot = null,
        string? skinRoot = null,
        SettingsStore? settings = null,
        string? gameDirOverride = null,
        Func<string, string?, string?>? pickFolder = null,
        PathDisplay? display = null,
        string? appVersion = null,
        IUrlLauncher? urlLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(push);
        _registry = registry;
        _saveRootOverride = string.IsNullOrWhiteSpace(saveRootOverride) ? null : saveRootOverride;
        _push = push;
        _backups = new BackupStore(backupRoot);
        Skins = new SkinStore(registry, skinRoot);
        _enable = new EnableService(_backups, PushEnable);
        _library = new LibraryService(_backups, _enable);
        _settings = settings;
        _gameDirOverride = string.IsNullOrWhiteSpace(gameDirOverride) ? null : gameDirOverride;
        _pickFolder = pickFolder;
        _display = display ?? PathDisplay.Current;
        _version = string.IsNullOrWhiteSpace(appVersion) ? AppVersion.Current : appVersion.Trim();
        _urlLauncher = urlLauncher;
        var lang = CurrentLanguage();
        foreach (var adapter in registry.All)
        {
            if (adapter is IGamePathSetup setup)
            {
                setup.UseLanguage(lang);
                if (_settings is not null || _gameDirOverride is not null)
                    ApplyGameDirectory(setup, PathsOf(adapter).GameDir);
            }
        }
    }

    public SkinStore Skins { get; }

    public string Dispatch(string requestJson)
    {
        string? id = null;
        string? game = null;
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new BridgeArgumentException("请求必须是 JSON 对象。");
            id = ReadString(root, "id");
            var method = ReadString(root, "method");
            if (string.IsNullOrWhiteSpace(method))
                throw new BridgeArgumentException("缺少 method。");
            var parameters = root.TryGetProperty("params", out var value) ? value : default;
            if (parameters.ValueKind is JsonValueKind.Undefined)
                parameters = default;
            game = parameters.ValueKind == JsonValueKind.Object ? ReadString(parameters, "gameId") : null;
            return method switch
            {
                "app.ping" => Ok(id, null, new { Ok = true }),
                "app.info" => AppInfo(id),
                "app.markHelpShown" => MarkHelpShown(id),
                "app.setLanguage" => SetLanguage(id, parameters),
                "app.openAuthorPage" => OpenAuthorPage(id),
                "app.games" => Games(id),
                "saves.list" => ListSaves(id, parameters),
                "saves.info" => Info(id, parameters),
                "enable.start" => EnableStart(id, parameters),
                "enable.update" => EnableUpdate(id, parameters),
                "enable.stop" => EnableStop(id),
                "enable.status" => EnableStatus(id),
                "backup.add" => BackupAdd(id, parameters),
                "backup.list" => BackupList(id, parameters),
                "backup.info" => BackupInfo(id, parameters),
                "backup.restore" => BackupRestore(id, parameters),
                "backup.delete" => BackupDelete(id, parameters),
                "theme.get" => ThemeGet(id, parameters),
                "skin.get" => SkinGet(id, parameters),
                "skin.import" => SkinImport(id, parameters),
                "skin.setOverlay" => SkinSetOverlay(id, parameters),
                "skin.clear" => SkinClear(id, parameters),
                "clone.list" => CloneList(id, parameters),
                "clone.run" => CloneRun(id, parameters),
                "generate.catalog" => GenerateCatalog(id, parameters),
                "generate.run" => GenerateRun(id, parameters),
                "library.catalog" => LibraryCatalog(id, parameters),
                "library.obtain" => LibraryObtain(id, parameters),
                "library.batchObtain" => LibraryBatchObtain(id, parameters),
                "settings.get" => SettingsGet(id, parameters),
                "settings.validate" => SettingsValidate(id, parameters),
                "settings.pickFolder" => SettingsPickFolder(id, parameters),
                "settings.save" => SettingsSave(id, parameters),
                _ => throw new BridgeArgumentException($"未知方法 {method}。")
            };
        }
        catch (BridgeArgumentException ex)
        {
            return Fail(id, game, "bad_args", ex.Message);
        }
        catch (JsonException)
        {
            return Fail(id, game, "bad_args", "请求不是合法 JSON。");
        }
        catch (Exception ex)
        {
            var mapped = ErrorMapper.Map(ex);
            return Fail(id, game, mapped.Code, mapped.Message);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        lock (_rootWatchGate)
        {
            _disposed = true;
            foreach (var watcher in _rootWatchers.Values)
                watcher.Dispose();
            _rootWatchers.Clear();
        }

        await _enable.DisposeAsync();
    }

    /// <summary>
    /// 存档列表自动刷新（T030，桌面端启用）：监视各游戏当前生效的存档根目录，游戏存档、槽位目录增删改名后，
    /// 去抖合并推送 <c>{"event":"saves.changed","data":{"gameId":…}}</c>，页面收到后静默刷新。
    /// 存档根目录在设置里改了，监视跟着切换；目录不存在时不监视、不报错；随本对象释放。
    /// </summary>
    public void WatchSaveRoots(int debounceMilliseconds = SaveRootWatcher.DefaultDebounceMilliseconds)
    {
        if (debounceMilliseconds is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(debounceMilliseconds), "防抖毫秒的安全范围是 0..60000。");
        lock (_rootWatchGate)
        {
            if (_disposed)
                return;
            _rootWatchDebounce = debounceMilliseconds;
            foreach (var adapter in _registry.All)
                RewatchLocked(adapter);
        }
    }

    /// <summary>某个游戏的存档根目录当前是否在监视（测试和自检用）。</summary>
    public bool IsWatchingSaveRoot(string gameId)
    {
        lock (_rootWatchGate)
            return _rootWatchers.TryGetValue(gameId, out var watcher) && watcher.Active;
    }

    private void Rewatch(IGameAdapter adapter)
    {
        lock (_rootWatchGate)
        {
            if (!_disposed && _rootWatchDebounce is not null)
                RewatchLocked(adapter);
        }
    }

    private void RewatchLocked(IGameAdapter adapter)
    {
        var root = PathsOf(adapter).SaveRoot;
        if (_rootWatchers.TryGetValue(adapter.GameId, out var current))
        {
            if (!string.IsNullOrWhiteSpace(root) && current.Active && SamePath(current.Root, root))
                return;
            current.Dispose();
            _rootWatchers.Remove(adapter.GameId);
        }

        if (string.IsNullOrWhiteSpace(root))
            return;
        var gameId = adapter.GameId;
        _rootWatchers[gameId] = new SaveRootWatcher(root, () => PushSavesChanged(gameId), _rootWatchDebounce!.Value, SaveFileNameOf(adapter));
    }

    private void PushSavesChanged(string gameId) =>
        _push(JsonSerializer.Serialize(new { Event = "saves.changed", Data = new { GameId = gameId } }, JsonOptions));

    /// <summary>槽位里的存档文件名：取适配器监听通配的最后一段（2077 为 sav.dat），带通配符或没有时用 sav.dat。</summary>
    private static string SaveFileNameOf(IGameAdapter adapter)
    {
        var glob = adapter.WatchSpec.IncludeGlobs.FirstOrDefault();
        var name = string.IsNullOrWhiteSpace(glob) ? null : glob.Replace('\\', '/').Split('/')[^1];
        return string.IsNullOrWhiteSpace(name) || name.Contains('*') || name.Contains('?') ? SaveRootWatcher.DefaultSaveFileName : name;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string CurrentLanguage() => _settings?.GetLanguage() ?? _languageInMemory;

    /// <summary>
    /// 程序信息（D022）：版本号，以及「说明」是否已经自动弹出过。标记存在设置文件里（程序级，不分游戏）；
    /// 没有设置文件时（命令行等）只记在内存里。
    /// </summary>
    private string AppInfo(string? id) => Ok(id, null, new
    {
        Version = _version,
        HelpShown = _settings?.GetHelpShown() ?? _helpShownInMemory,
        Language = CurrentLanguage()
    });

    /// <summary>「说明」已经自动弹出过，以后不再自动弹出。</summary>
    private string MarkHelpShown(string? id)
    {
        if (_settings is null)
            _helpShownInMemory = true;
        else
            _settings.SetHelpShown(true);
        return AppInfo(id);
    }

    private string SetLanguage(string? id, JsonElement parameters)
    {
        var raw = parameters.ValueKind == JsonValueKind.Object ? ReadString(parameters, "language") : null;
        var norm = SettingsStore.NormalizeLanguage(raw);
        if (_settings is null)
            _languageInMemory = norm;
        else
            _settings.SetLanguage(norm);
        foreach (var adapter in _registry.All)
        {
            if (adapter is IGamePathSetup setup)
                setup.UseLanguage(norm);
        }

        return AppInfo(id);
    }

    /// <summary>
    /// 用系统浏览器打开作者 B 站主页（D024）。不读任何参数：地址固定为 <see cref="AuthorPage.Url"/>，页面送来的 url 一律忽略。
    /// </summary>
    private string OpenAuthorPage(string? id)
    {
        if (_urlLauncher is null)
            return Fail(id, null, "failed", "当前环境不能打开浏览器，请手动访问：" + AuthorPage.Url);
        try
        {
            _urlLauncher.Open(AuthorPage.Url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Fail(id, null, "failed", "无法打开浏览器，请手动访问：" + AuthorPage.Url);
        }

        return Ok(id, null, new { Opened = true });
    }

    private string Games(string? id)
    {
        var games = _registry.All
            .OrderBy(adapter => adapter.GameId, StringComparer.Ordinal)
            .Select(adapter => new { Id = adapter.GameId, adapter.DisplayName })
            .ToArray();
        return Ok(id, null, games);
    }

    private string ListSaves(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slots = SaveQueryService.ListSlots(adapter, Root(adapter))
            .Select(slot =>
            {
                var support = adapter is ISaveSupportProbe probe
                    ? probe.Probe(slot.SaveFilePath)
                    : new SaveSupport(true, null);
                return new
                {
                    Name = slot.SlotName,
                    Time = slot.LastWriteTimeUtc,
                    Group = slot.Group,
                    Enabled = _enable.IsEnabled(slot.SaveFilePath),
                    support.Supported,
                    support.Reason,
                    Slot = slot.SaveFilePath
                };
            })
            .ToArray();
        return Ok(id, adapter.GameId, slots);
    }

    private string Info(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var info = SaveQueryService.GetInfo(adapter, slot);
        var resources = info.Resources.Select(resource =>
        {
            var descriptor = adapter.Resources.First(item => item.Id.Value == resource.Id);
            return new
            {
                resource.Id,
                resource.NameZh,
                resource.Quantity,
                resource.Stacks,
                resource.Ambiguous,
                descriptor.Max,
                descriptor.DefaultFill,
                descriptor.Group
            };
        }).ToArray();
        return Ok(id, adapter.GameId, new
        {
            info.GameVersion,
            info.SaveVersion,
            info.Diagnostics,
            Resources = resources
        });
    }

    private string EnableStart(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var changes = ReadChanges(parameters, adapter);
        var backupFirst = ReadBool(parameters, "backupFirst");
        var backupId = _enable.Start(adapter, slot, changes, backupFirst, Root(adapter));
        return Ok(id, adapter.GameId, StatusPayload(backupId));
    }

    private string EnableUpdate(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        _enable.Update(ReadChanges(parameters, adapter));
        return Ok(id, adapter.GameId, StatusPayload(null));
    }

    private string EnableStop(string? id)
    {
        _enable.Stop();
        return Ok(id, null, StatusPayload(null));
    }

    private string EnableStatus(string? id) => Ok(id, null, StatusPayload(null));

    private string BackupAdd(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var entry = _backups.Add(adapter, Path.GetDirectoryName(slot)!);
        return Ok(id, adapter.GameId, entry);
    }

    private string BackupList(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        return Ok(id, adapter.GameId, _backups.List(adapter.GameId));
    }

    private string BackupInfo(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var backupId = ReadString(parameters, "backupId");
        if (string.IsNullOrWhiteSpace(backupId))
            throw new BridgeArgumentException("缺少 backupId。");
        return Ok(id, adapter.GameId, _backups.Describe(adapter, backupId, Root(adapter)));
    }

    private string BackupRestore(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var backupId = ReadString(parameters, "backupId");
        if (string.IsNullOrWhiteSpace(backupId))
            throw new BridgeArgumentException("缺少 backupId。");
        _backups.Restore(adapter, backupId, Root(adapter), _enable.IsEnabled);
        return Ok(id, adapter.GameId, new { Restored = true });
    }

    private string BackupDelete(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var backupId = ReadString(parameters, "backupId");
        if (string.IsNullOrWhiteSpace(backupId))
            throw new BridgeArgumentException("缺少 backupId。");
        _backups.Delete(backupId);
        return Ok(id, adapter.GameId, new { Deleted = true });
    }

    private object StatusPayload(string? backupId)
    {
        var status = _enable.Status;
        return new
        {
            BackupId = backupId,
            status.Slot,
            status.SlotName,
            ChangeCount = status.Changes.Count,
            Changes = status.Changes.Select(change => new { Id = change.Id.Value, Value = change.TargetQuantity }).ToArray(),
            status.LastOutcome,
            status.LastElapsedMilliseconds,
            status.LastAtUtc
        };
    }

    private void PushEnable(EnableNotice notice)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Event = notice.Event,
            Data = new
            {
                Slot = _display.MaskOrNull(notice.Slot),
                Message = _display.MaskOrNull(notice.Message),
                notice.ElapsedMilliseconds
            }
        }, JsonOptions);
        _push(payload);
    }

    private string ThemeGet(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        return Ok(id, adapter.GameId, ThemeCatalog.Load(adapter));
    }

    private string SkinGet(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        return Ok(id, adapter.GameId, Skins.Get(adapter.GameId));
    }

    private string SkinImport(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var fileName = ReadString(parameters, "fileName");
        var encoded = ReadString(parameters, "dataBase64");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new BridgeArgumentException("缺少 fileName。");
        if (string.IsNullOrWhiteSpace(encoded))
            throw new BridgeArgumentException("缺少 dataBase64。");
        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new BridgeArgumentException("dataBase64 不是合法的 Base64。");
        }

        return Ok(id, adapter.GameId, Skins.ImportImage(adapter.GameId, fileName, data));
    }

    private string SkinSetOverlay(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("value", out var raw) || raw.ValueKind != JsonValueKind.Number || !raw.TryGetDouble(out var value))
            throw new BridgeArgumentException("缺少 value。");
        return Ok(id, adapter.GameId, Skins.SetOverlay(adapter.GameId, value));
    }

    private string SkinClear(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        return Ok(id, adapter.GameId, Skins.ClearImage(adapter.GameId));
    }

    private IGameAdapter RequireGame(JsonElement parameters)
    {
        var gameId = parameters.ValueKind == JsonValueKind.Object ? ReadString(parameters, "gameId") : null;
        if (string.IsNullOrWhiteSpace(gameId))
            throw new BridgeArgumentException("缺少 gameId。");
        try
        {
            return _registry.GetRequired(gameId);
        }
        catch (KeyNotFoundException)
        {
            throw new BridgeArgumentException($"未知游戏 {gameId}。");
        }
    }

    private string CloneList(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var document = adapter.Load(slot);
        if (document is not IItemCloneCapable cloneable)
            throw new BridgeArgumentException("这个存档不能复制装备。");
        return Ok(id, adapter.GameId, new
        {
            cloneable.NamesAvailable,
            Message = cloneable.NamesMessage,
            Items = cloneable.ListCloneable()
        });
    }

    private string CloneRun(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var key = ReadString(parameters, "key");
        if (string.IsNullOrWhiteSpace(key))
            throw new BridgeArgumentException("缺少 key。");
        var copies = parameters.TryGetProperty("copies", out var value) && value.TryGetInt32(out var parsed) ? parsed : 1;
        var backupFirst = !parameters.TryGetProperty("backupFirst", out var backup) || backup.ValueKind != JsonValueKind.False;
        var report = CloneService.Run(adapter, slot, new CloneRequest(key, copies), backupFirst, _backups, _enable);
        return Ok(id, adapter.GameId, new
        {
            report.Patch.Outcome,
            report.Seeds,
            report.AddedBytes,
            Copies = copies
        });
    }

    /// <summary>目录有几千条，只按页返回（take 默认 50，最多 200）。</summary>
    private string GenerateCatalog(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var kind = ReadString(parameters, "kind");
        var query = ReadString(parameters, "query");
        var skip = ReadInt(parameters, "skip", 0);
        var take = ReadInt(parameters, "take", GenerateService.DefaultTake);
        if (skip < 0)
            throw new BridgeArgumentException("skip 不能是负数。");
        if (take is < 1 or > GenerateService.MaxTake)
            throw new BridgeArgumentException($"take 必须在 1 到 {GenerateService.MaxTake} 之间。");
        return Ok(id, adapter.GameId, GenerateService.Catalog(adapter, slot, string.IsNullOrEmpty(kind) ? null : kind, query, skip, take));
    }

    /// <summary>
    /// 物品库卡片（T023）：同名合并后按卡片分页，参数同 generate.catalog。
    /// T024：kindFallback 为 true 时，kind 为空或未知就改用第一个分类（返回的 kind 是实际分类）；有 query 或 ownership 时附带 kindCounts。
    /// </summary>
    private string LibraryCatalog(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var kind = ReadString(parameters, "kind");
        var query = ReadString(parameters, "query");
        var ownership = ReadString(parameters, "ownership");
        var skip = ReadInt(parameters, "skip", 0);
        var take = ReadInt(parameters, "take", GenerateService.DefaultTake);
        if (skip < 0)
            throw new BridgeArgumentException("skip 不能是负数。");
        if (take is < 1 or > GenerateService.MaxTake)
            throw new BridgeArgumentException($"take 必须在 1 到 {GenerateService.MaxTake} 之间。");
        var kindFallback = parameters.TryGetProperty("kindFallback", out var fallback) && fallback.ValueKind == JsonValueKind.True;
        return Ok(id, adapter.GameId, GenerateService.Groups(adapter, slot, string.IsNullOrEmpty(kind) ? null : kind, query, skip, take, kindFallback, ownership));
    }

    private string GenerateRun(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var target = ReadString(parameters, "target");
        if (string.IsNullOrWhiteSpace(target))
            throw new BridgeArgumentException("缺少 target。");
        var copies = ReadInt(parameters, "copies", 1);
        var backupFirst = !parameters.TryGetProperty("backupFirst", out var backup) || backup.ValueKind != JsonValueKind.False;
        var toStash = parameters.TryGetProperty("toStash", out var stashProp) && stashProp.ValueKind is JsonValueKind.True;
        var report = GenerateService.Run(adapter, slot, new GenerateRequest(target, copies, toStash), backupFirst, _backups, _enable);
        return Ok(id, adapter.GameId, new
        {
            report.Patch.Outcome,
            Target = target,
            Copies = copies,
            ToStash = toStash,
            report.Seeds,
            report.AddedBytes,
            report.TemplateKey,
            report.TemplateLabel
        });
    }

    /// <summary>物品库「获得」：装备 1 件，堆叠物品按 quantity（默认 1）加到现有数量或插入新堆叠；不做备份（D019）。</summary>
    private string LibraryObtain(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        var target = ReadString(parameters, "target");
        if (string.IsNullOrWhiteSpace(target))
            throw new BridgeArgumentException("缺少 target。");
        long? quantity = null;
        if (parameters.TryGetProperty("quantity", out var raw) && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out var parsed))
                throw new BridgeArgumentException("quantity 必须是整数。");
            quantity = parsed;
        }
        var toStash = parameters.TryGetProperty("toStash", out var stashProp) && stashProp.ValueKind is JsonValueKind.True;

        return Ok(id, adapter.GameId, _library.Obtain(adapter, slot, target, quantity, toStash));
    }

    /// <summary>物品库批量「获得」：多件物品一次性写入，支持智能目标位置选择。</summary>
    private string LibraryBatchObtain(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var slot = RequireSlot(parameters, adapter);
        if (!parameters.TryGetProperty("items", out var itemsElem) || itemsElem.ValueKind != JsonValueKind.Array)
            throw new BridgeArgumentException("缺少 items 列表。");

        var list = new List<BatchItemRequest>();
        foreach (var elem in itemsElem.EnumerateArray())
        {
            var target = ReadString(elem, "target");
            if (string.IsNullOrWhiteSpace(target))
                continue;
            long? qty = null;
            if (elem.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number && q.TryGetInt64(out var parsedQty))
                qty = parsedQty;
            bool? toStash = null;
            if (elem.TryGetProperty("toStash", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False)
                toStash = s.GetBoolean();
            list.Add(new BatchItemRequest(target, qty, toStash));
        }

        if (list.Count == 0)
            throw new BridgeArgumentException("未选择任何要添加的物品。");

        var destMode = ReadString(parameters, "destinationMode") ?? "smart";
        var report = _library.BatchObtain(adapter, slot, list, destMode);
        return Ok(id, adapter.GameId, report);
    }

    // ---------- 路径设置（D020） ----------

    private const string SaveRootKind = "saveRoot";
    private const string GameDirKind = "gameDir";

    /// <summary>当前生效的路径：命令行覆盖优先，其次是设置文件；第一次用到某个游戏时读一次设置文件。</summary>
    private GamePaths PathsOf(IGameAdapter adapter) =>
        _paths.GetOrAdd(adapter.GameId, gameId =>
        {
            var stored = _settings?.Get(gameId) ?? GamePaths.Empty;
            return new GamePaths(_saveRootOverride ?? stored.SaveRoot, _gameDirOverride ?? stored.GameDir);
        });

    private static void ApplyGameDirectory(IGamePathSetup setup, string? gameDir)
    {
        var check = setup.CheckGameDirectory(gameDir);
        setup.UseGameDirectory(check.Ok ? check.Path : null);
    }

    private static PathCheck CheckPath(IGameAdapter adapter, string kind, string? path)
    {
        if (adapter is IGamePathSetup setup)
            return kind == SaveRootKind ? setup.CheckSaveRoot(path) : setup.CheckGameDirectory(path);
        if (kind == GameDirKind)
            return new PathCheck(true, path, "这个游戏不需要游戏目录。");
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(false, null, "请选择存档目录。");
        return Directory.Exists(path)
            ? new PathCheck(true, Path.GetFullPath(path), "目录可用。")
            : new PathCheck(false, null, "目录不存在：" + path);
    }

    private static object CheckPayload(PathCheck check) => new
    {
        check.Ok,
        check.Path,
        check.Message,
        check.Warning,
        check.Detected,
        check.Expansions
    };

    private object SettingsPayload(IGameAdapter adapter)
    {
        var paths = PathsOf(adapter);
        var saveCheck = CheckPath(adapter, SaveRootKind, paths.SaveRoot);
        var gameCheck = CheckPath(adapter, GameDirKind, paths.GameDir);
        var setup = adapter as IGamePathSetup;
        return new
        {
            paths.SaveRoot,
            paths.GameDir,
            SaveRootHint = setup?.SaveRootHint,
            GameDirHint = setup?.GameDirectoryHint,
            NeedsGameDir = setup is not null,
            SaveRootCheck = CheckPayload(saveCheck),
            GameDirCheck = CheckPayload(gameCheck),
            Ready = saveCheck.Ok && gameCheck.Ok,
            File = _settings?.FilePath
        };
    }

    private static string RequireKind(JsonElement parameters)
    {
        var kind = ReadString(parameters, "kind");
        return kind is SaveRootKind or GameDirKind
            ? kind
            : throw new BridgeArgumentException("kind 必须是 saveRoot 或 gameDir。");
    }

    private string SettingsGet(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        return Ok(id, adapter.GameId, SettingsPayload(adapter));
    }

    /// <summary>只读校验一个目录，返回 {ok, path, message, warning}；不保存。</summary>
    private string SettingsValidate(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var kind = RequireKind(parameters);
        return Ok(id, adapter.GameId, CheckPayload(CheckPath(adapter, kind, ReadPath(parameters, "path"))));
    }

    /// <summary>
    /// 打开原生的选择文件夹对话框并校验选中的目录。页面只能指定要选哪一项（kind），
    /// 不能指定读取任何路径；返回的只有选中的路径和校验结果。取消时 cancelled 为 true。
    /// </summary>
    private string SettingsPickFolder(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var kind = RequireKind(parameters);
        if (_pickFolder is null)
            throw new BridgeArgumentException("这里不能打开选择文件夹的对话框。");
        var paths = PathsOf(adapter);
        var current = kind == SaveRootKind ? paths.SaveRoot : paths.GameDir;
        var hint = kind == SaveRootKind ? (adapter as IGamePathSetup)?.SaveRootHint : null;
        var initial = new[] { current, hint }.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        var picked = _pickFolder(kind == SaveRootKind ? "选择存档目录" : "选择游戏目录", initial);
        if (string.IsNullOrWhiteSpace(picked))
            return Ok(id, adapter.GameId, new { Cancelled = true });
        var check = CheckPath(adapter, kind, picked);
        return Ok(id, adapter.GameId, new
        {
            Cancelled = false,
            Picked = picked,
            check.Ok,
            check.Path,
            check.Message,
            check.Warning,
            check.Detected,
            check.Expansions
        });
    }

    /// <summary>两项都校验通过才保存（存规范化后的路径），随后立即生效：存档列表、物品目录和图标都按新路径重新加载。</summary>
    private string SettingsSave(string? id, JsonElement parameters)
    {
        var adapter = RequireGame(parameters);
        var saveCheck = CheckPath(adapter, SaveRootKind, ReadPath(parameters, SaveRootKind));
        if (!saveCheck.Ok)
            throw new BridgeArgumentException("存档目录：" + saveCheck.Message);
        var gameCheck = CheckPath(adapter, GameDirKind, ReadPath(parameters, GameDirKind));
        if (!gameCheck.Ok)
            throw new BridgeArgumentException("游戏目录：" + gameCheck.Message);
        var next = new GamePaths(saveCheck.Path, adapter is IGamePathSetup ? gameCheck.Path : null);
        _settings?.Set(adapter.GameId, next);
        var previous = PathsOf(adapter);
        _paths[adapter.GameId] = next;
        if (!string.Equals(previous.SaveRoot, next.SaveRoot, StringComparison.OrdinalIgnoreCase))
            _enable.Stop();
        // 存档列表自动刷新（T030）跟着新的存档根目录走；没变且正在监视时什么也不做。
        Rewatch(adapter);
        if (adapter is IGamePathSetup setup)
            ApplyGameDirectory(setup, next.GameDir);
        return Ok(id, adapter.GameId, SettingsPayload(adapter));
    }

    private static int ReadInt(JsonElement parameters, string name, int fallback)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
            throw new BridgeArgumentException($"{name} 必须是整数。");
        return parsed;
    }

    /// <summary>
    /// 槽位标识就是 saves.list 返回的 slot：存档文件路径，用户目录部分已缩写（D021）。这里先还原缩写，
    /// 再要求它在当前存档根目录内、并且是刚枚举出来的已知槽位文件之一，所以页面送来任何别的路径都会被拒绝。
    /// </summary>
    private string RequireSlot(JsonElement parameters, IGameAdapter adapter)
    {
        var slot = parameters.ValueKind == JsonValueKind.Object ? ReadPath(parameters, "slot") : null;
        if (string.IsNullOrWhiteSpace(slot))
            throw new BridgeArgumentException("缺少 slot。");
        string full;
        try
        {
            full = Path.GetFullPath(slot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new BridgeArgumentException("槽位路径无效。");
        }

        var root = Path.GetFullPath(Root(adapter));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new BridgeArgumentException("槽位不在存档根目录内。");
        var known = SaveQueryService.ListSlots(adapter, root)
            .Select(item => Path.GetFullPath(item.SaveFilePath))
            .Any(path => path.Equals(full, StringComparison.OrdinalIgnoreCase));
        if (!known)
            throw new BridgeArgumentException("不是已发现的槽位文件。");
        return full;
    }

    private static List<ResourceChange> ReadChanges(JsonElement parameters, IGameAdapter adapter)
    {
        var changes = new List<ResourceChange>();
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("changes", out var array))
            return changes;
        if (array.ValueKind == JsonValueKind.Null)
            return changes;
        if (array.ValueKind != JsonValueKind.Array)
            throw new BridgeArgumentException("changes 必须是数组。");
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new BridgeArgumentException("changes 的每一项必须是对象。");
            var resourceId = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new BridgeArgumentException("changes 缺少 id。");
            if (!item.TryGetProperty("value", out var raw) || raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out var value))
                throw new BridgeArgumentException($"资源 {resourceId} 的 value 不是整数。");
            if (!adapter.Resources.Any(resource => resource.Id.Value == resourceId))
                throw new BridgeArgumentException($"未知资源 {resourceId}。");
            changes.Add(new ResourceChange(new ResourceId(resourceId), value));
        }

        return changes;
    }

    /// <summary>存档根目录：来自 --save-root 或用户设置（D020），不再用适配器的默认位置。</summary>
    private string Root(IGameAdapter adapter) =>
        PathsOf(adapter).SaveRoot ?? throw new BridgeArgumentException("还没有设置存档目录，请先点右上角的齿轮选择存档目录。");

    private static bool ReadBool(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            _ => throw new BridgeArgumentException($"{name} 必须是布尔值。")
        };
    }

    /// <summary>页面送回的路径：还原 %USERPROFILE% / %LOCALAPPDATA% 缩写（D021）。</summary>
    private string? ReadPath(JsonElement element, string name) => _display.Expand(ReadString(element, name));

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private string Ok(string? id, string? game, object data) =>
        new JsonEnvelope { Status = "ok", Game = game, Data = MaskData(data), Id = id }.ToJson();

    private string Fail(string? id, string? game, string code, string message) =>
        new JsonEnvelope
        {
            Status = "error",
            Game = game,
            Error = new JsonEnvelopeError(code, _display.Mask(message)),
            Id = id
        }.ToJson();

    /// <summary>
    /// 应答数据先转成 JSON 树，把其中每个字符串值里的用户目录缩写掉（D021）：路径、校验提示、备份和槽位信息一律覆盖，
    /// 以后新增的字段也不会漏。键名和数字不变。
    /// </summary>
    private JsonNode? MaskData(object data) =>
        MaskNode(JsonSerializer.SerializeToNode(data, data.GetType(), JsonOptions));

    private JsonNode? MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (key, child) in item.ToList())
                {
                    var masked = MaskNode(child);
                    if (!ReferenceEquals(masked, child))
                        item[key] = masked;
                }

                return item;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    var child = list[index];
                    var masked = MaskNode(child);
                    if (!ReferenceEquals(masked, child))
                        list[index] = masked;
                }

                return list;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                var shown = _display.Mask(text);
                return ReferenceEquals(shown, text) ? value : JsonValue.Create(shown);
            default:
                return node;
        }
    }
}
