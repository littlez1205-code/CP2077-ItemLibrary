using System.Globalization;
using System.Text;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Container;
using GameTool.Adapters.Cyberpunk2077.Localization;
using GameTool.Adapters.Cyberpunk2077.Names;
using GameTool.Adapters.Cyberpunk2077.Nodes;
using GameTool.App;
using GameTool.Core;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return App.Run(args);

internal static class App
{
    public static int Run(string[] args)
    {
        var json = args.Any(arg => arg == "--json");
        try
        {
            if (args.Length == 0)
                return Fail(json, 2, "bad_args", "用法: GameTool info|smoke|patch|restore|watch|research|clone|generate [--save <槽位目录>] [--json]");

            if (args[0] == "patch")
                return Patch(CliAdapter(), args, json);
            if (args[0] == "watch")
                return Watch(CliAdapter(), args, json);
            if (args[0] == "research")
                return Research(CliAdapter(), args, json);
            if (args[0] == "clone")
                return Clone(CliAdapter(), args, json);
            if (args[0] == "generate")
                return GenerateWithGame(args, json);

            string? save = null;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--json")
                    continue;
                if (args[i] == "--save")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                        return Fail(json, 2, "bad_args", "缺少 --save 的槽位目录。");
                    save = args[++i];
                    continue;
                }

                return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
            }

            var adapter = CliAdapter();
            return args[0] switch
            {
                "info" => Info(adapter, save, json),
                "smoke" => Smoke(adapter, save, json),
                "patch" => Patch(adapter, args, json),
                "restore" => Restore(adapter, save, json),
                _ => Fail(json, 2, "bad_args", $"未知命令 {args[0]}。")
            };
        }
        catch (Exception ex)
        {
            var mapped = ErrorMapper.Map(ex);
            return Fail(json, mapped.ExitCode, mapped.Code, mapped.Message);
        }
    }

    /// <summary>
    /// CLI 的适配器：名字表、图标用 Steam 探测到的游戏目录（第一次需要时才探测）。
    /// 桌面端不再自动探测（D020），改由用户设置；SteamGameLocator 只留给 CLI 和桌面自检。
    /// </summary>
    private static Cp2077Adapter CliAdapter()
    {
        var game = new Lazy<string?>(SteamGameLocator.FindInstalledGame);
        return new Cp2077Adapter(() => Cp2077ItemNames.ForGame(game.Value));
    }

    /// <summary>
    /// generate 可以用 --game 指定游戏目录（D023：例如只有本体的游戏目录），按 Cp2077Paths 校验后用于名字表和目录；不给时同其他命令用 Steam 探测。
    /// </summary>
    private static int GenerateWithGame(string[] args, bool json)
    {
        var at = Array.IndexOf(args, "--game");
        if (at < 0)
            return Generate(CliAdapter(), args, json);
        if (at + 1 >= args.Length)
            return Fail(json, 2, "bad_args", "缺少 --game 的值。");
        var check = Cp2077Paths.CheckGameDirectory(args[at + 1]);
        if (!check.Ok)
            return Fail(json, 1, "failed", "游戏目录：" + check.Message);
        if (!json)
            Console.WriteLine($"game={check.Path} {check.Detected}");
        var game = check.Path;
        var rest = args.Take(at).Concat(args.Skip(at + 2)).ToArray();
        return Generate(new Cp2077Adapter(() => Cp2077ItemNames.ForGame(game)), rest, json);
    }

    private static int Info(Cp2077Adapter adapter, string? slotDirectory, bool json)
    {
        if (!TryResolveSave(adapter, slotDirectory, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);

        if (!adapter.IsComplete(saveFile))
            return Fail(json, 1, "incomplete_save", "存档文件不完整（缺少 VASC 头或 DONE 尾）。");

        var data = SaveQueryService.GetInfo(adapter, saveFile);
        var resources = data.Resources;

        if (json)
        {
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, data).ToJson());
        }
        else
        {
            Console.WriteLine($"gameVersion: {data.GameVersion}");
            Console.WriteLine($"saveVersion: {data.SaveVersion}");
            foreach (var field in data.Diagnostics)
                Console.WriteLine($"{field.Name}: {field.Value}");
            foreach (var resource in resources)
            {
                var quantity = resource.Quantity?.ToString() ?? "null";
                Console.WriteLine($"{resource.Id}: quantity={quantity} stacks={resource.Stacks.Count} ambiguous={resource.Ambiguous}");
            }
        }

        return 0;
    }

    private static int Smoke(Cp2077Adapter adapter, string? slotDirectory, bool json)
    {
        if (!TryResolveSave(adapter, slotDirectory, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);

        var original = new FileInfo(saveFile);
        var length = original.Length;
        var modified = original.LastWriteTimeUtc;
        var tempRoot = Path.Combine(Path.GetTempPath(), "GameTool-smoke-" + Guid.NewGuid().ToString("N"));

        try
        {
            var slotName = Path.GetFileName(Path.GetDirectoryName(saveFile)) ?? "slot";
            var tempSlot = Path.Combine(tempRoot, slotName);
            Directory.CreateDirectory(tempSlot);
            var tempSave = Path.Combine(tempSlot, "sav.dat");
            CopyShared(saveFile, tempSave);

            var container = SaveContainer.Read(tempSave);
            var identity = container.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(tempSave));

            container.MarkAllDirty();
            var reread = SaveContainer.Read(container.ToArray());
            var roundtrip = SamePayload(container, reread);

            original.Refresh();
            var untouched = original.Length == length && original.LastWriteTimeUtc == modified;
            var document = adapter.Load(tempSave);
            var inventoryParsed = adapter.Resources.Count == 10;
            if (inventoryParsed)
            {
                foreach (var resource in adapter.Resources)
                    document.GetQuantity(resource.Id);
            }

            var originalCopy = File.ReadAllBytes(tempSave);
            var fill = ChangePlanner.Plan(adapter.Resources, fill: true, money: null, materials: null);
            var first = PatchService.Patch(adapter, tempSave, fill);
            var researchDetail = ResearchDetail(adapter, PrepareResearchPair(adapter, tempRoot, tempSave + ".bak"));
            var researchFlow = researchDetail == "pass";
            var filled = HeldResourcesMatchDefault(adapter, tempSave);
            var backupPath = tempSave + ".bak";
            var backupMatches = File.Exists(backupPath) && File.ReadAllBytes(backupPath).AsSpan().SequenceEqual(originalCopy);
            var backupStamp = File.GetLastWriteTimeUtc(backupPath);
            var second = PatchService.Patch(adapter, tempSave, fill);
            var backupStable = second.Outcome == PatchOutcome.NoChange
                && File.ReadAllBytes(backupPath).AsSpan().SequenceEqual(originalCopy)
                && File.GetLastWriteTimeUtc(backupPath) == backupStamp;
            PatchService.Restore(adapter, tempSave);
            var restored = File.ReadAllBytes(tempSave).AsSpan().SequenceEqual(originalCopy);
            var patchFlow = first.Outcome == PatchOutcome.Patched && filled && backupMatches && backupStable && restored;
            var (watchFlow, watchMs) = RunWatchFlow(adapter, tempRoot, tempSave, fill);
            var insertDetail = InsertFlow(adapter, tempRoot, originalCopy);
            var insertFlow = insertDetail == "pass";
            var cloneDetail = CloneFlow(adapter, tempRoot, originalCopy);
            var cloneFlow = cloneDetail == "pass";
            var generateDetail = GenerateFlow(adapter, tempRoot, originalCopy);
            var generateFlow = generateDetail == "pass" || generateDetail.StartsWith(GenerateSkipped, StringComparison.Ordinal);

            if (!identity || !roundtrip || !untouched || !inventoryParsed || !patchFlow || !watchFlow || !researchFlow || !insertFlow || !cloneFlow || !generateFlow)
            {
                return Fail(
                    json,
                    1,
                    "smoke_failed",
                    $"identityWrite={identity}; recompressRoundtrip={roundtrip}; originalUntouched={untouched}; inventoryParsed={inventoryParsed}; patchFlow={patchFlow}; watchFlow={watchFlow}; researchFlow={researchDetail}; insertFlow={insertDetail}; cloneFlow={cloneDetail}; generateFlow={generateDetail}; writeCompleteToDoneMs={watchMs}");
            }

            var data = new SmokeData(true, true, true, true, true, true, true, true, true, true, generateDetail == "pass" ? null : generateDetail, watchMs);
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, data).ToJson());
            else
            {
                Console.WriteLine("identityWrite: pass");
                Console.WriteLine("recompressRoundtrip: pass");
                Console.WriteLine("originalUntouched: pass");
                Console.WriteLine("inventoryParsed: pass");
                Console.WriteLine("patchFlow: pass");
                Console.WriteLine($"watchFlow: pass {watchMs}ms");
                Console.WriteLine("researchFlow: pass");
                Console.WriteLine("insertFlow: pass");
                Console.WriteLine("cloneFlow: pass");
                Console.WriteLine(generateDetail == "pass" ? "generateFlow: pass" : "generateFlow: pass (" + generateDetail + ")");
            }

            return 0;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static (bool Ok, long Milliseconds) RunWatchFlow(
        Cp2077Adapter adapter,
        string tempRoot,
        string sourceSave,
        IReadOnlyList<ResourceChange> changes)
    {
        var watchRoot = Path.Combine(tempRoot, "watch");
        var slot = Path.Combine(watchRoot, "QuickSave-0");
        Directory.CreateDirectory(slot);
        var ready = new TaskCompletionSource<WatchEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new WatchService(
            adapter,
            watchRoot,
            ["QuickSave-*/sav.dat"],
            changes,
            debounceMilliseconds: 150,
            onEvent: watchEvent =>
            {
                if (watchEvent.Outcome is "patched" or "error")
                    ready.TrySetResult(watchEvent);
            });
        watcher.Start();
        try
        {
            File.Copy(sourceSave, Path.Combine(slot, "sav.dat"));
            var finished = Task.WhenAny(ready.Task, Task.Delay(5000)).GetAwaiter().GetResult();
            if (finished != ready.Task || ready.Task.Result.Outcome != "patched")
                return (false, ready.Task.IsCompleted ? ready.Task.Result.WriteCompleteToDoneMs ?? -1 : -1);
            var milliseconds = ready.Task.Result.WriteCompleteToDoneMs ?? -1;
            var valuesOk = HeldResourcesMatchDefault(adapter, Path.Combine(slot, "sav.dat"));
            return (valuesOk && milliseconds >= 0 && milliseconds <= 500, milliseconds);
        }
        finally
        {
            watcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int Watch(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? root = null;
        var includes = new List<string>();
        long? money = null;
        long? materials = null;
        var fill = false;
        int? exitAfter = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--fill")
            {
                fill = true;
                continue;
            }

            if (args[i] is not ("--root" or "--include" or "--money" or "--materials" or "--exit-after"))
                return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
            if (i + 1 >= args.Length)
                return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
            var flag = args[i];
            var value = args[++i];
            switch (flag)
            {
                case "--root":
                    root = value;
                    break;
                case "--include":
                    includes.Add(value);
                    break;
                case "--exit-after":
                    if (!int.TryParse(value, out var count) || count < 1)
                        return Fail(json, 2, "bad_args", "--exit-after 必须是正整数。");
                    exitAfter = count;
                    break;
                default:
                    if (!long.TryParse(value, out var number))
                        return Fail(json, 2, "bad_args", $"{flag} 不是整数。");
                    if (flag == "--money")
                        money = number;
                    else
                        materials = number;
                    break;
            }
        }

        if (money is null && materials is null && !fill)
            return Fail(json, 2, "bad_args", "watch 至少要给 --money、--materials 或 --fill 之一。");

        root ??= adapter.DefaultSaveRoot;
        if (!Directory.Exists(root))
            return Fail(json, 1, "failed", $"监听目录不存在：{root}");
        if (includes.Count == 0)
            includes.Add("QuickSave-*/sav.dat");

        var changes = ChangePlanner.Plan(adapter.Resources, fill, money, materials);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        var remaining = exitAfter ?? int.MaxValue;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.Error.WriteLine("监听中：F5 快速存档后会自动补丁；按 Ctrl+C 退出");
        WriteWatchLine(json, "started", null);
        var watcher = new WatchService(adapter, root, includes, changes, adapter.WatchSpec.DebounceMilliseconds, watchEvent =>
        {
            var kind = watchEvent.Outcome == "error" ? "error" : watchEvent.Outcome;
            WriteWatchLine(json, kind, watchEvent);
            if (Interlocked.Decrement(ref remaining) == 0)
                finished.TrySetResult();
        });
        watcher.Start();
        try
        {
            if (exitAfter is null)
                Task.Delay(Timeout.Infinite, stop.Token).GetAwaiter().GetResult();
            else
                Task.WhenAny(finished.Task, Task.Delay(Timeout.Infinite, stop.Token)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            watcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return 0;
    }

    private static void WriteWatchLine(bool json, string eventName, WatchEvent? watchEvent)
    {
        if (!json)
        {
            if (watchEvent is null)
                return;
            Console.WriteLine($"{watchEvent.Outcome}: {watchEvent.Path} writeCompleteToDoneMs={watchEvent.WriteCompleteToDoneMs}");
            return;
        }

        var envelope = new JsonEnvelope
        {
            Status = eventName == "error" ? "error" : "ok",
            Game = Cp2077Adapter.Game,
            Error = eventName == "error" ? new JsonEnvelopeError("watch_error", watchEvent?.Error ?? "监听失败") : null,
            Data = new
            {
                Event = eventName,
                Path = watchEvent?.Path,
                Outcome = watchEvent?.Outcome,
                Error = watchEvent?.Error,
                WriteCompleteToDoneMs = watchEvent?.WriteCompleteToDoneMs,
                FirstEventToDoneMs = watchEvent?.FirstEventToDoneMs,
                Results = watchEvent?.Results?.Select(result => new
                {
                    Id = result.Id.Value,
                    result.PreviousQuantity,
                    result.AppliedQuantity,
                    result.Outcome
                }).ToArray()
            }
        };
        Console.WriteLine(envelope.ToJson());
    }

    private static int Patch(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? slot = null;
        long? money = null;
        long? materials = null;
        var fill = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--fill")
            {
                fill = true;
                continue;
            }

            if (args[i] is not ("--save" or "--money" or "--materials"))
                return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
            if (i + 1 >= args.Length)
                return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
            var value = args[++i];
            if (args[i - 1] == "--save")
            {
                slot = value;
                continue;
            }

            if (!long.TryParse(value, out var number))
                return Fail(json, 2, "bad_args", $"{args[i - 1]} 不是整数。");
            if (args[i - 1] == "--money")
                money = number;
            else
                materials = number;
        }

        if (slot is null)
            return Fail(json, 2, "bad_args", "patch 必须显式指定 --save，避免误改。");
        if (money is null && materials is null && !fill)
            return Fail(json, 2, "bad_args", "patch 至少要给 --money、--materials 或 --fill 之一。");
        if (!TryResolveSave(adapter, slot, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);

        var planned = ChangePlanner.Plan(adapter.Resources, fill, money, materials);
        var report = PatchService.Patch(adapter, saveFile, planned);
        WritePatchReport(json, report);
        return 0;
    }

    private static int Restore(Cp2077Adapter adapter, string? slotDirectory, bool json)
    {
        if (slotDirectory is null)
            return Fail(json, 2, "bad_args", "restore 必须显式指定 --save。");
        if (!TryResolveSave(adapter, slotDirectory, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);

        var report = PatchService.Restore(adapter, saveFile);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { report.SaveFilePath, report.ElapsedMilliseconds }).ToJson());
        else
            Console.WriteLine($"restored: {report.SaveFilePath} elapsedMilliseconds={report.ElapsedMilliseconds}");
        return 0;
    }

    private static void WritePatchReport(bool json, PatchReport report)
    {
        if (json)
        {
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new
            {
                report.Outcome,
                report.BackupPath,
                report.ElapsedMilliseconds,
                Results = report.Results.Select(result => new
                {
                    Id = result.Id.Value,
                    result.PreviousQuantity,
                    result.AppliedQuantity,
                    result.Outcome
                }).ToArray()
            }).ToJson());
            return;
        }

        Console.WriteLine($"outcome: {report.Outcome}");
        Console.WriteLine($"elapsedMilliseconds: {report.ElapsedMilliseconds}");
        Console.WriteLine($"backupPath: {report.BackupPath ?? ""}");
        foreach (var result in report.Results)
        {
            Console.WriteLine(
                $"{result.Id.Value}: {result.PreviousQuantity?.ToString() ?? "null"} -> {result.AppliedQuantity?.ToString() ?? "null"} {result.Outcome}");
        }
    }

    private static bool HeldResourcesMatchDefault(Cp2077Adapter adapter, string saveFile)
    {
        var document = adapter.Load(saveFile);
        foreach (var resource in adapter.Resources)
        {
            var quantity = document.GetQuantity(resource.Id);
            if (quantity is null)
                continue;
            if (quantity != resource.DefaultFill)
                return false;
        }

        return true;
    }

    private static bool TryResolveSave(Cp2077Adapter adapter, string? slotDirectory, out string saveFile, out string error)
    {
        if (slotDirectory is not null)
        {
            if (!Directory.Exists(slotDirectory))
            {
                saveFile = "";
                error = $"找不到槽位目录 {slotDirectory}。";
                return false;
            }

            saveFile = Path.Combine(slotDirectory, "sav.dat");
            if (!File.Exists(saveFile))
            {
                error = $"槽位目录里没有 sav.dat：{slotDirectory}。";
                return false;
            }

            error = "";
            return true;
        }

        var match = ManualSaveSelection.NewestPlayable(adapter.DiscoverSaves(adapter.DefaultSaveRoot));
        if (match is null)
        {
            saveFile = "";
            error = "找不到 ManualSave-* 存档。";
            return false;
        }

        saveFile = match.SaveFilePath;
        error = "";
        return true;
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static bool SamePayload(SaveContainer left, SaveContainer right)
    {
        if (left.ChunkCount != right.ChunkCount)
            return false;
        for (var i = 0; i < left.ChunkCount; i++)
        {
            if (!left.GetDecompressed(i).AsSpan().SequenceEqual(right.GetDecompressed(i)))
                return false;
        }

        return left.CopyNodeTable().AsSpan().SequenceEqual(right.CopyNodeTable());
    }

    private static int Fail(bool json, int code, string errorCode, string message)
    {
        Console.Error.WriteLine(message);
        if (json)
            Console.WriteLine(JsonEnvelope.Fail(Cp2077Adapter.Game, errorCode, message).ToJson());
        return code;
    }

    private static (string Before, string After) PrepareResearchPair(Cp2077Adapter adapter, string tempRoot, string originalSave)
    {
        var before = Path.Combine(tempRoot, "research-before", "sav.dat");
        var after = Path.Combine(tempRoot, "research-after", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(before)!);
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        ResearchService.PrepareHeldQuantityPair(adapter, originalSave, before, after);
        return (before, after);
    }

    private static string ResearchDetail(Cp2077Adapter adapter, (string Before, string After) pair)
    {
        if (!File.Exists(pair.Before) || !File.Exists(pair.After))
            return "missing-pair";
        return ResearchService.DescribeHeldQuantityResearch(adapter, pair.Before, pair.After);
    }

    private static int Clone(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? key = null;
        var list = false;
        var copies = 1;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--list")
            {
                list = true;
                continue;
            }

            if (args[i] is "--save" or "--key" or "--copies")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                if (args[i] == "--save")
                    save = args[++i];
                else if (args[i] == "--key")
                    key = args[++i];
                else if (!int.TryParse(args[++i], out copies))
                    return Fail(json, 2, "bad_args", "件数必须是整数。");
                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (!TryResolveSave(adapter, save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        var document = (Cp2077SaveDocument)adapter.Load(saveFile);
        if (list)
        {
            var items = document.ListCloneable();
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { document.NamesAvailable, Message = document.NamesMessage, Items = items }).ToJson());
            else
            {
                if (!document.NamesAvailable && document.NamesMessage is not null)
                    Console.WriteLine(document.NamesMessage);
                foreach (var item in items)
                    Console.WriteLine($"{(item.BagId == ItemInserter.StashBagId ? "储物箱" : "背包")} {item.Label} {item.RecordName} {item.Key}");
            }

            return 0;
        }

        if (string.IsNullOrWhiteSpace(key))
            return Fail(json, 2, "bad_args", "用法: GameTool clone --save <槽位> --list | --key <键> --copies <1-10>");
        var report = PatchEngine.Clone(adapter, saveFile, new CloneRequest(key, copies), new PatchOptions(saveFile + ".bak"));
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { report.Seeds, report.AddedBytes, Copies = copies }).ToJson());
        else
            Console.WriteLine($"copies={copies} addedBytes={report.AddedBytes} seeds={string.Join(",", report.Seeds)}");
        return 0;
    }

    private static int Generate(Cp2077Adapter adapter, string[] args, bool json)
    {
        const string usage = "用法: GameTool generate --save <槽位> --catalog [--groups] [--kind clothing|ranged|melee|grenade|cyberware|arm_cyberware|quickhack|mod|consumable|ammo|material|recipe|currency] [--query 文本] [--skip n] [--take n] | --target <Items.X> [--copies 1-10，堆叠物品为要加的数量]；都可加 --game <游戏目录>";
        string? save = null;
        string? kind = null;
        string? query = null;
        string? target = null;
        var catalog = false;
        var grouped = false;
        var copies = 1;
        var skip = 0;
        var take = GenerateService.DefaultTake;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--catalog")
            {
                catalog = true;
                continue;
            }

            if (args[i] == "--groups")
            {
                grouped = true;
                continue;
            }

            if (args[i] is "--save" or "--kind" or "--query" or "--target" or "--copies" or "--skip" or "--take")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var name = args[i];
                var value = args[++i];
                switch (name)
                {
                    case "--save": save = value; break;
                    case "--kind": kind = value; break;
                    case "--query": query = value; break;
                    case "--target": target = value; break;
                    default:
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                            return Fail(json, 2, "bad_args", $"{name} 必须是整数。");
                        if (name == "--copies")
                            copies = number;
                        else if (name == "--skip")
                            skip = number;
                        else
                            take = number;
                        break;
                }

                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (catalog == (target is not null))
            return Fail(json, 2, "bad_args", usage);
        if (!TryResolveSave(adapter, save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        if (catalog && grouped)
        {
            var groups = GenerateService.Groups(adapter, saveFile, kind, query, skip, take);
            if (json)
            {
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, groups).ToJson());
                return 0;
            }

            if (!groups.NamesAvailable && groups.Message is not null)
                Console.WriteLine(groups.Message);
            var groupKinds = groups.Kinds.ToDictionary(item => item.Id, item => item.Label);
            foreach (var group in groups.Groups)
            {
                var kindLabel = groupKinds.TryGetValue(group.Kind, out var text) ? text : group.Kind;
                var variants = group.Variants.Count == 1
                    ? group.Variants[0].RecordName + (group.Variants[0].Owned ? " 已拥有" : "")
                    : $"{group.Variants.Count} 个{group.VariantNoun}: " + string.Join(" | ", group.Variants.Select((item, i) =>
                        $"{(i == group.DefaultIndex ? "*" : "")}{item.VariantLabel}={item.RecordName}{(item.Owned ? "(已拥有)" : "")}"));
                Console.WriteLine($"{kindLabel} {group.Label} [{group.Quality ?? "-"}{new string('+', group.Plus)}{(group.Iconic ? " 标志性" : "")}] {variants}");
            }

            Console.WriteLine($"groups={groups.Total} items={groups.ItemTotal} skip={groups.Skip} shown={groups.Groups.Count}");
            WriteHidden(groups.Hidden, groups.HiddenNotice);
            return 0;
        }

        if (catalog)
        {
            var view = GenerateService.Catalog(adapter, saveFile, kind, query, skip, take);
            if (json)
            {
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, view).ToJson());
                return 0;
            }

            if (!view.NamesAvailable && view.Message is not null)
                Console.WriteLine(view.Message);
            var labels = view.Kinds.ToDictionary(item => item.Id, item => item.Label);
            foreach (var item in view.Items)
            {
                var kindLabel = labels.TryGetValue(item.Kind, out var text) ? text : item.Kind;
                Console.WriteLine($"{kindLabel} {item.Label} {item.RecordName}{(item.Owned ? " 已拥有" : "")}");
            }

            Console.WriteLine($"total={view.Total} skip={view.Skip} shown={view.Items.Count}");
            WriteHidden(view.Hidden, view.HiddenNotice);
            return 0;
        }

        // 堆叠物品（消耗品、弹药、制作材料、配方、货币）按数量获得：--copies 是要加的数量，已持有就加数量，没有就插入 29 字节堆叠。
        if (adapter.Load(saveFile) is Cp2077SaveDocument stackDocument && stackDocument.FindGeneratable(target!) is { Stackable: true } stackItem)
            return ObtainStack(adapter, saveFile, stackDocument, stackItem, copies, json);

        var report = PatchEngine.Generate(adapter, saveFile, new GenerateRequest(target!, copies), new PatchOptions(saveFile + ".bak"));
        if (json)
        {
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new
            {
                Target = target,
                Copies = copies,
                report.Seeds,
                report.AddedBytes,
                report.TemplateKey,
                report.TemplateLabel,
                report.Patch.BackupPath
            }).ToJson());
        }
        else
        {
            Console.WriteLine($"target={target} copies={copies} addedBytes={report.AddedBytes} seeds={string.Join(",", report.Seeds.Select(seed => seed.ToString("x8", CultureInfo.InvariantCulture)))}");
            Console.WriteLine($"template={report.TemplateLabel} {report.TemplateKey}");
        }

        return 0;
    }

    /// <summary>D023：没装往日之影时，各分类隐藏的卡片数和条目数。</summary>
    private static void WriteHidden(IReadOnlyList<GenerateHiddenKind> hidden, string? notice)
    {
        foreach (var kind in hidden)
            Console.WriteLine($"hidden {kind.Kind} {kind.Label} groups={kind.Groups} items={kind.Items}");
        if (notice is not null)
            Console.WriteLine(notice);
    }

    /// <summary>
    /// generate --target 的堆叠路径（T023，配方等）：走 PatchEngine.Patch 的改数量 / 插入新堆叠和原有严格校验，生成 .bak。
    /// 目标不在内置资源表里时追加一条临时资源描述（同桌面端物品库）。
    /// </summary>
    private static int ObtainStack(Cp2077Adapter adapter, string saveFile, Cp2077SaveDocument document, GenerateCatalogItem item, long quantity, bool json)
    {
        var resource = document.StackResource(item.Target)
            ?? throw new PatchRejectedException($"{item.Label} 不能按数量获得。");
        if (quantity < 1)
            return Fail(json, 2, "bad_args", "数量至少为 1。");
        var after = (item.Held ?? 0) + quantity;
        if (after > resource.Max)
            return Fail(json, 2, "bad_args", $"{item.Label} 获得后为 {after}，超过上限 {resource.Max}。");
        IGameAdapter working = adapter.Resources.Any(entry => entry.Id == resource.Id) ? adapter : adapter.WithResources([resource]);
        var report = PatchEngine.Patch(working, saveFile, [new ResourceChange(resource.Id, after)], new PatchOptions(saveFile + ".bak"));
        var inserted = report.Results.Any(result => result.Outcome == ChangeOutcome.Inserted);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { item.Target, item.Label, Before = item.Held, After = after, Inserted = inserted, Outcome = report.Outcome, report.BackupPath }).ToJson());
        else
            Console.WriteLine($"target={item.Target} label={item.Label} before={item.Held?.ToString(CultureInfo.InvariantCulture) ?? "-"} after={after} inserted={inserted.ToString().ToLowerInvariant()} outcome={report.Outcome}");
        return report.Outcome == PatchOutcome.Patched ? 0 : 1;
    }

    private static int Research(Cp2077Adapter adapter, string[] args, bool json)
    {
        if (args.Length < 2)
            return Fail(json, 2, "bad_args", "用法: GameTool research dump|diff|find|inventory|insert|generate|parts|remove|loc|names ...");
        return args[1] switch
        {
            "dump" => ResearchDump(adapter, args, json),
            "diff" => ResearchDiff(adapter, args, json),
            "find" => ResearchFind(adapter, args, json),
            "inventory" => ResearchInventory(adapter, args, json),
            "insert" => ResearchInsert(adapter, args, json),
            "generate" => ResearchGenerate(adapter, args, json),
            "parts" => ResearchParts(args, json),
            "remove" => ResearchRemove(adapter, args, json),
            "loc" => ResearchLoc(adapter, args, json),
            "names" => ResearchNames(args, json),
            _ => Fail(json, 2, "bad_args", $"未知研究命令 {args[1]}。")
        };
    }

    private static int ResearchDump(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? output = null;
        var force = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--force")
            {
                force = true;
                continue;
            }

            if (args[i] is "--save" or "--out")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                if (args[i] == "--save")
                    save = args[++i];
                else
                    output = args[++i];
                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (save is null || output is null)
            return Fail(json, 2, "bad_args", "research dump 需要 --save 和 --out。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        ResearchService.Dump(adapter, saveFile, output, force);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { Output = Path.GetFullPath(output) }).ToJson());
        else
            Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static int ResearchDiff(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? left = null;
        string? right = null;
        string? only = null;
        var summary = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--summary")
            {
                summary = true;
                continue;
            }

            if (args[i] is "--a" or "--b" or "--only")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                if (args[i - 1] == "--a")
                    left = value;
                else if (args[i - 1] == "--b")
                    right = value;
                else if (only is not null)
                    return Fail(json, 2, "bad_args", "research diff 只能有一个 --only。");
                else
                    only = value;
                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (left is null || right is null)
            return Fail(json, 2, "bad_args", "research diff 需要 --a 和 --b。");
        if (!TryResolveResearchSave(left, out var leftFile, out var leftError))
            return Fail(json, 1, "save_not_found", leftError);
        if (!TryResolveResearchSave(right, out var rightFile, out var rightError))
            return Fail(json, 1, "save_not_found", rightError);
        var diff = ResearchService.Diff(adapter, leftFile, rightFile);
        if (only is not null)
        {
            var names = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            diff = ResearchService.SelectRegions(diff, names);
        }

        if (summary)
        {
            var report = ResearchService.Summarize(diff);
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, report).ToJson());
            else
                WriteSummary(diff, report);
            return 0;
        }

        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, diff).ToJson());
        else
            WriteDiff(diff);
        return 0;
    }

    private static int ResearchFind(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? inRegion = null;
        ResearchNeedle? needle = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] is "--save" or "--u32" or "--tdb" or "--hex" or "--ascii" or "--in")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                if (args[i - 1] == "--save")
                    save = value;
                else if (args[i - 1] == "--in")
                {
                    if (inRegion is not null)
                        return Fail(json, 2, "bad_args", "research find 只能有一个 --in。");
                    inRegion = value;
                }
                else if (needle is not null)
                    return Fail(json, 2, "bad_args", "research find 只能指定一种搜索。");
                else
                    needle = new ResearchNeedle(args[i - 1] switch
                    {
                        "--u32" => ResearchNeedleKind.U32,
                        "--tdb" => ResearchNeedleKind.Tdb,
                        "--hex" => ResearchNeedleKind.Hex,
                        _ => ResearchNeedleKind.Ascii
                    }, value);
                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (save is null || needle is null)
            return Fail(json, 2, "bad_args", "research find 需要 --save 和一种搜索。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        var found = ResearchService.Find(adapter, saveFile, needle, inRegion);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, found).ToJson());
        else
            WriteFind(found);
        return 0;
    }

    private static int ResearchInventory(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? left = null;
        string? right = null;
        var bag = InventoryResearch.DefaultBagId;
        var allBags = false;
        var names = false;
        string? game = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--names")
            {
                names = true;
                continue;
            }

            if (args[i] == "--all-bags")
            {
                allBags = true;
                continue;
            }

            if (args[i] is "--save" or "--a" or "--b" or "--bag" or "--game")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--save":
                        save = value;
                        break;
                    case "--a":
                        left = value;
                        break;
                    case "--b":
                        right = value;
                        break;
                    case "--game":
                        game = value;
                        break;
                    default:
                        if (!ulong.TryParse(value, out bag))
                            return Fail(json, 2, "bad_args", "子背包 ID 不是整数。");
                        break;
                }

                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (left is not null || right is not null)
        {
            if (save is not null || allBags || names || left is null || right is null)
                return Fail(json, 2, "bad_args", "research inventory 对比需要 --a 和 --b，并且不能和 --save、--all-bags 或 --names 一起用。");
            if (!TryResolveResearchSave(left, out var leftFile, out var leftError))
                return Fail(json, 1, "save_not_found", leftError);
            if (!TryResolveResearchSave(right, out var rightFile, out var rightError))
                return Fail(json, 1, "save_not_found", rightError);
            var compare = InventoryResearch.Compare(adapter, leftFile, rightFile, bag);
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, compare).ToJson());
            else
                WriteInventoryCompare(compare);
            return 0;
        }

        if (save is null)
            return Fail(json, 2, "bad_args", "research inventory 需要 --save，或 --a 和 --b。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        if (allBags)
        {
            var bags = InventoryResearch.Bags(saveFile);
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, bags).ToJson());
            else
            {
                foreach (var item in bags)
                    Console.WriteLine($"bag {item.Id} items={item.Items}");
            }

            return 0;
        }

        var inventory = InventoryResearch.Read(adapter, saveFile, bag);
        if (!names)
        {
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, inventory).ToJson());
            else
                WriteInventory(inventory);
            return 0;
        }

        game ??= SteamGameLocator.FindInstalledGame();
        if (string.IsNullOrWhiteSpace(game))
            return Fail(json, 1, "failed", "找不到游戏目录，请用 --game 指定。");
        var catalog = ItemNameCatalog.Load(game);
        if (!catalog.Available && !json)
            Console.WriteLine(ItemNameCatalog.RedmodUnavailable);
        var named = inventory.Items.Select(item =>
        {
            var resolved = catalog.Resolve(item.TweakDbId);
            return new
            {
                item.Ordinal,
                item.NodeIndex,
                item.HeaderHex,
                item.TweakDbId,
                item.Name,
                item.Length,
                item.Stack,
                item.Quantity,
                resolved.RecordName,
                resolved.DisplayName,
                resolved.Text,
                resolved.Resolved
            };
        }).ToArray();
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new
            {
                inventory.BagId,
                NamesAvailable = catalog.Available,
                Message = catalog.Available ? null : ItemNameCatalog.RedmodUnavailable,
                catalog.TweakScanMilliseconds,
                catalog.LocalizationMilliseconds,
                Items = named
            }).ToJson());
        else
        {
            Console.WriteLine($"tweakScanMs={catalog.TweakScanMilliseconds} localizationMs={catalog.LocalizationMilliseconds}");
            Console.WriteLine($"bag {inventory.BagId}");
            foreach (var item in named)
            {
                var quantity = item.Stack ? $" quantity={item.Quantity}" : "";
                var stack = item.Stack ? " stack" : "";
                Console.WriteLine($"#{item.Ordinal} node={item.NodeIndex} header={item.HeaderHex} tdb={item.TweakDbId} name={item.Name ?? "—"} length={item.Length}{stack}{quantity} record={item.RecordName ?? item.TweakDbId} text={item.Text ?? "—"}");
            }
        }

        return 0;
    }

    private static int ResearchNames(string[] args, bool json)
    {
        string? key = null;
        string? game = null;
        var lang = "zh-cn";
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] is not ("--key" or "--game" or "--lang"))
                return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
            var value = args[++i];
            switch (args[i - 1])
            {
                case "--key": key = value; break;
                case "--game": game = value; break;
                default: lang = value; break;
            }
        }

        if (key is null)
            return Fail(json, 2, "bad_args", "research names 需要 --key。");
        if (!ItemNameCatalog.TryParseKey(key, out var parsed))
            return Fail(json, 2, "bad_args", $"无法解析 --key {key}。");
        game ??= SteamGameLocator.FindInstalledGame();
        if (string.IsNullOrWhiteSpace(game))
            return Fail(json, 1, "failed", "找不到游戏目录，请用 --game 指定。");
        var catalog = ItemNameCatalog.Load(game, lang);
        if (!catalog.Available)
        {
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { Available = false, Message = ItemNameCatalog.RedmodUnavailable }).ToJson());
            else
                Console.WriteLine(ItemNameCatalog.RedmodUnavailable);
            return 0;
        }

        var resolved = parsed.IsId ? catalog.Resolve(parsed.Id) : catalog.Resolve(parsed.Name);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { Available = true, catalog.TweakScanMilliseconds, catalog.LocalizationMilliseconds, resolved }).ToJson());
        else
        {
            Console.WriteLine($"tweakScanMs={catalog.TweakScanMilliseconds} localizationMs={catalog.LocalizationMilliseconds}");
            Console.WriteLine($"record={resolved.RecordName ?? resolved.Label}");
            Console.WriteLine($"displayName={resolved.DisplayName ?? "—"}");
            Console.WriteLine($"text={resolved.Text ?? "—"}");
            Console.WriteLine($"resolved={resolved.Resolved.ToString().ToLowerInvariant()}");
        }

        return 0;
    }

    private static int ResearchInsert(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? output = null;
        string? tweak = null;
        string? quantityText = null;
        int? ordinal = null;
        uint? seed = null;
        var intoSaveRoot = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--into-save-root")
            {
                intoSaveRoot = true;
                continue;
            }

            if (args[i] is "--save" or "--out" or "--stack-tdb" or "--qty" or "--clone" or "--seed")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--save":
                        save = value;
                        break;
                    case "--out":
                        output = value;
                        break;
                    case "--stack-tdb":
                        tweak = value;
                        break;
                    case "--qty":
                        quantityText = value;
                        break;
                    case "--clone":
                        if (!int.TryParse(value, out var parsed) || parsed < 1)
                            return Fail(json, 2, "bad_args", "克隆序号必须是正整数。");
                        ordinal = parsed;
                        break;
                    default:
                        if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedSeed))
                            return Fail(json, 2, "bad_args", "种子必须是 8 位十六进制。");
                        seed = parsedSeed;
                        break;
                }

                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        var stack = tweak is not null || quantityText is not null;
        var clone = ordinal is not null || seed is not null;
        if (save is null || output is null || stack == clone || (stack && (tweak is null || quantityText is null)) || (clone && ordinal is null))
            return Fail(json, 2, "bad_args", "research insert 需要 --save、--out，以及 --stack-tdb/--qty 或 --clone。");
        if (stack && seed is not null)
            return Fail(json, 2, "bad_args", "堆叠插入不能带 --seed。");
        uint quantity = 0;
        if (stack && !uint.TryParse(quantityText, out quantity))
            return Fail(json, 2, "bad_args", "数量不是整数。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        ResearchService.ValidateInsertOutput(saveFile, output, intoSaveRoot);
        var full = Path.GetFullPath(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var container = SaveContainer.Read(saveFile);
        var insertion = stack
            ? ItemInserter.InsertStack(container, tweak!, quantity)
            : ItemInserter.InsertClone(container, ordinal!.Value, seed);
        var sourceDir = Path.GetDirectoryName(saveFile) ?? throw new ResearchRejectedException("无法确定源槽位目录。");
        foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
        {
            if (!File.Exists(Path.Combine(sourceDir, name)))
                throw new ResearchRejectedException($"源槽位缺少 {name}。");
        }

        Directory.CreateDirectory(full);
        try
        {
            foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
                File.Copy(Path.Combine(sourceDir, name), Path.Combine(full, name));
            var destination = Path.Combine(full, "sav.dat");
            using (var stream = File.Create(destination))
                container.Write(stream);
            adapter.Load(destination);
            var inventory = InventoryResearch.Read(adapter, destination, InventoryResearch.DefaultBagId);
            if (inventory.Items.Count != insertion.PreviousPlayerCount + 1)
                throw new ResearchRejectedException("自检失败：玩家背包物品数没有加 1。");
            var inserted = inventory.Items[^1];
            if (!string.Equals(inserted.HeaderHex, insertion.HeaderHex, StringComparison.OrdinalIgnoreCase))
                throw new ResearchRejectedException("自检失败：新物品不在玩家背包末尾。");
            if (insertion.Quantity is uint expected && inserted.Quantity != expected)
                throw new ResearchRejectedException("自检失败：新物品数量不对。");
            ResearchService.RequireSingleItemInsertion(ResearchService.Diff(adapter, saveFile, destination), insertion.AddedBytes);
        }
        catch
        {
            Directory.Delete(full, recursive: true);
            throw;
        }

        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, insertion).ToJson());
        else
        {
            Console.WriteLine($"self-check: pass node={insertion.NodeIndex} bytes={insertion.AddedBytes} header={insertion.HeaderHex}");
            if (insertion.TweakName is not null)
                Console.WriteLine($"stack {insertion.TweakName} qty={insertion.Quantity}");
            if (insertion.Seed is uint written)
                Console.WriteLine($"seed={written:x8}");
        }

        return 0;
    }

    private static int ResearchGenerate(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? output = null;
        string? template = null;
        string? target = null;
        string? partsText = null;
        string? game = null;
        var intoSaveRoot = false;
        var zeroPrefixIds = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--into-save-root")
            {
                intoSaveRoot = true;
                continue;
            }

            if (args[i] == "--zero-prefix-ids")
            {
                zeroPrefixIds = true;
                continue;
            }

            if (args[i] is "--save" or "--out-slot" or "--template" or "--target" or "--parts" or "--game")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--save": save = value; break;
                    case "--out-slot": output = value; break;
                    case "--template": template = value; break;
                    case "--target": target = value; break;
                    case "--parts": partsText = value; break;
                    default: game = value; break;
                }

                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (save is null || output is null || template is null || target is null || partsText is null)
            return Fail(json, 2, "bad_args", "用法: GameTool research generate --save <槽位> --template <键> --target <Items.X> --parts keep|auto --out-slot <新槽位目录> [--zero-prefix-ids] [--into-save-root]");
        PartsMode mode;
        if (partsText == "keep")
            mode = PartsMode.Keep;
        else if (partsText == "auto")
            mode = PartsMode.Auto;
        else
            return Fail(json, 2, "bad_args", "--parts 只能是 keep 或 auto。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        ResearchService.ValidateInsertOutput(saveFile, output, intoSaveRoot);
        var full = Path.GetFullPath(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        game ??= SteamGameLocator.FindInstalledGame();
        var catalog = string.IsNullOrWhiteSpace(game) ? null : ItemNameCatalog.Load(game);
        var names = catalog?.Available == true ? catalog : null;
        if (mode == PartsMode.Auto && names is null)
            return Fail(json, 1, "failed", "--parts auto 需要 tweak 源：" + ItemNameCatalog.RedmodUnavailable);
        if (names is not null && names.Resolve(target).RecordName is null)
            return Fail(json, 1, "failed", $"tweak 源里找不到 {target}。");

        var container = SaveContainer.Read(saveFile);
        var generation = ItemGenerator.GenerateFromTemplate(container, template, target, mode, names?.PresetParts(target), zeroPrefixIds);
        var sourceDir = Path.GetDirectoryName(saveFile) ?? throw new ResearchRejectedException("无法确定源槽位目录。");
        foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
        {
            if (!File.Exists(Path.Combine(sourceDir, name)))
                throw new ResearchRejectedException($"源槽位缺少 {name}。");
        }

        Directory.CreateDirectory(full);
        try
        {
            foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
                File.Copy(Path.Combine(sourceDir, name), Path.Combine(full, name));
            var destination = Path.Combine(full, "sav.dat");
            using (var stream = File.Create(destination))
                container.Write(stream);
            adapter.Load(destination);
            var inventory = InventoryResearch.Read(adapter, destination, InventoryResearch.DefaultBagId);
            if (inventory.Items.Count != generation.PreviousPlayerCount + 1)
                throw new ResearchRejectedException("自检失败：玩家背包物品数没有加 1。");
            var inserted = inventory.Items[^1];
            if (!string.Equals(inserted.HeaderHex, generation.HeaderHex, StringComparison.OrdinalIgnoreCase) || inserted.NodeIndex != generation.NodeIndex)
                throw new ResearchRejectedException("自检失败：新物品不在玩家背包末尾。");
            var written = SaveContainer.Read(destination);
            var node = NodeTable.Parse(written.CopyNodeTable())[generation.NodeIndex];
            if (node.Length != generation.BodyLength)
                throw new ResearchRejectedException("自检失败：新 itemData 的长度不对。");
            ItemPartTree.Parse(written.Payload.Slice(node.PayloadOffset, node.Length));
            ResearchService.RequireSingleItemInsertion(ResearchService.Diff(adapter, saveFile, destination), generation.AddedBytes);
        }
        catch
        {
            Directory.Delete(full, recursive: true);
            throw;
        }

        var named = generation with
        {
            PartList = generation.PartList
                .Select(part => part with
                {
                    Record = names?.Resolve(part.TweakDbIdHex).RecordName,
                    SlotName = names?.Resolve(part.SlotHex).RecordName
                })
                .ToArray()
        };
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, named).ToJson());
        else
        {
            Console.WriteLine($"self-check: pass node={named.NodeIndex} bytes={named.AddedBytes} header={named.HeaderHex}");
            Console.WriteLine($"target={named.Target} parts={named.Parts} zeroPrefixIds={named.ZeroPrefixIds.ToString().ToLowerInvariant()} seed={named.Seed:x8} length {named.TemplateLength} -> {named.BodyLength}");
            foreach (var part in named.PartList)
                Console.WriteLine($"{new string(' ', part.Depth * 2)}- {part.Record ?? part.TweakDbIdHex} slot={part.SlotName ?? part.SlotHex} seed={part.Seed:x8}{(part.FreshSeed ? " new" : "")}");
        }

        return 0;
    }

    private static int ResearchParts(string[] args, bool json)
    {
        string? save = null;
        string? game = null;
        var withNames = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--names")
            {
                withNames = true;
                continue;
            }

            if (args[i] is "--save" or "--game")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                if (args[i] == "--save")
                    save = args[++i];
                else
                    game = args[++i];
                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (save is null)
            return Fail(json, 2, "bad_args", "research parts 需要 --save。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        ItemNameCatalog? names = null;
        if (withNames)
        {
            game ??= SteamGameLocator.FindInstalledGame();
            if (string.IsNullOrWhiteSpace(game))
                return Fail(json, 1, "failed", "找不到游戏目录，请用 --game 指定。");
            var catalog = ItemNameCatalog.Load(game);
            names = catalog.Available ? catalog : null;
            if (names is null && !json)
                Console.WriteLine(ItemNameCatalog.RedmodUnavailable);
        }

        var scans = ItemGenerator.ScanParts(SaveContainer.Read(saveFile))
            .Select(scan => scan with
            {
                Parts = scan.Parts
                    .Select(part => part with
                    {
                        Record = names?.Resolve(part.TweakDbIdHex).RecordName,
                        SlotName = names?.Resolve(part.SlotHex).RecordName
                    })
                    .ToArray()
            })
            .ToArray();
        var parsed = scans.Count(scan => scan.Error is null);
        var exact = scans.Count(scan => scan.RoundTrip && scan.RootMatchesHeader);
        if (json)
        {
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { Total = scans.Length, Parsed = parsed, Exact = exact, Items = scans }).ToJson());
            return 0;
        }

        foreach (var scan in scans)
        {
            var record = names?.Resolve(scan.TweakDbIdHex).RecordName ?? scan.TweakDbIdHex;
            var state = scan.Error ?? (scan.RoundTrip && scan.RootMatchesHeader ? "ok" : $"roundTrip={scan.RoundTrip} rootMatchesHeader={scan.RootMatchesHeader}");
            Console.WriteLine($"{scan.Key} {record} length={scan.Length} parts={scan.Parts.Count} {state}");
            foreach (var part in scan.Parts)
                Console.WriteLine($"  {new string(' ', part.Depth * 2)}- {part.Record ?? part.TweakDbIdHex} slot={part.SlotName ?? part.SlotHex} seed={part.Seed:x8}");
        }

        Console.WriteLine($"total={scans.Length} parsed={parsed} exact={exact}");
        return 0;
    }

    private static int ResearchRemove(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? save = null;
        string? output = null;
        string? tweak = null;
        var intoSaveRoot = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                continue;
            if (args[i] == "--into-save-root")
            {
                intoSaveRoot = true;
                continue;
            }

            if (args[i] is "--save" or "--out" or "--stack-tdb")
            {
                if (i + 1 >= args.Length)
                    return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--save":
                        save = value;
                        break;
                    case "--out":
                        output = value;
                        break;
                    default:
                        tweak = value;
                        break;
                }

                continue;
            }

            return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
        }

        if (save is null || output is null || tweak is null)
            return Fail(json, 2, "bad_args", "research remove 需要 --save、--out 和 --stack-tdb。");
        if (!TryResolveResearchSave(save, out var saveFile, out var error))
            return Fail(json, 1, "save_not_found", error);
        ResearchService.ValidateInsertOutput(saveFile, output, intoSaveRoot);
        var full = Path.GetFullPath(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var container = SaveContainer.Read(saveFile);
        var removal = ItemInserter.RemoveStack(container, tweak);
        var sourceDir = Path.GetDirectoryName(saveFile) ?? throw new ResearchRejectedException("无法确定源槽位目录。");
        foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
        {
            if (!File.Exists(Path.Combine(sourceDir, name)))
                throw new ResearchRejectedException($"源槽位缺少 {name}。");
        }

        Directory.CreateDirectory(full);
        try
        {
            foreach (var name in new[] { "metadata.9.json", "screenshot.png" })
                File.Copy(Path.Combine(sourceDir, name), Path.Combine(full, name));
            var destination = Path.Combine(full, "sav.dat");
            using (var stream = File.Create(destination))
                container.Write(stream);
            var loaded = adapter.Load(destination);
            var resource = adapter.Resources.FirstOrDefault(item => item.Locator == "tdb:" + tweak);
            if (resource is not null)
            {
                if (loaded.GetQuantity(resource.Id) is not null)
                    throw new ResearchRejectedException("自检失败：该资源仍然持有。");
            }
            else
            {
                var id = Convert.ToHexString(BitConverter.GetBytes(TweakDbId.FromName(tweak))).ToLowerInvariant();
                var stillHeld = InventoryResearch.Read(adapter, destination, InventoryResearch.DefaultBagId).Items
                    .Any(item => item.TweakDbId == id);
                if (stillHeld)
                    throw new ResearchRejectedException("自检失败：该资源仍然持有。");
            }

            ResearchService.RequireSingleStackRemoval(
                adapter.OpenForResearch(saveFile),
                adapter.OpenForResearch(destination),
                removal.RemovedBytes,
                removal.NodeIndex,
                Cp2077Format.DecompressedChunkSize);
        }
        catch
        {
            Directory.Delete(full, recursive: true);
            throw;
        }

        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, removal).ToJson());
        else
            Console.WriteLine($"self-check: pass node={removal.NodeIndex} bytes={removal.RemovedBytes} removed={removal.TweakName}");
        return 0;
    }

    private static int ResearchLoc(Cp2077Adapter adapter, string[] args, bool json)
    {
        string? lang = null;
        string? game = null;
        string? output = null;
        string? key = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is not ("--lang" or "--game" or "--out" or "--key"))
                return Fail(json, 2, "bad_args", $"未知参数 {args[i]}。");
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                return Fail(json, 2, "bad_args", $"缺少 {args[i]} 的值。");
            var value = args[++i];
            switch (args[i - 1])
            {
                case "--lang": lang = value; break;
                case "--game": game = value; break;
                case "--out": output = value; break;
                default: key = value; break;
            }
        }

        if (lang is null)
            return Fail(json, 2, "bad_args", "research loc 需要 --lang。");
        if ((output is null) == (key is null))
            return Fail(json, 2, "bad_args", "research loc 需要 --out 或 --key 其中之一。");
        game ??= SteamGameLocator.FindInstalledGame();
        if (string.IsNullOrWhiteSpace(game))
            return Fail(json, 1, "failed", "找不到游戏目录，请用 --game 指定。");
        if (output is not null)
            LocalizationCatalog.RequireExportDirectory(output, game, adapter.DefaultSaveRoot);

        var text = LocalizationCatalog.Load(game, lang);
        var entries = text.Entries;
        if (key is not null)
        {
            var found = LocalizationCatalog.Find(entries, key);
            if (found.Count == 0)
                return Fail(json, 1, "failed", "没有匹配的条目。");
            if (json)
                Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, found).ToJson());
            else
            {
                foreach (var entry in found)
                    Console.WriteLine($"LocKey#{entry.PrimaryKey}\t{entry.SecondaryKey}\t{entry.FemaleVariant}\t{entry.MaleVariant}");
            }

            return 0;
        }

        var exported = LocalizationCatalog.Export(text, output!, lang);
        if (json)
            Console.WriteLine(JsonEnvelope.Ok(Cp2077Adapter.Game, new { text.BaseCount, text.Ep1Count, text.Overridden, Count = text.Entries.Count, Output = exported }).ToJson());
        else
            Console.WriteLine($"base={text.BaseCount} ep1={text.Ep1Count} overridden={text.Overridden} entries={text.Entries.Count}{Environment.NewLine}{exported}");
        return 0;
    }

    private static void WriteSummary(ResearchDiffResult diff, DiffSummary summary)
    {
        foreach (var field in diff.Header)
            Console.WriteLine($"header {field.Name}: {field.Left ?? "—"} -> {field.Right ?? "—"}");
        foreach (var row in summary.Changes)
            Console.WriteLine($"{row.Name} #{row.LeftIndex} -> #{row.RightIndex} length {row.LeftLength} -> {row.RightLength} bytes={row.ChangedBytes} spans={row.SpanCount}");
        foreach (var region in summary.Added)
            Console.WriteLine($"+ {region.Name} #{region.RightIndex} length={region.RightLength}");
        foreach (var region in summary.Removed)
            Console.WriteLine($"- {region.Name} #{region.LeftIndex} length={region.LeftLength}");
        Console.WriteLine($"indexShiftOnly: {summary.IndexShiftOnly}");
        Console.WriteLine($"changed={summary.ChangedCount} added={summary.AddedCount} removed={summary.RemovedCount} indexShiftOnly={summary.IndexShiftOnly}");
    }

    private static void WriteInventory(ResearchInventory inventory)
    {
        Console.WriteLine($"bag {inventory.BagId}");
        foreach (var item in inventory.Items)
        {
            var quantity = item.Stack ? $" quantity={item.Quantity}" : "";
            var stack = item.Stack ? " stack" : "";
            Console.WriteLine($"#{item.Ordinal} node={item.NodeIndex} header={item.HeaderHex} tdb={item.TweakDbId} name={item.Name ?? "—"} length={item.Length}{stack}{quantity}");
        }
    }

    private static void WriteInventoryCompare(InventoryCompare compare)
    {
        Console.WriteLine($"bag {compare.BagId}");
        foreach (var change in compare.Changes)
        {
            var mark = change.Kind switch
            {
                "added" => "+",
                "removed" => "-",
                _ => "~"
            };
            Console.WriteLine($"{mark} #{change.LeftOrdinal?.ToString() ?? "—"} -> #{change.RightOrdinal?.ToString() ?? "—"} node {change.LeftNode?.ToString() ?? "—"} -> {change.RightNode?.ToString() ?? "—"} header={change.HeaderHex} tdb={change.TweakDbId} name={change.Name ?? "—"} length {change.LeftLength?.ToString() ?? "—"} -> {change.RightLength?.ToString() ?? "—"}");
        }
    }

    private static void WriteDiff(ResearchDiffResult diff)
    {
        foreach (var field in diff.Header)
            Console.WriteLine($"header {field.Name}: {field.Left ?? "—"} -> {field.Right ?? "—"}");
        foreach (var region in diff.Regions)
        {
            var title = region.Kind switch
            {
                "added" => $"+ {region.Name} #{region.RightIndex} length={region.RightLength}",
                "removed" => $"- {region.Name} #{region.LeftIndex} length={region.LeftLength}",
                "resized" => $"{region.Name} #{region.LeftIndex} -> #{region.RightIndex} length {region.LeftLength} -> {region.RightLength} first={region.FirstDifference}",
                "indexShiftOnly" => $"{region.Name} #{region.LeftIndex} -> #{region.RightIndex} indexShiftOnly",
                _ => $"{region.Name} #{region.LeftIndex} -> #{region.RightIndex}"
            };
            Console.WriteLine(title);
            foreach (var span in region.Spans)
                Console.WriteLine($"  [{span.Start}, {span.End}) {span.Left} -> {span.Right}");
        }
    }

    private static void WriteFind(ResearchFindResult found)
    {
        foreach (var hit in found.Hits)
        {
            var region = hit.Region is null ? "—" : hit.Region + " #" + hit.RegionIndex;
            Console.WriteLine($"offset={hit.Offset} region={region} relative={hit.Relative}");
        }

        if (found.Truncated)
            Console.WriteLine($"已截断 total={found.Total} shown={found.Hits.Count}");
    }

    private static bool TryResolveResearchSave(string path, out string saveFile, out string error)
    {
        if (File.Exists(path))
        {
            saveFile = Path.GetFullPath(path);
            error = "";
            return true;
        }

        if (Directory.Exists(path))
        {
            var nested = Path.Combine(path, "sav.dat");
            if (File.Exists(nested))
            {
                saveFile = Path.GetFullPath(nested);
                error = "";
                return true;
            }

            saveFile = "";
            error = $"槽位目录里没有 sav.dat：{path}。";
            return false;
        }

        saveFile = "";
        error = $"找不到存档 {path}。";
        return false;
    }

    private sealed record SmokeData(
        bool IdentityWrite,
        bool RecompressRoundtrip,
        bool OriginalUntouched,
        bool InventoryParsed,
        bool PatchFlow,
        bool WatchFlow,
        bool ResearchFlow,
        bool InsertFlow,
        bool CloneFlow,
        bool GenerateFlow,
        string? GenerateNote,
        long WriteCompleteToDoneMs);

    private const string GenerateSkipped = "skipped:";

    /// <summary>
    /// 在临时副本上各生成 1 件未拥有的服装、远程武器、近战武器、义体、手雷、手臂义体、快速破解、模组和网络接入仓（T023）：
    /// 严格校验由 PatchEngine 完成，这里再核对玩家物品数 +9、资源数量不变；
    /// 再经物品库获得 3 个堆叠物品和 1 份配方（见 StackObtainFlow），并核对按卡片分页的目录（同名合并）与逐条目录的条目数一致。
    /// 没有 tweak 源（未装 REDmod）时按「跳过」计为通过并注明原因。
    /// </summary>
    private static string GenerateFlow(Cp2077Adapter adapter, string tempRoot, byte[] original)
    {
        var slot = Path.Combine(tempRoot, "generate");
        Directory.CreateDirectory(slot);
        var path = Path.Combine(slot, "sav.dat");
        File.WriteAllBytes(path, original);
        var before = (Cp2077SaveDocument)adapter.Load(path);
        if (!before.GenerateAvailable)
            return GenerateSkipped + " " + (before.GenerateMessage ?? "no tweak source");
        var clothing = PickUnowned(before, "clothing", "Items.Jacket_01_basic_01");
        var ranged = PickUnowned(before, "ranged", "Items.Preset_Ajax_Default");
        var melee = PickUnowned(before, "melee", "Items.Preset_Katana_Default");
        var cyberware = PickUnowned(before, "cyberware", "Items.AdvancedRamUpgradeLegendaryPlusPlus");
        var grenade = PickUnowned(before, "grenade", "Items.GrenadeFragEpicPlus");
        var arm = PickUnowned(before, "arm_cyberware", "Items.AdvancedMantisBladesLegendaryPlus");
        var quickhack = PickUnowned(before, "quickhack", "Items.ContagionLvl4Program");
        var mod = PickUnowned(before, "mod", "Items.PowerMod2_Legendary");
        var cyberdeck = PickUnowned(before, "cyberware", "Items.AdvancedArasakaShadowMKIVEpic", "网络接入仓");
        string?[] targets = [clothing, ranged, melee, cyberware, grenade, arm, quickhack, mod, cyberdeck];
        if (targets.Any(target => target is null))
            return "no-target";
        var quantities = adapter.Resources.Select(resource => (resource.Id, Quantity: before.GetQuantity(resource.Id))).ToArray();
        try
        {
            foreach (var target in targets)
            {
                var report = PatchEngine.Generate(adapter, path, new GenerateRequest(target!, 1), new PatchOptions(path + ".bak"));
                if (report.Patch.Outcome != PatchOutcome.Patched || report.Seeds.Count != 1)
                    return "outcome-" + target;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or PatchRejectedException)
        {
            return "failed: " + ex.Message;
        }

        var after = (Cp2077SaveDocument)adapter.Load(path);
        if (after.PlayerItemCount != before.PlayerItemCount + targets.Length)
            return "count";
        if (quantities.Any(item => after.GetQuantity(item.Id) != item.Quantity))
            return "quantity";
        var groups = after.ListGeneratableGroups(null, null, 0, 1);
        if (groups.ItemTotal != after.ListGeneratable(null, null, 0, 1).Total || groups.Total == 0 || groups.Total >= groups.ItemTotal)
            return "groups";
        return StackObtainFlow(adapter, tempRoot, path, quantities);
    }

    /// <summary>
    /// 物品库的堆叠路径：经 LibraryService 获得 3 个不在内置资源表里的消耗品（未持有就插入新堆叠，已持有就加数量），
    /// 再获得 1 份未持有的配方（T023，插入 29 字节堆叠），核对数量、获得没有生成备份（D019）、内置资源数量不变。
    /// </summary>
    private static string StackObtainFlow(Cp2077Adapter adapter, string tempRoot, string path, (ResourceId Id, long? Quantity)[] quantities)
    {
        var document = (Cp2077SaveDocument)adapter.Load(path);
        var target = document.ListGeneratable("consumable", null, 0, GenerateService.MaxTake).Items
            .FirstOrDefault(item => (item.Held ?? 0) + 3 <= (item.MaxQuantity ?? 0));
        if (target is null)
            return "no-stack-target";
        var recipe = document.ListGeneratable("recipe", null, 0, GenerateService.MaxTake).Items.FirstOrDefault(item => item.Held is null);
        if (recipe is null)
            return "no-recipe-target";
        var backups = new BackupStore(Path.Combine(tempRoot, "generate-backups"));
        var enable = new EnableService(backups, _ => { });
        try
        {
            var library = new LibraryService(backups, enable);
            var report = library.Obtain(adapter, path, target.Target, 3);
            if (report.After != (target.Held ?? 0) + 3 || backups.List(adapter.GameId).Count != 0)
                return "stack-report";
            var learned = library.Obtain(adapter, path, recipe.Target, 1);
            if (!learned.Inserted || learned.After != 1 || backups.List(adapter.GameId).Count != 0)
                return "recipe-report";
        }
        catch (Exception ex) when (ex is InvalidDataException or PatchRejectedException)
        {
            return "stack-failed: " + ex.Message;
        }
        finally
        {
            enable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var after = (Cp2077SaveDocument)adapter.Load(path);
        if (after.FindGeneratable(target.Target)?.Held != (target.Held ?? 0) + 3)
            return "stack-held";
        if (after.FindGeneratable(recipe.Target)?.Held != 1)
            return "recipe-held";
        if (quantities.Any(item => after.GetQuantity(item.Id) != item.Quantity))
            return "stack-quantity";
        return "pass";
    }

    private static string? PickUnowned(Cp2077SaveDocument document, string kind, string preferred, string? fallbackQuery = null)
    {
        var exact = document.ListGeneratable(kind, preferred, 0, GenerateService.MaxTake).Items
            .FirstOrDefault(item => item.Target == preferred && !item.Owned);
        if (exact is not null)
            return exact.Target;
        for (var skip = 0; ; skip += GenerateService.MaxTake)
        {
            var page = document.ListGeneratable(kind, fallbackQuery, skip, GenerateService.MaxTake);
            var free = page.Items.FirstOrDefault(item => !item.Owned);
            if (free is not null)
                return free.Target;
            if (skip + page.Items.Count >= page.Total || page.Items.Count == 0)
                return null;
        }
    }

    private static string CloneFlow(Cp2077Adapter adapter, string tempRoot, byte[] original)
    {
        var slot = Path.Combine(tempRoot, "clone");
        Directory.CreateDirectory(slot);
        var path = Path.Combine(slot, "sav.dat");
        File.WriteAllBytes(path, original);
        var before = (Cp2077SaveDocument)adapter.Load(path);
        var gear = before.ListCloneable();
        if (gear.Count == 0)
            return "no-gear";
        var quantities = adapter.Resources.Select(resource => (resource.Id, Quantity: before.GetQuantity(resource.Id))).ToArray();
        var report = PatchEngine.Clone(adapter, path, new CloneRequest(gear[0].Key, 1), new PatchOptions(path + ".bak"));
        if (report.Patch.Outcome != PatchOutcome.Patched || report.Seeds.Count != 1)
            return "outcome";
        var after = (Cp2077SaveDocument)adapter.Load(path);
        if (after.PlayerItemCount != before.PlayerItemCount + 1)
            return "count";
        if (quantities.Any(item => after.GetQuantity(item.Id) != item.Quantity))
            return "quantity";
        var seed = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(seed, report.Seeds[0]);
        var payload = SaveContainer.Read(path).Payload.ToArray();
        var hits = 0;
        for (var i = 0; i <= payload.Length - 4; i++)
        {
            if (payload.AsSpan(i, 4).SequenceEqual(seed))
                hits++;
        }

        return hits == 2 ? "pass" : "seed-" + hits;
    }

    private static string InsertFlow(Cp2077Adapter adapter, string tempRoot, byte[] original)
    {
        var slot = Path.Combine(tempRoot, "insert");
        Directory.CreateDirectory(slot);
        var path = Path.Combine(slot, "sav.dat");
        File.WriteAllBytes(path, original);
        var before = (Cp2077SaveDocument)adapter.Load(path);
        var working = (Cp2077SaveDocument)adapter.Load(path);
        const string preferred = "Items.GoodQualityFood1";
        var preferredId = Convert.ToHexString(BitConverter.GetBytes(TweakDbId.FromName(preferred))).ToLowerInvariant();
        var held = InventoryResearch.Read(adapter, path, InventoryResearch.DefaultBagId).Items
            .Any(entry => entry.TweakDbId == preferredId);
        var name = held ? "Items.GameToolSmokeInsert" : preferred;
        working.InsertTweak(name, 3);
        working.VerifyNamedInsertion(before, name, 3);
        var output = Path.Combine(slot, "out.dat");
        using (var stream = File.Create(output))
            working.Save(stream);

        var reloaded = SaveContainer.Read(output);
        var tweak = Convert.ToHexString(BitConverter.GetBytes(TweakDbId.FromName(name))).ToLowerInvariant();
        var item = InventoryResearch.Read(adapter, output, InventoryResearch.DefaultBagId).Items
            .SingleOrDefault(entry => entry.TweakDbId == tweak);
        if (item is null || item.Quantity != 3 || item.Length != 29)
            return "reload";
        var diff = ResearchService.Diff(adapter, path, output);
        ResearchService.RequireSingleItemInsertion(diff, 45);
        if (reloaded.DecompressedLength != SaveContainer.Read(path).DecompressedLength + 45)
            return "length";
        return "pass";
    }
}
