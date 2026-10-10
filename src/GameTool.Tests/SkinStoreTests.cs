using System.Text;
using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

public class SkinStoreTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameTool-skin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Import_AcceptsPngAndJpg_AndReplacesThePreviousFile()
    {
        var store = Create(new ThemeStub("fake", null));
        var png = store.ImportImage("fake", "shot.png", Png);
        Assert.True(png.HasImage);
        Assert.True(File.Exists(Path.Combine(_root, "fake", "background.png")));

        var jpg = store.ImportImage("fake", "shot.jpg", Jpeg);
        Assert.True(jpg.HasImage);
        Assert.False(File.Exists(Path.Combine(_root, "fake", "background.png")));
        Assert.True(File.Exists(Path.Combine(_root, "fake", "background.jpg")));
    }

    [Fact]
    public void Import_RejectsForgedExtensionOversizedAndUnknownMagic()
    {
        var store = Create(new ThemeStub("fake", null));
        Assert.Contains("不符", Assert.Throws<SkinRejectedException>(() => store.ImportImage("fake", "shot.png", Jpeg)).Message);
        var oversized = new byte[SkinStore.MaxBytes + 1];
        Png.CopyTo(oversized, 0);
        Assert.Contains("20", Assert.Throws<SkinRejectedException>(() => store.ImportImage("fake", "big.png", oversized)).Message);
        Assert.Contains("格式", Assert.Throws<SkinRejectedException>(() => store.ImportImage("fake", "anim.gif", "GIF89a"u8.ToArray())).Message);
    }

    [Fact]
    public void Overlay_RejectsOutOfRange_AndClearRemovesTheImage()
    {
        var store = Create(new ThemeStub("fake", null));
        Assert.Throws<SkinRejectedException>(() => store.SetOverlay("fake", 0.81));
        Assert.Throws<SkinRejectedException>(() => store.SetOverlay("fake", -0.01));
        var saved = store.SetOverlay("fake", 0.5);
        Assert.Equal(0.5, saved.OverlayOpacity);

        store.ImportImage("fake", "shot.png", Png);
        var cleared = store.ClearImage("fake");
        Assert.False(cleared.HasImage);
        Assert.Equal(0.5, cleared.OverlayOpacity);
    }

    [Fact]
    public void PathsOutsideTheSkinRoot_AreRejected()
    {
        var store = Create(new ThemeStub("fake", null));
        Assert.Throws<SkinRejectedException>(() => store.ImportImage("fake", "..\\shot.png", Png));
        Assert.Throws<SkinRejectedException>(() => store.ImportImage("../escape", "shot.png", Png));
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(_root)!.FullName, "shot.png")));
    }

    [Fact]
    public void InvalidTheme_FallsBackToTheDefault()
    {
        var store = Create(new ThemeStub("fake", """{"accent":"yellow"}"""));
        var theme = store.Theme("fake");
        Assert.Equal(ThemeCatalog.Default.Accent, theme.Accent);
        Assert.Equal(ThemeCatalog.Default.DefaultOverlayOpacity, store.Get("fake").OverlayOpacity);

        var missing = Create(new ThemeStub("fake", null));
        Assert.Equal(ThemeCatalog.Default.Accent, missing.Theme("fake").Accent);
    }

    private SkinStore Create(ThemeStub adapter)
    {
        var registry = new AdapterRegistry();
        registry.Register(adapter);
        return new SkinStore(registry, _root);
    }

    private sealed class ThemeStub : IGameAdapter, IThemeProvider
    {
        private readonly string? _theme;

        public ThemeStub(string gameId, string? theme)
        {
            GameId = gameId;
            _theme = theme;
        }

        public string GameId { get; }
        public string DisplayName => GameId;
        public string DefaultSaveRoot => "";
        public SaveWatchSpec WatchSpec => new("", [], 1);
        public IReadOnlyList<ResourceDescriptor> Resources => [];
        public Stream? OpenTheme() => _theme is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(_theme));
        public IEnumerable<SaveSlotInfo> DiscoverSaves(string saveRoot) => [];
        public bool IsComplete(string saveFilePath) => false;
        public ISaveDocument Load(string saveFilePath) => throw new NotSupportedException();
    }
}
