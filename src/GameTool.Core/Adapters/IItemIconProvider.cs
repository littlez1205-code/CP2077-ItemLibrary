namespace GameTool.Core;

/// <summary>物品图标的性别变体。游戏默认用女性图标，男性角色才换男性图标。</summary>
public enum IconGender
{
    Female,
    Male
}

/// <summary>
/// 适配器可选：从本机游戏资源里取物品图标。懒加载，第一次请求才打开游戏资源；
/// 游戏、资源或图标缺失时返回 null，不抛错。
/// </summary>
public interface IItemIconProvider
{
    /// <summary>图标版本：游戏资源或提取规则变化时改变，用于可长期缓存的地址。图标不可用时为 null。</summary>
    string? IconVersion { get; }

    /// <summary>按记录名取 PNG 字节；没有图标返回 null。</summary>
    byte[]? TryGetIconPng(string recordName, IconGender gender);
}
