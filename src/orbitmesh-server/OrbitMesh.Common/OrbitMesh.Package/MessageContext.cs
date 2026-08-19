namespace OrbitMesh.Package;

/// <summary>Ambient context for the message currently being handled inside a
/// <see cref="MessageHandlerAttribute"/> method - who sent it and its scope. Thread-static: valid only
/// for the duration of that handler's synchronous execution on its own thread.</summary>
public sealed class MessageContext
{
    [ThreadStatic]
    private static MessageContext? current;

    /// <summary>The context for the message handler currently executing on this thread, or
    /// <see cref="None"/> outside of one.</summary>
    public static MessageContext Current
    {
        get => current ?? None;
        set => current = value;
    }

    /// <summary>An empty context, for use outside of a message handler.</summary>
    public static MessageContext None => new();

    /// <summary>True if this context actually describes an in-progress message (both
    /// <see cref="Scope"/> and <see cref="Sender"/> are set).</summary>
    public bool HasContext => Scope != null && Sender != null;

    /// <summary>True if the current message is part of a saga (request/response) exchange.</summary>
    public bool IsSaga => HasContext && Scope!.IsSaga;

    /// <summary>The saga correlation id, if <see cref="IsSaga"/>.</summary>
    public string? SagaId => HasContext ? Scope!.SagaId : null;

    /// <summary>Who the message was addressed to.</summary>
    public MessageScope? Scope { get; set; }

    /// <summary>Who sent the message.</summary>
    public MessageSender? Sender { get; set; }

    internal MessageContext()
    {
    }
}
