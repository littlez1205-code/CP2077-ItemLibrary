using GameTool.Core;

namespace GameTool.App;

public static class ManualSaveSelection
{
    public static bool IsExperimentSlot(string slotName) =>
        slotName.Length == "ManualSave-90".Length
        && slotName.StartsWith("ManualSave-9", StringComparison.Ordinal)
        && char.IsDigit(slotName[^1]);

    public static SaveSlotInfo? NewestPlayable(IEnumerable<SaveSlotInfo> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return slots
            .Where(slot => slot.SlotName.StartsWith("ManualSave-", StringComparison.Ordinal) && !IsExperimentSlot(slot.SlotName))
            .OrderByDescending(slot => slot.LastWriteTimeUtc)
            .ThenBy(slot => slot.SlotName, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
