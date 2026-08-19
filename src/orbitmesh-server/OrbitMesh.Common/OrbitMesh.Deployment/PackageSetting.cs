using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>One <c>&lt;Setting&gt;</c> declared in a package's <c>PackageInfo.xml</c> manifest - name,
/// type, and default value/content.</summary>
public sealed class PackageSetting
{
    /// <summary>The setting's key, as used with <c>PackageHost.GetSettingValue</c>.</summary>
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>How the value is parsed/rendered.</summary>
    [XmlAttribute("type")]
    public SettingType Type { get; set; }

    /// <summary>If true and no value/default is available, the package logs an error on startup
    /// (see <c>PackageHost</c>'s manifest-checking on connect) - it does not block startup itself.</summary>
    [XmlAttribute("isRequired")]
    public bool IsRequired { get; set; }

    /// <summary>An optional XML Schema used to validate an <see cref="SettingType.XmlDocument"/>-typed
    /// value in the Console's editor.</summary>
    [XmlAttribute("schemaXSD")]
    public string? SchemaXSD { get; set; }

    /// <summary>Human-readable description, shown in the Console's settings editor.</summary>
    [XmlAttribute("description")]
    public string? Description { get; set; }

    /// <summary>The default value for a scalar setting - see <see cref="DefaultContent"/> for a
    /// multi-line JSON/XML default instead.</summary>
    [XmlAttribute("defaultValue")]
    public string? DefaultValue { get; set; }

    /// <summary>If true, a matching value in the package's local <c>settings.json</c> is ignored -
    /// the setting must come from the Server/Console instead.</summary>
    [XmlAttribute("ignoreLocalValue")]
    public bool IgnoreLocalValue { get; set; }

    /// <summary>If true, <see cref="DefaultValue"/>/<see cref="DefaultContent"/> is never used to
    /// fill in a missing value - the setting is left genuinely unset instead.</summary>
    [XmlAttribute("ignoreDefaultValue")]
    public bool IgnoreDefaultValue { get; set; }

    /// <summary>A multi-line default value (typically JSON or XML) - see <see cref="DefaultValue"/>
    /// for a short scalar default instead.</summary>
    [XmlElement("defaultContent")]
    public string? DefaultContent { get; set; }

    /// <summary>The effective default: <see cref="DefaultContent"/> if set, otherwise
    /// <see cref="DefaultValue"/>, otherwise an empty string.</summary>
    [XmlIgnore]
    public string DefaultSettingValue =>
        !string.IsNullOrEmpty(DefaultContent) ? DefaultContent : DefaultValue ?? string.Empty;
}
