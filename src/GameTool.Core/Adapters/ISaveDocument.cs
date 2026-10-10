namespace GameTool.Core;

public interface ISaveDocument
{
    SaveVersionInfo Version { get; }
    long? GetQuantity(ResourceId id);
    IReadOnlyList<ResourceChangeResult> Apply(IReadOnlyList<ResourceChange> changes);
    void Save(Stream output);
}
