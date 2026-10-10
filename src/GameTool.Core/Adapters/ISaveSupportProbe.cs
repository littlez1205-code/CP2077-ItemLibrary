namespace GameTool.Core;

public sealed record SaveSupport(bool Supported, string? Reason);

/// <summary>只读文件头判断能否处理，不做全量解压。</summary>
public interface ISaveSupportProbe
{
    SaveSupport Probe(string saveFilePath);
}
