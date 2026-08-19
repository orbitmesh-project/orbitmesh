namespace OrbitMesh.Deployment;

/// <summary>How a <see cref="PackageSetting"/>'s value is interpreted and rendered by the Console.
/// The underlying storage is always a plain string in <c>settings.json</c>/the Server's config - this
/// only governs parsing (<c>PackageHost.GetSettingValue&lt;T&gt;</c> and friends) and UI.</summary>
public enum SettingType
{
    /// <summary>"true"/"false", read with <c>GetSettingValue&lt;bool&gt;</c>.</summary>
    Boolean,
    /// <summary>Plain text, read with <c>GetSettingValue&lt;string&gt;</c>.</summary>
    String,
    /// <summary>A floating-point number, read with <c>GetSettingValue&lt;double&gt;</c>.</summary>
    Double,
    /// <summary>A 32-bit integer, read with <c>GetSettingValue&lt;int&gt;</c>.</summary>
    Int32,
    /// <summary>A 64-bit integer, read with <c>GetSettingValue&lt;long&gt;</c>.</summary>
    Int64,
    /// <summary>An arbitrary nested configuration block.</summary>
    ConfigurationSection,
    /// <summary>A date/time value, read with <c>GetSettingValue&lt;DateTime&gt;</c>.</summary>
    DateTime,
    /// <summary>A duration, read with <c>GetSettingValue&lt;TimeSpan&gt;</c>.</summary>
    TimeSpan,
    /// <summary>Free-form XML, read with <c>GetSettingAsXmlDocument</c>.</summary>
    XmlDocument,
    /// <summary>Free-form JSON, read with <c>GetSettingAsJson</c>/<c>GetSettingAsJson&lt;T&gt;</c>.</summary>
    JsonObject,
    /// <summary>Same storage as <see cref="String"/> - only changes how the Console renders/masks the
    /// field, so a package can opt in without changing how it reads the setting.</summary>
    Password
}
