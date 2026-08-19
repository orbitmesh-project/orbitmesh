using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Reads a JSON object into a Dictionary&lt;string, string&gt;, coercing non-string values (numbers,
/// booleans) to their string form instead of throwing. The console binds Int32/Double/Boolean-typed
/// package settings to native HTML number/checkbox inputs, so it POSTs their values as JSON numbers/
/// booleans (e.g. {"Latitude": 45.588104}) even though settings are stored as strings server-side -
/// the default Dictionary&lt;string, string&gt; converter rejects that with a 400.
/// </summary>
public sealed class FlexibleStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }
        var result = new Dictionary<string, string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }
            var key = reader.GetString()!;
            reader.Read();
            result[key] = OrbitMesh.FlexibleJsonValue.ReadAsString(ref reader) ?? string.Empty;
        }
        throw new JsonException("Unexpected end of JSON while reading a settings object.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, val) in value)
        {
            writer.WriteString(key, val);
        }
        writer.WriteEndObject();
    }
}
