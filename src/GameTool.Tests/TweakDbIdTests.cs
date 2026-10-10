using GameTool.Adapters.Cyberpunk2077;

namespace GameTool.Tests;

public class TweakDbIdTests
{
    [Fact]
    public void KnownVectors()
    {
        Assert.Equal(0x0BF5E188ECul, TweakDbId.FromName("Items.money"));
        Assert.Equal(0x18A5EC1EA5ul, TweakDbId.FromName("Items.LegendaryMaterial1"));
    }
}
