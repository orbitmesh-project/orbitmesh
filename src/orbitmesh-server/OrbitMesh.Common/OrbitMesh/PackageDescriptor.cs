namespace OrbitMesh;

/// <summary>Reflection-derived description of a package's message handlers and telemetry item shapes,
/// sent to the Server so the Console can show what it exposes - built automatically by
/// <c>PackageHost.RegisterMessageHandlers</c>/<c>DescribeTelemetryItemTypes</c>. Not something package
/// authors construct by hand.</summary>
public sealed class PackageDescriptor
{
    /// <summary>One <c>[MessageHandler]</c>-attributed method, as advertised to the Console.</summary>
    public sealed class MessageHandlerDescriptor
    {
        /// <summary>The handler's key (see <c>Package.MessageHandlerAttribute.Key</c>).</summary>
        public required string MessageKey { get; set; }

        /// <summary>Human-readable description (see <c>Package.MessageHandlerAttribute.Description</c>).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The response type's full name, if any.</summary>
        public string? ResponseType { get; set; }

        /// <summary>The handler method's parameters.</summary>
        public List<MemberDescriptor> Parameters { get; set; } = new();

        /// <inheritdoc/>
        public override string ToString() =>
            $"{MessageKey}({string.Join(", ", Parameters.Select(p => p.TypeName + " " + p.Name))})";
    }

    /// <summary>A type referenced by a message handler's parameters/response or a telemetry item -
    /// described once and reused by <see cref="TypeDescriptor.TypeFullname"/> everywhere else it appears.</summary>
    public sealed class TypeDescriptor
    {
        /// <summary>The type's own XML doc summary, if the assembly ships one.</summary>
        public string? Description { get; set; }

        /// <summary>True if this is a generic type - see <see cref="GenericParameters"/>.</summary>
        public bool IsGeneric { get; set; }

        /// <summary>True if this is an array type - its element type is described in
        /// <see cref="GenericParameters"/> (with a single entry).</summary>
        public bool IsArray { get; set; }

        /// <summary>Full names of this type's generic type arguments (or, for an array, its single
        /// element type), if <see cref="IsGeneric"/>/<see cref="IsArray"/>.</summary>
        public List<string>? GenericParameters { get; set; }

        /// <summary>True if this is an enum - see <see cref="EnumValues"/>.</summary>
        public bool IsEnum { get; set; }

        /// <summary>This enum's members, if <see cref="IsEnum"/>.</summary>
        public List<MemberDescriptor>? EnumValues { get; set; }

        /// <summary>The type's short name.</summary>
        public required string TypeName { get; set; }

        /// <summary>The type's full name - used to cross-reference this descriptor from elsewhere.</summary>
        public required string TypeFullname { get; set; }

        /// <summary>This type's public properties, if it's a plain object (not an enum/array/generic).</summary>
        public List<MemberDescriptor>? Properties { get; set; }

        /// <inheritdoc/>
        public override string ToString() => TypeFullname;
    }

    /// <summary>One property, method parameter, or enum value referenced from a <see cref="TypeDescriptor"/>
    /// or <see cref="MessageHandlerDescriptor"/>.</summary>
    public sealed class MemberDescriptor
    {
        /// <summary>What kind of member this describes.</summary>
        public enum MemberType
        {
            /// <summary>A named value of an enum <see cref="TypeDescriptor"/>.</summary>
            EnumValue,
            /// <summary>A public property of an object <see cref="TypeDescriptor"/>.</summary>
            Property,
            /// <summary>A parameter of a <see cref="MessageHandlerDescriptor"/>.</summary>
            MethodParameter
        }

        /// <summary>The member's name.</summary>
        public required string Name { get; set; }

        /// <summary>The member's type's full name.</summary>
        public required string TypeName { get; set; }

        /// <summary>The member's own XML doc summary, if the assembly ships one.</summary>
        public string? Description { get; set; }

        /// <summary>What kind of member this is.</summary>
        public MemberType Type { get; set; }

        /// <summary>True if this is an optional method parameter.</summary>
        public bool IsOptional { get; set; }

        /// <summary>The default value, for an optional parameter or an enum value's underlying number.</summary>
        public object? DefaultValue { get; set; }
    }

    /// <summary>The described package's name.</summary>
    public required string PackageName { get; set; }

    /// <summary>Every <c>[MessageHandler]</c>-attributed method this package exposes.</summary>
    public List<MessageHandlerDescriptor> MessageHandlers { get; set; } = new();

    /// <summary>Every type referenced by <see cref="MessageHandlers"/>' parameters/responses.</summary>
    public List<TypeDescriptor> MessageHandlerTypes { get; set; } = new();

    /// <summary>Every <c>[TelemetryItem]</c>-attributed type this package publishes.</summary>
    public List<TypeDescriptor> TelemetryItemTypes { get; set; } = new();
}
