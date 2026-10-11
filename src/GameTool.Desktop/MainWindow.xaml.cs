using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.App;
using GameTool.Core;
using Microsoft.Web.WebView2.Core;

namespace GameTool.Desktop;

/// <summary>启动参数：路径来自命令行（自检）或用户设置（D020）。空字符串表示没有给。</summary>
public sealed record MainWindowOptions(
    string SaveRoot,
    string BackupRoot,
    string SkinRoot,
    string GameDir,
    string SettingsFile,
    string Pristine,
    bool SelfTest,
    bool FullSelfTest,
    string Show);

public partial class MainWindow : Window
{
    private const string Origin = "https://gametool.local/";
    private readonly string _saveRoot;
    private readonly string _backupRoot;
    private readonly string _skinRoot;
    private readonly string _gameDir;
    private readonly string _settingsFile;
    private readonly string _pristine;
    private readonly string _show;
    private readonly bool _selfTest;
    private readonly bool _fullSelfTest;
    private readonly DispatcherTimer _timeout;
    private BridgeDispatcher? _bridge;
    private AdapterRegistry? _registry;
    private bool _selfTestPassed;

    public MainWindow(MainWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _saveRoot = options.SaveRoot;
        _backupRoot = options.BackupRoot;
        _skinRoot = options.SkinRoot;
        _gameDir = options.GameDir;
        _settingsFile = options.SettingsFile;
        _pristine = options.Pristine;
        _show = options.Show;
        _selfTest = options.SelfTest;
        _fullSelfTest = options.FullSelfTest;
        var fullSelfTest = options.FullSelfTest;
        InitializeComponent();
        _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(fullSelfTest ? 120 : 15) };
        _timeout.Tick += (_, _) => FinishSelfTest(1, fullSelfTest ? "120 秒内完整自检没有完成。" : "15 秒内没有收到 app.ping 应答。");
        Loaded += async (_, _) => await StartAsync();
        SourceInitialized += (_, _) => WindowChromeHost.Attach(this);
        StateChanged += (_, _) =>
        {
            FitMaximized();
            PushWindowState();
        };
        SizeChanged += (_, _) => FitMaximized();
        LocationChanged += (_, _) => FitMaximized();
        Activated += (_, _) => PushWindowState();
        Deactivated += (_, _) => PushWindowState();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>最大化时把页面缩进到显示器工作区以内（见 <see cref="WindowChromeHost.MaximizedOverflow"/>），还原后贴满窗口。</summary>
    private void FitMaximized()
    {
        var overflow = WindowChromeHost.MaximizedOverflow(this);
        if (View.Margin != overflow)
            View.Margin = overflow;
    }

    /// <summary>Alt+空格：系统标题栏没了，焦点又在 WebView2 里，这里自己弹出窗口的系统菜单（还原、移动、大小、最小化、最大化、关闭）。</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.System || e.SystemKey != System.Windows.Input.Key.Space)
            return;
        e.Handled = true;
        ShowSystemMenu();
    }

    private void ShowSystemMenu()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;
        var device = PointToScreen(new Point(8, 34));
        SystemCommands.ShowSystemMenu(this, source.CompositionTarget.TransformFromDevice.Transform(device));
    }

    /// <summary>窗口状态推给页面：最大化时按钮换成「还原」图标、隐藏缩放边；失去焦点时按钮变淡。</summary>
    private void PushWindowState()
    {
        if (View.CoreWebView2 is null)
            return;
        Push(System.Text.Json.JsonSerializer.Serialize(new { @event = "window", data = WindowStatePayload() }));
    }

    private object WindowStatePayload() => new
    {
        maximized = WindowState == WindowState.Maximized,
        active = IsActive
    };

    /// <summary>
    /// 一体式标题栏的按钮和缩放边（D021）：window.minimize / toggleMaximize / close / state / startResize。
    /// 在界面线程直接处理，不经过桥接；返回 false 表示不是窗口方法。
    /// </summary>
    private bool TryHandleWindow(string requestJson)
    {
        string? method;
        string? id;
        string? edge = null;
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            method = root.TryGetProperty("method", out var methodValue) && methodValue.ValueKind == JsonValueKind.String ? methodValue.GetString() : null;
            if (method is null || !method.StartsWith("window.", StringComparison.Ordinal))
                return false;
            id = root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
            if (root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("edge", out var edgeValue) && edgeValue.ValueKind == JsonValueKind.String)
                edge = edgeValue.GetString();
        }
        catch (JsonException)
        {
            return false;
        }

        switch (method)
        {
            case "window.state":
                Reply(id, true, null, WindowStatePayload());
                break;
            case "window.minimize":
                Reply(id, true, null);
                SystemCommands.MinimizeWindow(this);
                break;
            case "window.toggleMaximize":
                Reply(id, true, null);
                if (WindowState == WindowState.Maximized)
                    SystemCommands.RestoreWindow(this);
                else
                    SystemCommands.MaximizeWindow(this);
                break;
            case "window.close":
                Reply(id, true, null);
                Close();
                break;
            case "window.systemMenu":
                Reply(id, true, null);
                ShowSystemMenu();
                break;
            case "window.startResize":
                if (!WindowChromeHost.IsEdge(edge))
                {
                    Reply(id, false, "未知的窗口边缘。");
                    break;
                }

                Reply(id, true, null);
                // 等这次消息处理返回后再进入系统的缩放循环，WebView2 不会被卡在回调里。
                Dispatcher.BeginInvoke(() => WindowChromeHost.BeginResize(this, edge!));
                break;
            default:
                Reply(id, false, "未知的窗口方法 " + method + "。");
                break;
        }

        return true;
    }

    private async Task StartAsync()
    {
        try
        {
            if (_selfTest)
                _timeout.Start();
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameTool",
                "WebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userData);
            await View.EnsureCoreWebView2Async(environment);
            var core = View.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            // D021：页面顶栏用 CSS app-region: drag 充当标题栏（拖动、双击最大化 / 还原、右键系统菜单、Aero 贴边都由系统处理）。
            core.Settings.IsNonClientRegionSupportEnabled = true;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
#endif
            // 只显示内置页面：非本地内容的导航一律取消，新窗口（target=_blank、window.open）一律不开。
            // 作者主页（D024）由宿主用系统浏览器打开，不在 WebView2 里导航。
            core.NavigationStarting += (_, args) =>
            {
                if (!IsLocalPage(args.Uri))
                    args.Cancel = true;
            };
            core.FrameNavigationStarting += (_, args) =>
            {
                if (!IsLocalPage(args.Uri))
                    args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) => Serve(args);
            core.WebMessageReceived += (_, args) => OnMessage(args.WebMessageAsJson);
            var registry = new AdapterRegistry();
            registry.Register(new Cp2077Adapter());
            _registry = registry;
            _bridge = new BridgeDispatcher(
                registry,
                string.IsNullOrWhiteSpace(_saveRoot) ? null : _saveRoot,
                Push,
                string.IsNullOrWhiteSpace(_backupRoot) ? null : _backupRoot,
                string.IsNullOrWhiteSpace(_skinRoot) ? null : _skinRoot,
                new SettingsStore(string.IsNullOrWhiteSpace(_settingsFile) ? null : _settingsFile),
                string.IsNullOrWhiteSpace(_gameDir) ? null : _gameDir,
                PickFolder,
                urlLauncher: _selfTest ? _selfTestLauncher : new ShellUrlLauncher());
            // T030：游戏存档后自动刷新存档列表（宿主监视存档根目录，去抖后推送 saves.changed），随桥接一起在关闭时释放。
            _bridge.WatchSaveRoots();
            var query = new List<string>();
            if (_fullSelfTest)
                query.Add("selftest=full");
            else if (_selfTest)
                query.Add("selftest=1");
            if (!string.IsNullOrWhiteSpace(_show))
                query.Add("show=" + Uri.EscapeDataString(_show));
            var start = Origin + "index.html" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
            core.Navigate(start);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // 启动时检查通过、但创建时才发现运行时不可用（如刚被卸载）：与启动检查同样的中文提示，然后退出。
            Hide();
            App.ReportMissingWebView2(_selfTest);
            if (_selfTest)
                FinishSelfTest(2, App.MissingWebView2Message);
            else
                Application.Current.Shutdown(2);
        }
        catch (Exception ex)
        {
            if (_selfTest)
                FinishSelfTest(1, ex.ToString());
            else
                MessageBox.Show(PathDisplay.Current.Mask(ex.Message), "游戏工具", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsLocalPage(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("gametool.local", StringComparison.OrdinalIgnoreCase);

    /// <summary>自检用的假浏览器启动器（D024）：只记下要打开的地址，绝不真的打开浏览器。</summary>
    private sealed class RecordingUrlLauncher : IUrlLauncher
    {
        private readonly List<string> _opened = [];

        public void Open(string url)
        {
            lock (_opened)
                _opened.Add(url);
        }

        public string[] Opened()
        {
            lock (_opened)
                return [.. _opened];
        }
    }

    private readonly RecordingUrlLauncher _selfTestLauncher = new();

    private void OnMessage(string requestJson)
    {
        if (TryHandleWindow(requestJson) || TryHandleSelfTest(requestJson))
            return;
        var bridge = _bridge;
        if (bridge is null)
            return;
        Task.Run(() =>
        {
            var response = bridge.Dispatch(requestJson);
            Dispatcher.BeginInvoke(() =>
            {
                View.CoreWebView2?.PostWebMessageAsJson(response);
                if (_selfTest && !_fullSelfTest && !_selfTestPassed && IsPingOk(response))
                    FinishSelfTest(0, "app.ping 应答已收到。");
            });
        });
    }

    private bool TryHandleSelfTest(string requestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodValue) ? methodValue.GetString() : null;
            if (method is not ("selftest.step" or "selftest.overwrite" or "selftest.hash" or "selftest.done" or "selftest.leak" or "selftest.fakeGame" or "selftest.launched" or "selftest.newSlot"))
                return false;
            var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            var parameters = root.TryGetProperty("params", out var parameterValue) ? parameterValue : default;
            if (method == "selftest.overwrite")
            {
                var destination = Path.Combine(_saveRoot, "ManualSave-0", "sav.dat");
                File.Copy(_pristine, destination, overwrite: true);
                LogSelfTest("overwrite", true, destination);
            }
            else if (method == "selftest.hash")
            {
                var backupId = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("backupId", out var backupValue)
                    ? backupValue.GetString()
                    : null;
                var match = HashesMatch(backupId);
                LogSelfTest("hash", match, backupId ?? "");
                Reply(id, true, null, new { Match = match });
                return true;
            }
            else if (method == "selftest.fakeGame")
            {
                if (!_selfTest)
                    return false;
                // D023：在临时目录里搭两个最小的假游戏目录（只有空占位文件，不碰真实游戏目录），供页面走桥接校验往日之影检测。
                var (baseOnly, withExpansion) = CreateFakeGames();
                Reply(id, true, null, new { BaseOnly = baseOnly, WithExpansion = withExpansion });
                return true;
            }
            else if (method == "selftest.newSlot")
            {
                if (!_fullSelfTest)
                    return false;
                // T030：在自检沙箱的存档目录里新建一个槽位（复制原始副本），页面不做任何操作，存档列表应自动出现它。
                const string name = "ManualSave-8";
                var directory = Path.Combine(_saveRoot, name);
                Directory.CreateDirectory(directory);
                File.Copy(_pristine, Path.Combine(directory, "sav.dat"), overwrite: true);
                LogSelfTest("newSlot", true, directory);
                Reply(id, true, null, new { Name = name });
                return true;
            }
            else if (method == "selftest.launched")
            {
                if (!_selfTest)
                    return false;
                // D024：假启动器收到了几次请求、是否全是固定的作者主页（页面自己不知道这个地址）。
                var opened = _selfTestLauncher.Opened();
                Reply(id, true, null, new { Count = opened.Length, FixedOnly = opened.All(url => url == AuthorPage.Url) });
                return true;
            }
            else if (method == "selftest.leak")
            {
                // D021：页面把界面上显示的全部文字送来，这里检查里面有没有真实的用户目录（页面自己不知道用户目录是什么）。
                var text = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("text", out var textValue)
                    ? textValue.GetString() ?? ""
                    : "";
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var clean = string.IsNullOrEmpty(profile) || !text.Contains(profile, StringComparison.OrdinalIgnoreCase);
                Reply(id, true, null, new { Clean = clean, Length = text.Length });
                return true;
            }
            else if (method == "selftest.done")
            {
                var ok = parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("ok", out var okValue)
                    && okValue.ValueKind == JsonValueKind.True;
                var detail = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("detail", out var detailValue)
                    ? detailValue.ToString()
                    : "";
                LogSelfTest("done", ok, detail);
                Reply(id, true, null);
                FinishSelfTest(ok ? 0 : 1, detail);
                return true;
            }
            else
            {
                var name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString()
                    : "step";
                var ok = parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("ok", out var okValue)
                    && okValue.ValueKind == JsonValueKind.True;
                var detail = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("detail", out var detailValue)
                    ? detailValue.GetString()
                    : "";
                LogSelfTest(name ?? "step", ok, detail ?? "");
            }

            Reply(id, true, null);
            return true;
        }
        catch (Exception ex)
        {
            LogSelfTest("host", false, ex.Message);
            return false;
        }
    }

    private string? _fakeGameRoot;

    /// <summary>
    /// 两个假游戏目录：都有 bin\x64\Cyberpunk2077.exe 和 archive\pc\content（空占位）；第二个另有 archive\pc\ep1\ep1_2_gamedata.archive 和 r6\cache\tweakdb_ep1.bin（各 1 字节）。
    /// 全是本进程新建的普通文件，自检结束时删除。
    /// </summary>
    private (string BaseOnly, string WithExpansion) CreateFakeGames()
    {
        _fakeGameRoot ??= Path.Combine(Path.GetTempPath(), "GameTool-selftest-fakegame-" + Guid.NewGuid().ToString("N"));
        var baseOnly = FakeGame(Path.Combine(_fakeGameRoot, "base-only"), expansion: false);
        var withExpansion = FakeGame(Path.Combine(_fakeGameRoot, "with-ep1"), expansion: true);
        return (baseOnly, withExpansion);
    }

    private static string FakeGame(string root, bool expansion)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
        File.WriteAllBytes(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), []);
        Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
        if (expansion)
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "ep1"));
            File.WriteAllBytes(Path.Combine(root, "archive", "pc", "ep1", "ep1_2_gamedata.archive"), [1]);
            Directory.CreateDirectory(Path.Combine(root, "r6", "cache"));
            File.WriteAllBytes(Path.Combine(root, "r6", "cache", "tweakdb_ep1.bin"), [1]);
        }

        return root;
    }

    private void DeleteFakeGames()
    {
        if (_fakeGameRoot is null || !Directory.Exists(_fakeGameRoot))
            return;
        try
        {
            Directory.Delete(_fakeGameRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool HashesMatch(string? backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId))
            return false;
        var backupFile = Path.Combine(_backupRoot, backupId.Replace('/', Path.DirectorySeparatorChar), "sav.dat");
        var slotFile = Path.Combine(_saveRoot, "ManualSave-0", "sav.dat");
        if (!File.Exists(backupFile) || !File.Exists(slotFile))
            return false;
        using var left = File.OpenRead(slotFile);
        using var right = File.OpenRead(backupFile);
        var leftHash = System.Security.Cryptography.SHA256.HashData(left);
        var rightHash = System.Security.Cryptography.SHA256.HashData(right);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    private static void Reply(string? id, bool ok, string? error, object? data = null)
    {
        var envelope = ok
            ? new JsonEnvelope { Status = "ok", Id = id, Data = data ?? new { Ok = true } }
            : new JsonEnvelope { Status = "error", Id = id, Error = new JsonEnvelopeError("failed", error ?? "自检失败") };
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (Application.Current.MainWindow is MainWindow window)
                window.View.CoreWebView2?.PostWebMessageAsJson(envelope.ToJson());
        });
    }

    /// <summary>
    /// settings.pickFolder 的原生对话框（D020）。桥接在后台线程调用，这里切回界面线程弹出；页面只拿到选中的路径。
    /// 自检不弹对话框，当作取消。
    /// </summary>
    private string? PickFolder(string title, string? initialDirectory)
    {
        if (_selfTest)
            return null;
        return Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                dialog.InitialDirectory = initialDirectory;
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        });
    }

    private static void LogSelfTest(string name, bool ok, string detail)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(App.LogDirectory(), "selftest.log"),
                DateTime.Now.ToString("o") + " " + name + " ok=" + ok + " " + detail + Environment.NewLine);
        }
        catch (IOException)
        {
        }
    }

    private void Push(string json)
    {
        Dispatcher.BeginInvoke(() => View.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private void Serve(CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri))
            return;
        var path = uri.AbsolutePath.Trim('/');
        if (path.StartsWith("skin/", StringComparison.OrdinalIgnoreCase))
        {
            ServeSkin(args, path);
            return;
        }

        if (path.StartsWith(IconRoute.Prefix, StringComparison.Ordinal))
        {
            ServeIcon(args, uri);
            return;
        }

        if (path.Length == 0)
            path = "index.html";
        var media = path switch
        {
            "index.html" => "text/html; charset=utf-8",
            "app.css" => "text/css; charset=utf-8",
            "app.js" => "text/javascript; charset=utf-8",
            _ => ""
        };
        var stream = media.Length == 0
            ? null
            : Assembly.GetExecutingAssembly().GetManifestResourceStream("GameTool.Desktop.wwwroot." + path);
        if (stream is null)
        {
            args.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(), 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        args.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(
            stream, 200, "OK", "Content-Type: " + media + "\r\nCache-Control: no-store");
    }

    private void ServeSkin(CoreWebView2WebResourceRequestedEventArgs args, string path)
    {
        var parts = path.Split('/');
        var image = parts.Length == 3 && parts[2].Equals("background", StringComparison.OrdinalIgnoreCase)
            ? _bridge?.Skins.TryOpenImage(Uri.UnescapeDataString(parts[1]))
            : null;
        if (image is null)
        {
            args.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(), 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        args.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(image.Bytes),
            200,
            "OK",
            "Content-Type: " + image.ContentType + "\r\nCache-Control: no-store");
    }

    /// <summary>
    /// 物品图标：icon/&lt;gameId&gt;/&lt;record&gt;.png?g=f|m&amp;v=&lt;版本&gt;。记录名先过 <see cref="IconRoute"/> 的字符白名单，
    /// 提取放到后台线程（不卡界面）；带版本的地址按一年 immutable 缓存。没有图标或游戏不在本机时回 404，页面保留占位图。
    /// </summary>
    private void ServeIcon(CoreWebView2WebResourceRequestedEventArgs args, Uri uri)
    {
        if (!IconRoute.TryParse(uri.AbsolutePath, uri.Query, out var request)
            || _registry?.All.FirstOrDefault(adapter => adapter.GameId == request.GameId) is not IItemIconProvider provider)
        {
            args.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(), 404, "Not Found", "Content-Type: text/plain\r\nCache-Control: no-store");
            return;
        }

        _ = CompleteIconAsync(args, args.GetDeferral(), provider, request);
    }

    private async Task CompleteIconAsync(
        CoreWebView2WebResourceRequestedEventArgs args,
        CoreWebView2Deferral deferral,
        IItemIconProvider provider,
        IconRequest request)
    {
        byte[]? png;
        try
        {
            png = await Task.Run(() => provider.TryGetIconPng(request.RecordName, request.Gender));
        }
        catch (Exception)
        {
            png = null;
        }

        // await 回到界面线程，WebView2 的对象只能在这里用。
        try
        {
            var environment = View.CoreWebView2?.Environment;
            if (environment is not null)
            {
                args.Response = png is null
                    ? environment.CreateWebResourceResponse(new MemoryStream(), 404, "Not Found", "Content-Type: text/plain\r\nCache-Control: no-store")
                    : environment.CreateWebResourceResponse(
                        new MemoryStream(png),
                        200,
                        "OK",
                        "Content-Type: image/png\r\nCache-Control: private, max-age=31536000, immutable");
            }

            deferral.Complete();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // 窗口已关闭，请求随之作废。
        }
    }

    private static bool IsPingOk(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            return root.TryGetProperty("status", out var status)
                && status.GetString() == "ok"
                && root.TryGetProperty("id", out var id)
                && id.GetString() == "selftest";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void FinishSelfTest(int code, string message)
    {
        if (_selfTestPassed)
            return;
        _selfTestPassed = true;
        _timeout.Stop();
        DeleteFakeGames();
        try
        {
            File.AppendAllText(Path.Combine(App.LogDirectory(), "selftest.log"), DateTime.Now.ToString("o") + " exit=" + code + " " + message + Environment.NewLine);
        }
        catch (IOException)
        {
        }

        Application.Current.Shutdown(code);
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_bridge is not null)
            await _bridge.DisposeAsync();
        base.OnClosed(e);
    }
}
