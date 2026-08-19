using System.Text.Json.Serialization;
using OrbitMesh.Utils;

namespace OrbitMesh;

/// <summary>A single published telemetry value and its metadata - see
/// <c>PackageHost.PushTelemetryItem</c>/<c>RegisterTelemetryItemCallback</c>.</summary>
public sealed class TelemetryItem
{
    /// <summary>The publishing package's Edge.</summary>
    public required string EdgeName { get; set; }

    /// <summary>The publishing package's name.</summary>
    public required string PackageName { get; set; }

    /// <summary>The item's name, as passed to <c>PackageHost.PushTelemetryItem</c>.</summary>
    public required string Name { get; set; }

    /// <summary>"{EdgeName}/{PackageName}/{Name}" - identifies this item across the whole system.</summary>
    [JsonIgnore]
    public string UniqueId => $"{EdgeName}/{PackageName}/{Name}";

    /// <summary>The value's type name, either explicit or inferred from <see cref="Value"/>'s own type.</summary>
    public string? Type { get; set; }

    /// <summary>Arbitrary extra key/value data published alongside the value.</summary>
    public Dictionary<string, object>? Metadatas { get; set; }

    /// <summary>When this value was last published.</summary>
    public DateTime LastUpdate { get; set; }

    /// <summary>How many seconds this value stays valid before <see cref="IsExpired"/> becomes true
    /// (0 = never expires).</summary>
    public int Lifetime { get; set; }

    /// <summary>True if <see cref="Lifetime"/> is set and has elapsed since <see cref="LastUpdate"/>.</summary>
    public bool IsExpired => Lifetime > 0 && DateTime.Now > LastUpdate.AddSeconds(Lifetime);

    /// <summary>The published value, untyped - see <see cref="GetValue{T}"/> for typed access.</summary>
    public object? Value { get; set; }

    /// <summary>True if a value has actually been published.</summary>
    [JsonIgnore]
    public bool HasValue => Value != null;

    /// <summary>The published value, dynamically typed for convenient access without casting.</summary>
    [JsonIgnore]
    public dynamic? DynamicValue => Value;

    /// <summary>Converts <see cref="Value"/> to <typeparamref name="T"/>. Throws if unset/inconvertible
    /// - see <see cref="TryGetValue{T}"/> for a non-throwing version.</summary>
    public T? GetValue<T>() => HasValue ? ObjectConverter.ConvertToObject<T>(Value) : default;

    /// <summary>Like <see cref="GetValue{T}"/>, but returns false instead of throwing if unset/inconvertible.</summary>
    public bool TryGetValue<T>(out T? value)
    {
        try
        {
            value = GetValue<T>();
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
