namespace OrbitMesh;

/// <summary>Event args describing a received message - used by lower-level/consumer-side APIs
/// (most package code instead uses <see cref="Package.MessageHandlerAttribute"/> or
/// <c>PackageHost.RegisterMessageHandler</c>, which unwrap this into just the payload).</summary>
public sealed class MessageEventArgs : EventArgs
{
    /// <summary>Who sent the message.</summary>
    public required MessageSender Sender { get; set; }

    /// <summary>Who the message was addressed to.</summary>
    public required MessageScope Scope { get; set; }

    /// <summary>The message's key (handler name).</summary>
    public required string Key { get; set; }

    /// <summary>The message's payload, untyped.</summary>
    public object? Data { get; set; }
}
