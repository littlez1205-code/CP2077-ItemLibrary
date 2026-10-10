namespace GameTool.Core;

public interface IGameAdapter
{
    string GameId { get; }
    string DisplayName { get; }
    string DefaultSaveRoot { get; }
    SaveWatchSpec WatchSpec { get; }
    IReadOnlyList<ResourceDescriptor> Resources { get; }
    IEnumerable<SaveSlotInfo> DiscoverSaves(string saveRoot);
    bool IsComplete(string saveFilePath);
    ISaveDocument Load(string saveFilePath);
}
