namespace GameTool.Core;

/// <summary>
/// 适配器可选：在内置资源表之外追加几条资源描述，得到一个新的适配器实例（原实例不变）。
/// 物品库按记录获得堆叠物品时用它走 PatchEngine.Patch 的改数量 / 插入路径和严格校验。
/// </summary>
public interface IResourceExtensible
{
    IGameAdapter WithResources(IReadOnlyList<ResourceDescriptor> extra);
}
