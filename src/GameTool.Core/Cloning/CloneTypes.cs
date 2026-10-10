namespace GameTool.Core;

public sealed record CloneRequest(string Key, int Copies);

public sealed record CloneReport(PatchReport Patch, IReadOnlyList<uint> Seeds, int AddedBytes);

public sealed record CloneListItem(
    string Key,
    ulong BagId,
    int Ordinal,
    string HeaderHex,
    string TweakDbIdHex,
    int Length,
    string Label,
    string? RecordName);

public sealed record CloneApplyResult(
    string Key,
    int Copies,
    int AddedBytes,
    int FirstNodeIndex,
    int PreviousPlayerCount,
    int SourceLength,
    byte[] SourceHeader,
    byte[] SourceBody,
    IReadOnlyList<uint> Seeds);

public interface IItemCloneCapable
{
    int PlayerItemCount { get; }

    bool NamesAvailable { get; }

    string? NamesMessage { get; }

    IReadOnlyList<CloneListItem> ListCloneable();

    CloneApplyResult Clone(CloneRequest request);

    void VerifyOnlyCloned(ISaveDocument original, CloneApplyResult applied);
}
