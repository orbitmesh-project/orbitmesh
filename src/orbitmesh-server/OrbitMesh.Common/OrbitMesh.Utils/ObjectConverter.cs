using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OrbitMesh.Utils;

/// <summary>Converts loosely-typed values coming from the SignalR/JSON wire (JsonElement/JsonNode) into CLR types.</summary>
public static class ObjectConverter
{
    /// <summary>The JSON options used by default when no explicit <c>options</c> is given -
    /// case-insensitive property names, string-named enums.</summary>
    public static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Converts a loosely-typed wire value (already <typeparamref name="T"/>, a
    /// <see cref="JsonElement"/>/<see cref="JsonNode"/>, or anything else re-serialized/deserialized
    /// through JSON) into <typeparamref name="T"/>.</summary>
    public static T? ConvertToObject<T>(object? value, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;
        switch (value)
        {
            case null:
                return default;
            case T typed:
                return typed;
            case JsonElement element:
                return element.Deserialize<T>(options);
            case JsonNode node:
                return node.Deserialize<T>(options);
            default:
                var json = JsonSerializer.Serialize(value, options);
                return JsonSerializer.Deserialize<T>(json, options);
        }
    }

    /// <summary>Non-generic version of <see cref="ConvertToObject{T}"/>, for when the target type
    /// isn't known at compile time.</summary>
    public static object? ConvertToObject(object? value, Type targetType, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;
        if (value is null)
        {
            return null;
        }
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }
        switch (value)
        {
            case JsonElement element:
                return element.Deserialize(targetType, options);
            case JsonNode node:
                return node.Deserialize(targetType, options);
            default:
                var json = JsonSerializer.Serialize(value, options);
                return JsonSerializer.Deserialize(json, targetType, options);
        }
    }
}
