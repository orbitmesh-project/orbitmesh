# Settings

Déclarés dans [PackageInfo.xml](/fr/guide/sdk/manifest), lus à l'exécution :

```csharp
int timezone = PackageHost.GetSettingValue<int>("TimeZone");
bool has = PackageHost.ContainsSetting("ApiKey");
PackageHost.TryGetSettingValue<double>("Latitude", out var lat, defaultValue: 0);

// Un setting de type JsonObject (voir le tableau des types dans PackageInfo.xml) :
var config = PackageHost.GetSettingAsJson<MyConfig>("OpenWeatherConfiguration");
```

`SettingsUpdated` se déclenche à chaque fois que la Console pousse un changement de settings pendant que le package tourne. Abonnez-vous si un setting live (une clé API, par exemple) doit prendre effet sans redémarrage - ne lisez pas les settings qu'une fois dans `OnStart`.

## Tokens de variable

La valeur d'un setting peut contenir un token `{Nom}`, résolu côté serveur contre les Variables du Server (Console → Variables) avant livraison. `GetSettingValue`/`GetSettingAsJson` voient toujours la valeur substituée, jamais le token littéral. Rien à faire côté package - voir [Variables](/fr/guide/architecture/variables) pour le point de vue admin.
