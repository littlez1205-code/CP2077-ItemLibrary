namespace GameTool.Core;

public sealed record ResearchField(string Name, string Value);

public sealed record ResearchRegion(int Index, string Name, int Offset, int Length, int? Parent);

/// <summary>解压后的研究视图。没有区域时，diff 和 find 退回整段字节。</summary>
public sealed record ResearchImage(byte[] Payload, IReadOnlyList<ResearchField> Header, IReadOnlyList<ResearchRegion> Regions);

/// <summary>可选。未实现时研究命令仍可对原始字节做 diff 和 find。</summary>
public interface IResearchProvider
{
    ResearchImage OpenForResearch(string saveFilePath);
}

/// <summary>可选。把资源名字换算成存档里的 TweakDBID。</summary>
public interface ITweakDbIdSource
{
    ulong TweakId(string name);
}
