using System.Text.Json.Serialization;

namespace OrbitMesh;

/// <summary>Who a message is addressed to - see <c>PackageHost.SendMessage</c>/<c>SendMessageAsync</c>.</summary>
public sealed class MessageScope
{
    /// <summary>The kind of target a <see cref="MessageScope"/> addresses.</summary>
    public enum ScopeType
    {
        /// <summary>No target - an empty/unset scope.</summary>
        None,
        /// <summary>Every package that has joined the named group (see <c>PackageHost.SubscribeMessages</c>).</summary>
        Group,
        /// <summary>A single package, by name (see <see cref="Args"/>).</summary>
        Package,
        /// <summary>Every package on a given Edge, by edge name (see <see cref="Args"/>).</summary>
        Edge,
        /// <summary>Every package except the sender.</summary>
        Others,
        /// <summary>Every connected package.</summary>
        All
    }

    /// <summary>Correlation id for a saga (request/response) exchange - see
    /// <c>MessageExtension.WithSaga</c>. Null for a plain one-way message.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? SagaId { get; set; }

    /// <summary>True if this scope is part of a saga (request/response) exchange.</summary>
    [JsonIgnore]
    public bool IsSaga => !string.IsNullOrEmpty(SagaId);

    /// <summary>The kind of target.</summary>
    public ScopeType Scope { get; set; }

    /// <summary>The target's name(s), meaning depends on <see cref="Scope"/> (group name, package
    /// name, or edge name) - unused for <see cref="ScopeType.Others"/>/<see cref="ScopeType.All"/>.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Creates an empty (<see cref="ScopeType.None"/>) scope.</summary>
    public MessageScope() { }

    /// <summary>Creates a scope targeting <paramref name="scope"/>, with the given target name(s).</summary>
    public MessageScope(ScopeType scope, params string[]? args)
    {
        Scope = scope;
        Args = new List<string>();
        if (args is { Length: > 0 })
        {
            Args.AddRange(args);
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        Scope is ScopeType.Group or ScopeType.Package or ScopeType.Edge
            ? $"{Scope} [{string.Join(", ", Args)}]"
            : Scope.ToString();

    /// <summary>Creates a scope targeting a single package by name.</summary>
    public static MessageScope Create(string package) => Create(ScopeType.Package, package);

    /// <summary>Creates a scope targeting <paramref name="scope"/>, with the given target name(s).</summary>
    public static MessageScope Create(ScopeType scope, params string[]? args) => new(scope, args);
}
