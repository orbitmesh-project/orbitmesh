using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitMesh;

/// <summary>
/// Reads a JSON string OR number into a C# string. The console's JS builds saga IDs as
/// <c>Date.getTime() + random</c> (a JS number), but <see cref="MessageScope.SagaId"/> is a string on
/// the wire elsewhere (and Newtonsoft.Json, the original server's serializer, coerced this automatically -
/// System.Text.Json does not, by default, for a number token into a string-typed property).
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    /// <inheritdoc/>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleJsonValue.ReadAsString(ref reader);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
