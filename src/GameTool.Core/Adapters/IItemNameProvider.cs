namespace GameTool.Core;

public sealed record ItemLabel(string Label, string? RecordName);

/// <summary>适配器可选。名字表懒加载一次并缓存。</summary>
public interface IItemNameProvider
{
    bool Available { get; }

    string? UnavailableMessage { get; }

    ItemLabel Resolve(string tweakDbIdHex);

    bool IsInternal(string? recordName);
}
