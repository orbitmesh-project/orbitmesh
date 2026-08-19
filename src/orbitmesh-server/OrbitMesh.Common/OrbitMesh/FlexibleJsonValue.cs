using System.Globalization;
using System.Text.Json;

namespace OrbitMesh;

/// <summary>
/// Shared "read a JSON string/number/bool/null token as a C# string" rule, used by both
/// <see cref="FlexibleStringConverter"/> (single values, e.g. saga IDs) and the server's settings-
/// dictionary converter. Newtonsoft.Json (the original stack) auto-coerced numbers/booleans into
/// string-typed properties; System.Text.Json does not, so both converters need this same leniency.
/// </summary>
public static class FlexibleJsonValue
{
    /// <summary>Reads the current JSON token (string, number, bool, or null) as a C# string.</summary>
    public static string? ReadAsString(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Cannot convert token {reader.TokenType} to a string")
    };
}
