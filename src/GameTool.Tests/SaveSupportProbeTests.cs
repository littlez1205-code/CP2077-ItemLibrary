using System.Buffers.Binary;
using GameTool.Adapters.Cyberpunk2077;

namespace GameTool.Tests;

public class SaveSupportProbeTests
{
    [Fact]
    public void Probe_ReadsOnlyTheHeader()
    {
        var adapter = new Cp2077Adapter();
        var path = Path.Combine(Path.GetTempPath(), "GameTool-probe-" + Guid.NewGuid().ToString("N"));
        var head = new byte[12];
        "VASC"u8.CopyTo(head);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 261);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 2100);
        File.WriteAllBytes(path, head);
        try
        {
            var support = adapter.Probe(path);
            Assert.False(support.Supported);
            Assert.Contains("2.1", support.Reason);
            Assert.Contains("261", support.Reason);
            Assert.Contains("2.31", support.Reason);
            Assert.Contains("重新存档", support.Reason);

            // 游戏更新后的新存档：不能靠重新存档解决，要等工具适配（D024）。
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 280);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 2600);
            File.WriteAllBytes(path, head);
            var newer = adapter.Probe(path);
            Assert.False(newer.Supported);
            Assert.Contains("2.6", newer.Reason);
            Assert.Contains("等待工具适配", newer.Reason);
            Assert.DoesNotContain("重新存档", newer.Reason);

            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 269);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 2310);
            File.WriteAllBytes(path, head);
            Assert.True(adapter.Probe(path).Supported);

            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 277);
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 2500);
            File.WriteAllBytes(path, head);
            Assert.True(adapter.Probe(path).Supported);

            // 只认实测过的版本对：两个版本之间、或存档格式与游戏版本错配的组合一律不支持。
            foreach (var (save, game) in new (uint, uint)[] { (273, 2400), (277, 2310), (269, 2500), (270, 2310), (276, 2500) })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), save);
                BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), game);
                File.WriteAllBytes(path, head);
                Assert.False(adapter.Probe(path).Supported, $"{save}/{game} 不应被支持");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
