# PackageInfo.xml

Chaque package embarque un manifeste qui se décrit au Server/à la Console :

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package Name="DayInfo" Version="0.0.0" Author="Votre nom"
         URL="https://example.com"
         Description="Ce que fait ce package"
         Icon="icon.jpg"
         ExecutableFilename="OrbitMesh.DayInfo.dll"
         Runtime="DotNet">
  <Settings>
    <Setting name="TimeZone" isRequired="true" type="Int32" description="Décalage UTC" />
    <Setting name="ApiKey" isRequired="true" type="Password" description="Clé API tierce" />
    <Setting name="Configuration" isRequired="true" type="JsonObject" description="Config structurée">
      <defaultContent>{ "RefreshIntervalSeconds": 900 }</defaultContent>
    </Setting>
  </Settings>
  <Dependencies>
    <Dependency name="OrbitMesh.Common" version="1.0.1" />
  </Dependencies>
  <Compatibility dotNetTargetPlatform="net10.0" />
</Package>
```

`Version` est un placeholder. Il est stampé depuis le `<Version>` du `.csproj` du projet au build - voir [Construire les artefacts de release](/fr/guide/installation/manual-build). Vous maintenez le numéro de version à un seul endroit.

`Runtime` (`DotNet` ou `Python`) déclare quel runtime d'Edge `ExecutableFilename` nécessite. Vaut `DotNet` par défaut si omis. Un Edge refuse de démarrer un package déclarant l'autre runtime, plutôt que d'essayer et échouer à le lancer - voir le [SDK/Edge Python](https://github.com/orbitmesh-project/orbitmesh) pour construire un package Python.

## Types de setting

| `type` | Signification |
| --- | --- |
| `Boolean`, `String`, `Double`, `Int32`, `Int64`, `DateTime`, `TimeSpan` | Scalaire simple, parsé avec `GetSettingValue<T>` |
| `Password` | Même stockage que `String` - change juste comment la Console rend/masque le champ |
| `JsonObject` | JSON libre, lu avec `GetSettingAsJson<T>()` |
| `XmlDocument` | XML libre, lu avec `GetSettingAsXmlDocument()` |
| `ConfigurationSection` | Un bloc de config imbriqué arbitraire |

Utilisez `defaultValue="..."` pour une valeur par défaut scalaire, ou un élément `<defaultContent>` pour un défaut JSON/XML multi-ligne (comme montré ci-dessus).
