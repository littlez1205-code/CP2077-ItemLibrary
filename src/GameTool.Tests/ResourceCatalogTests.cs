using System.Text;
using GameTool.Core;

namespace GameTool.Tests;

public class ResourceCatalogTests
{
    [Theory]
    [InlineData("""[{"id":"a","name":{"zh":"甲","en":"A"},"group":"g","max":0,"defaultFill":0,"locator":"tdb:A"}]""")]
    [InlineData("""[{"id":"a","name":{"zh":"甲","en":"A"},"group":"g","max":10,"defaultFill":11,"locator":"tdb:A"}]""")]
    [InlineData("""[{"id":"a","name":{"zh":"甲","en":"A"},"group":"g","max":10,"defaultFill":-1,"locator":"tdb:A"}]""")]
    [InlineData("""[{"id":"a","name":{"zh":"甲","en":"A"},"group":"g","max":10,"defaultFill":1,"locator":""}]""")]
    [InlineData("""[{"id":"a","name":{"zh":"甲","en":"A"},"group":"g","max":10,"locator":"tdb:A"},{"id":"a","name":{"zh":"乙","en":"B"},"group":"g","max":10,"locator":"tdb:B"}]""")]
    public void RejectsInvalidCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(stream));
    }
}
