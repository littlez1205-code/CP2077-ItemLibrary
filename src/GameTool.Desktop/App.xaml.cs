using System.Diagnostics;
using System.IO;
using System.Windows;
using GameTool.Adapters.Cyberpunk2077;
using GameTool.Adapters.Cyberpunk2077.Localization;
using Microsoft.Web.WebView2.Core;

namespace GameTool.Desktop;

public partial class App : Application
{
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var saveRoot = ReadOption(e.Args, "--save-root");
        var backupRoot = ReadOption(e.Args, "--backup-root");
        var skinRoot = ReadOption(e.Args, "--skin-root");
        var show = ReadOption(e.Args, "--show");
        var gameDir = ReadOption(e.Args, "--game-dir");
        var settingsFile = ReadOption(e.Args, "--settings");
        var pristine = "";
        var fullSelfTest = e.Args.Any(arg => arg == "--self-test=full");
        var selfTest = fullSelfTest || e.Args.Any(arg => arg == "--self-test");
        if (fullSelfTest)
        {
            var paths = CreateFullSelfTestPaths();
            saveRoot = paths.SaveRoot;
            backupRoot = paths.BackupRoot;
            skinRoot = paths.SkinRoot;
            pristine = paths.Pristine;
        }

        if (selfTest)
        {
            // 自检从不读写真实设置文件（D020）：设置放进临时目录，路径全部来自命令行或临时副本；
            // 没给 --game-dir 时，只有自检才用 Steam 探测游戏目录。
            var baseDir = Path.Combine(Path.GetTempPath(), "GameTool-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            settingsFile = Path.Combine(baseDir, "settings.json");
            if (string.IsNullOrWhiteSpace(saveRoot))
            {
                saveRoot = Path.Combine(baseDir, "saves");
                Directory.CreateDirectory(saveRoot);
            }

            if (string.IsNullOrWhiteSpace(gameDir))
                gameDir = SteamGameLocator.FindInstalledGame() ?? "";
        }
        if (!HasWebView2Runtime())
        {
            ReportMissingWebView2(selfTest);
            Shutdown(2);
            return;
        }

        var window = new MainWindow(new MainWindowOptions(saveRoot, backupRoot, skinRoot, gameDir, settingsFile, pristine, selfTest, fullSelfTest, show));
        MainWindow = window;
        window.Show();
    }

    internal const string MissingWebView2Message =
        "缺少 Microsoft Edge WebView2 运行时，界面无法显示。请从微软官网下载安装（" + RuntimeDownloadUrl + "），然后重新打开本工具。";

    /// <summary>
    /// 缺少 WebView2 运行时（D022）：弹出中文提示，可选用默认浏览器打开下载页，然后退出（退出码 2）。
    /// 自检不弹对话框（否则会卡住等人点），只写自检日志。
    /// </summary>
    internal static void ReportMissingWebView2(bool selfTest)
    {
        if (selfTest)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(LogDirectory(), "selftest.log"),
                    DateTime.Now.ToString("o") + " exit=2 " + MissingWebView2Message + Environment.NewLine);
            }
            catch (IOException)
            {
            }

            return;
        }

        var choice = MessageBox.Show(
            MissingWebView2Message + "\n\n现在打开下载页面吗？",
            "游戏工具",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        if (choice != MessageBoxResult.Yes)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            MessageBox.Show(
                "无法打开浏览器，请手动访问：\n" + RuntimeDownloadUrl,
                "游戏工具",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    internal static bool HasWebView2Runtime()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        return "";
    }

    internal readonly record struct SelfTestPaths(string SaveRoot, string BackupRoot, string SkinRoot, string Pristine);

    internal static SelfTestPaths CreateFullSelfTestPaths()
    {
        // 源存档：常见位置里的 ManualSave-*，只读复制到临时目录（自检环境专用，桌面端正常运行不探测路径）。
        var adapter = new Cp2077Adapter();
        var source = adapter.DiscoverSaves(Cp2077Paths.DefaultSaveRoot)
            .FirstOrDefault(slot => slot.SlotName.StartsWith("ManualSave-", StringComparison.Ordinal));
        if (source is null)
            throw new InvalidOperationException("找不到 ManualSave-* 存档，无法做完整自检。");
        var baseDir = Path.Combine(Path.GetTempPath(), "GameTool-selftest-" + Guid.NewGuid().ToString("N"));
        var saveRoot = Path.Combine(baseDir, "saves");
        var backupRoot = Path.Combine(baseDir, "backups");
        var skinRoot = Path.Combine(baseDir, "skins");
        Directory.CreateDirectory(Path.Combine(saveRoot, "ManualSave-0"));
        Directory.CreateDirectory(backupRoot);
        Directory.CreateDirectory(skinRoot);
        var slot = Path.Combine(saveRoot, "ManualSave-0", "sav.dat");
        var pristine = Path.Combine(baseDir, "pristine.dat");
        File.Copy(source.SaveFilePath, slot);
        File.Copy(source.SaveFilePath, pristine);
        PlantLegacy(saveRoot, slot);
        PlantResidue(saveRoot, slot);
        File.SetLastWriteTimeUtc(slot, DateTime.UtcNow);
        return new SelfTestPaths(saveRoot, backupRoot, skinRoot, pristine);
    }

    private static void PlantLegacy(string saveRoot, string slot)
    {
        var path = Path.Combine(saveRoot, "LegacySave", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(slot, path);
        var bytes = File.ReadAllBytes(path);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 261);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 2100);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
    }

    private static void PlantResidue(string saveRoot, string slot)
    {
        var path = Path.Combine(saveRoot, "ResidueSave", "sav.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(slot, path);
        var bytes = File.ReadAllBytes(path);
        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Cp2077Format.ChunkCountOffset));
        var entry = Cp2077Format.ChunkTableOffset + ((int)count * Cp2077Format.ChunkEntrySize);
        bytes[entry] = 0x5A;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
    }

    internal static string LogDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTool",
            "logs");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
