# Settings

Declared in [PackageInfo.xml](/guide/sdk/manifest), read at runtime:

```csharp
int timezone = PackageHost.GetSettingValue<int>("TimeZone");
bool has = PackageHost.ContainsSetting("ApiKey");
PackageHost.TryGetSettingValue<double>("Latitude", out var lat, defaultValue: 0);

// A JsonObject-typed setting (see the setting types table in PackageInfo.xml):
var config = PackageHost.GetSettingAsJson<MyConfig>("OpenWeatherConfiguration");
```

`SettingsUpdated` fires whenever the Console pushes a settings change while the package is running. Subscribe to it if a live setting (an API key, say) needs to take effect without a restart - don't just read settings once in `OnStart`.

## Variable tokens

A setting's value may contain a `{Name}` token, resolved server-side against the Server's Variables (Console → Variables) before delivery. `GetSettingValue`/`GetSettingAsJson` always see the substituted value, never the literal token. Nothing to do on the package's side - see [Variables](/guide/architecture/variables) for the admin-side view.

The Console's per-package Settings panel lists available Variable tokens above the fields themselves - click one to copy it, then paste into any setting value:

![Package Settings panel showing available Variable tokens above the setting fields](/screenshots/settings-variable-tokens.jpg)
