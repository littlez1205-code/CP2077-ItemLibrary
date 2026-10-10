namespace GameTool.Core;

/// <summary>
/// 返回某一资源的全部堆叠数量。没有该资源时返回空列表。
/// </summary>
public interface IStackInspector
{
    IReadOnlyList<long> GetStacks(ResourceId id);
}
