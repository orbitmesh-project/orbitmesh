namespace OrbitMesh.Package;

/// <summary>Fluent helpers for saga (request/response) messaging on top of <see cref="MessageScope"/>
/// and <see cref="MessageContext"/>.</summary>
public static class MessageExtension
{
    /// <summary>Tags this scope with a saga correlation id (generating one if it doesn't already have
    /// one), so a response can be routed back to whoever's awaiting it.</summary>
    public static MessageScope WithSaga(this MessageScope scope, string? sagaId = null)
    {
        if (!scope.IsSaga)
        {
            scope.SagaId = sagaId ?? Guid.NewGuid().ToString("N");
        }
        return scope;
    }

    /// <summary>Registers a callback invoked when a response to this saga-scoped message comes back.</summary>
    public static MessageScope OnSagaResponse<TResponse>(this MessageScope scope, Action<TResponse?> onResponse)
    {
        PackageHost.RegisterSagaResponseCallback(scope.WithSaga().SagaId!, onResponse);
        return scope;
    }

    /// <summary>Builds the scope a response to the currently-handled message should be sent to -
    /// the original sender, carrying the same saga id.</summary>
    public static MessageScope CreateResponseScope(this MessageContext context)
    {
        var sender = context.Sender ?? throw new InvalidOperationException("No message context available.");
        var target = sender.Type == MessageSender.SenderType.ConsumerHub ? sender.ConnectionId! : sender.FriendlyName!;
        return new MessageScope(MessageScope.ScopeType.Package, target) { SagaId = context.SagaId };
    }

    /// <summary>Sends <paramref name="message"/> back to whoever sent the message currently being
    /// handled, as a saga response.</summary>
    public static void SendResponse(this MessageContext context, object? message) =>
        // shared: true - see PackageHost.RegisterSagaResponseCallback's own note on the same key.
        PackageHost.SendMessage(context.CreateResponseScope(), OrbitMeshDefaultNames.SagaResponseMessageKey, message, shared: true);
}
