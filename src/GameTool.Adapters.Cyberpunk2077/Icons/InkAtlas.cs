using GameTool.Adapters.Cyberpunk2077.Resources;

namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>图集里的一块：归一化 UV，原点在左上角。</summary>
public sealed record AtlasPart(string Name, double Left, double Top, double Right, double Bottom);

/// <summary>图集的一个槽：贴图深度路径和部件表。</summary>
public sealed record AtlasSlot(string? TexturePath, IReadOnlyDictionary<string, AtlasPart> Parts);

/// <summary>
/// inkTextureAtlas（.inkatlas）。物品图集的 slots[0] 是全分辨率贴图，slots[1] 是 *_1080p 版本，部件名相同。
/// 旧图集把 texture / parts 直接写在根上，按单槽处理。
/// </summary>
public sealed class InkAtlas
{
    private InkAtlas(IReadOnlyList<AtlasSlot> slots) => Slots = slots;

    public IReadOnlyList<AtlasSlot> Slots { get; }

    public static InkAtlas Parse(Cr2wFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.ExportCount == 0)
            throw new InvalidDataException("图集没有导出对象。");
        var root = file.ReadExport(0);
        var slots = new List<AtlasSlot>();
        foreach (var item in root.GetArray("slots"))
        {
            if (item is Cr2wStruct slot)
                slots.Add(ReadSlot(slot));
        }

        if (slots.Count == 0 && (root.Has("texture") || root.Has("parts")))
            slots.Add(ReadSlot(root));
        return new InkAtlas(slots);
    }

    private static AtlasSlot ReadSlot(Cr2wStruct slot)
    {
        var texture = (slot["texture"] as Cr2wResourceRef)?.DepotPath;
        var parts = new Dictionary<string, AtlasPart>(StringComparer.Ordinal);
        foreach (var item in slot.GetArray("parts"))
        {
            if (item is not Cr2wStruct mapper || mapper.GetName("partName") is not { Length: > 0 } name)
                continue;
            var rect = mapper.GetStruct("clippingRectInUVCoords");
            parts[name] = new AtlasPart(
                name,
                rect?.GetFloat("Left") ?? 0,
                rect?.GetFloat("Top") ?? 0,
                rect?.GetFloat("Right") ?? 0,
                rect?.GetFloat("Bottom") ?? 0);
        }

        return new AtlasSlot(string.IsNullOrWhiteSpace(texture) ? null : texture, parts);
    }
}
