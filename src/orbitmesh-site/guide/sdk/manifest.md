# PackageInfo.xml

Every package ships a manifest describing itself to the Server/Console:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package Name="DayInfo" Version="0.0.0" Author="Your Name"
         URL="https://example.com"
         Description="What this package does"
         Icon="icon.jpg"
         ExecutableFilename="OrbitMesh.DayInfo.dll"
         Runtime="DotNet">
  <Settings>
    <Setting name="TimeZone" isRequired="true" type="Int32" description="UTC offset" />
    <Setting name="ApiKey" isRequired="true" type="Password" description="Third-party API key" />
    <Setting name="Configuration" isRequired="true" type="JsonObject" description="Structured config">
      <defaultContent>{ "RefreshIntervalSeconds": 900 }</defaultContent>
    </Setting>
  </Settings>
  <Dependencies>
    <Dependency name="OrbitMesh.Common" version="1.0.1" />
  </Dependencies>
  <Compatibility dotNetTargetPlatform="net10.0" />
</Package>
```

`Version` is a placeholder. It's stamped from the project's own `.csproj` `<Version>` at build time - see [Building release artifacts](/guide/installation/manual-build). You maintain the version number in one place.

`Runtime` (`DotNet` or `Python`) declares which Edge runtime `ExecutableFilename` needs. Defaults to `DotNet` if omitted. An Edge refuses to start a package declaring the other runtime, rather than trying and failing to launch it - see the [Python SDK/Edge](https://github.com/orbitmesh-project/orbitmesh) for building a Python package.

## Setting types

| `type` | Meaning |
| --- | --- |
| `Boolean`, `String`, `Double`, `Int32`, `Int64`, `DateTime`, `TimeSpan` | Plain scalar, parsed with `GetSettingValue<T>` |
| `Password` | Same storage as `String` - only changes how the Console renders/masks the field |
| `JsonObject` | Free-form JSON, read with `GetSettingAsJson<T>()` |
| `XmlDocument` | Free-form XML, read with `GetSettingAsXmlDocument()` |
| `ConfigurationSection` | An arbitrary nested config block |

Use `defaultValue="..."` for a scalar default, or a `<defaultContent>` element for a multi-line JSON/XML default (as shown above).
