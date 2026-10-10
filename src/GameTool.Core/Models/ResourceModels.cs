namespace GameTool.Core;

public readonly record struct ResourceId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// 一条可修改资源。Max 是数量上限，具体边界由各游戏适配器给出。
/// </summary>
public sealed record ResourceDescriptor(
    ResourceId Id,
    string NameZh,
    string NameEn,
    string Group,
    long Max,
    long? DefaultFill,
    string Locator);

public sealed record ResourceChange(ResourceId Id, long TargetQuantity);

public sealed record ResourceChangeResult(
    ResourceId Id,
    long? PreviousQuantity,
    long? AppliedQuantity,
    string Outcome);
