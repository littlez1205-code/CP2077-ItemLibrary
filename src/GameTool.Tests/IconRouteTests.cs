using GameTool.App;
using GameTool.Core;

namespace GameTool.Tests;

/// <summary>T020：桌面端 icon/ 地址的解析和记录名白名单。</summary>
public class IconRouteTests
{
    [Theory]
    [InlineData("/icon/cp2077/Items.Preset_Ajax_Default.png", "?g=f&v=r1-abc", "cp2077", "Items.Preset_Ajax_Default", IconGender.Female)]
    [InlineData("icon/cp2077/Items.Jacket_01_basic_01.png", "?v=r1&g=m", "cp2077", "Items.Jacket_01_basic_01", IconGender.Male)]
    [InlineData("/icon/cp2077/Items.money.png", "", "cp2077", "Items.money", IconGender.Female)]
    [InlineData("/icon/fake-game_2/Base.png", null, "fake-game_2", "Base", IconGender.Female)]
    public void Accepts_WellFormedPaths(string path, string? query, string game, string record, IconGender gender)
    {
        Assert.True(IconRoute.TryParse(path, query, out var request));
        Assert.Equal(new IconRequest(game, record, gender), request);
    }

    [Theory]
    [InlineData("/icon/cp2077/..%2F..%2Fsecret.png")]
    [InlineData("/icon/cp2077/../../secret.png")]
    [InlineData("/icon/cp2077/Items..Double.png")]
    [InlineData("/icon/cp2077/.hidden.png")]
    [InlineData("/icon/cp2077/Items.Thing..png")]
    [InlineData("/icon/cp2077/Items Space.png")]
    [InlineData("/icon/cp2077/Items.Thing")]
    [InlineData("/icon/cp2077/Items.Thing.jpg")]
    [InlineData("/icon/cp2077/a/b.png")]
    [InlineData("/icon/cp2077/C:\\Windows\\x.png")]
    [InlineData("/icon/cp2077/Items.物品.png")]
    [InlineData("/icon/cp 2077/Items.Thing.png")]
    [InlineData("/icon//Items.Thing.png")]
    [InlineData("/icon/cp2077/.png")]
    [InlineData("/skin/cp2077/background")]
    [InlineData("")]
    public void Rejects_PathTricks(string path)
    {
        Assert.False(IconRoute.TryParse(path, "?g=f", out _));
    }

    [Fact]
    public void Rejects_OverlongRecord()
    {
        Assert.False(IconRoute.IsValidRecordName(new string('a', IconRoute.MaxRecordLength + 1)));
        Assert.True(IconRoute.IsValidRecordName(new string('a', IconRoute.MaxRecordLength)));
    }
}
