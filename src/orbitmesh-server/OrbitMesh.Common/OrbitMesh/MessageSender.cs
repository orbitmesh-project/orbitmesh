namespace OrbitMesh;

/// <summary>Who sent the message currently being handled - see <c>MessageContext.Sender</c>.</summary>
public sealed class MessageSender
{
    /// <summary>The kind of connection the sender used.</summary>
    public enum SenderType
    {
        /// <summary>An external read-only consumer, connected via SignalR (ConsumerHub).</summary>
        ConsumerHub,
        /// <summary>A package, connected via SignalR (OrbitMeshHub) - the usual case.</summary>
        OrbitMeshHub,
        /// <summary>An external read-only consumer, calling over plain HTTP.</summary>
        ConsumerHttp,
        /// <summary>A package or automation calling over plain HTTP rather than a live connection.</summary>
        OrbitMeshHttp
    }

    /// <summary>The sender's live connection id, if sent over a SignalR connection.</summary>
    public string? ConnectionId { get; set; }

    /// <summary>The kind of connection the sender used.</summary>
    public SenderType Type { get; set; }

    /// <summary>The sender's package/consumer name.</summary>
    public string? FriendlyName { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"{FriendlyName} ({Type}<{ConnectionId}>)";
}
