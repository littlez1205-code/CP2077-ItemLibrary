using System.Text.RegularExpressions;

namespace GameTool.Adapters.Cyberpunk2077.Localization;

public static partial class SteamGameLocator
{
    public const string GameFolderName = "Cyberpunk 2077";

    public static string? FindInstalledGame()
    {
#pragma warning disable CA1416 // 游戏安装位置只在 Windows 注册表里
        var steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
#pragma warning restore CA1416
        if (string.IsNullOrWhiteSpace(steamPath))
            return null;
        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(ReadLibraryPaths(File.ReadAllText(vdf)));
        return FindInLibraries(libraries);
    }

    public static IReadOnlyList<string> ReadLibraryPaths(string vdfText)
    {
        var paths = new List<string>();
        foreach (Match match in PathPattern().Matches(vdfText))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        return paths;
    }

    public static string? FindInLibraries(IEnumerable<string> libraryRoots)
    {
        foreach (var root in libraryRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var game = Path.Combine(root, "steamapps", "common", GameFolderName);
            if (Directory.Exists(game))
                return game;
        }

        return null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
