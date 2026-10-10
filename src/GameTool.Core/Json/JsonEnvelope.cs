using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameTool.Core;

public sealed record JsonEnvelopeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// CLI <c>--json</c> 的 stdout 信封。日志不得写入 stdout。
/// </summary>
public sealed class JsonEnvelope
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("game")]
    public string? Game { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }

    [JsonPropertyName("error")]
    public JsonEnvelopeError? Error { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    public static JsonEnvelope Ok(string game, object data) => new()
    {
        Status = "ok",
        Game = game,
        Data = data
    };

    public static JsonEnvelope Fail(string? game, string code, string message) => new()
    {
        Status = "error",
        Game = game,
        Error = new JsonEnvelopeError(code, message)
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}
