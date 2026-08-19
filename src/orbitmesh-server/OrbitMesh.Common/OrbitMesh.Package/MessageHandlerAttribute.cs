namespace OrbitMesh.Package;

/// <summary>Exposes a method as an RPC endpoint other packages/the Console can call by key - see
/// <see cref="PackageHost.RegisterMessageHandlers"/> (wires up every attributed method on an instance)
/// and <see cref="PackageHost.SendMessageAsync{TResponse}"/> (calls one from the other side).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MessageHandlerAttribute : Attribute
{
    /// <summary>The key callers use to reach this handler. Defaults to empty, meaning the method name
    /// itself is used as the key.</summary>
    public string Key { get; set; }

    /// <summary>Human-readable description, surfaced to the Console/other tooling that lists a
    /// package's available handlers.</summary>
    public string Description { get; set; }

    /// <summary>True to keep this handler out of that listing while still being callable - for
    /// internal/advanced handlers not meant for casual discovery.</summary>
    public bool IsHidden { get; set; }

    /// <summary>False (default): the key is namespaced as <c>"{PackageName}/{key}"</c>. True opts into
    /// a raw, shared key other packages can call without knowing this package's name.</summary>
    public bool Shared { get; set; }

    /// <summary>The type callers should expect back, for documentation/tooling purposes only - the
    /// actual return type is whatever the attributed method declares.</summary>
    public Type? ResponseType { get; set; }

    /// <summary>Uses the method's own name as the key.</summary>
    public MessageHandlerAttribute()
        : this(string.Empty, null)
    {
    }

    /// <summary>Uses <paramref name="key"/> instead of the method's own name.</summary>
    public MessageHandlerAttribute(string key)
        : this(key, null)
    {
    }

    /// <summary>Uses the method's own name as the key, and documents the expected response type.</summary>
    public MessageHandlerAttribute(Type responseType)
        : this(string.Empty, responseType)
    {
    }

    /// <summary>Uses <paramref name="key"/> instead of the method's own name, and documents the
    /// expected response type.</summary>
    public MessageHandlerAttribute(string key, Type? responseType)
    {
        Key = key;
        IsHidden = false;
        Description = string.Empty;
        ResponseType = responseType;
    }
}
