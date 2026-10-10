using System.Text.Json;
using System.Text.Json.Serialization;
using GameTool.Core;

namespace GameTool.App;

public sealed record ResourceInfo(
    string Id,
    string NameZh,
    long? Quantity,
    IReadOnlyList<long> Stacks,
    bool Ambiguous);

[JsonConverter(typeof(SaveInfoDataConverter))]
public sealed record SaveInfoData(
    uint GameVersion,
    uint SaveVersion,
    IReadOnlyList<DiagnosticField> Diagnostics,
    IReadOnlyList<ResourceInfo> Resources);

public sealed class SaveInfoDataConverter : JsonConverter<SaveInfoData>
{
    public override SaveInfoData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("存档信息只写出，不读回。");

    public override void Write(Utf8JsonWriter writer, SaveInfoData value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName(nameof(SaveInfoData.GameVersion)) ?? "gameVersion", value.GameVersion);
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName(nameof(SaveInfoData.SaveVersion)) ?? "saveVersion", value.SaveVersion);
        foreach (var field in value.Diagnostics)
        {
            writer.WritePropertyName(field.Name);
            JsonSerializer.Serialize(writer, field.Value, options);
        }

        writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(nameof(SaveInfoData.Resources)) ?? "resources");
        JsonSerializer.Serialize(writer, value.Resources, options);
        writer.WriteEndObject();
    }
}
