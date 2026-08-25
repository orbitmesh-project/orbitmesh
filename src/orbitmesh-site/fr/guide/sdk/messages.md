# Messages (RPC entre packages)

`[MessageHandler]` expose une méthode que d'autres packages, ou la Console, peuvent appeler par clé :

```csharp
[MessageHandler("GetSunInfo", Description = "Calcule le lever/coucher du soleil pour une date et une position.")]
public SunInfo GetSunInfo(DateOnly date, int timezone, double latitude, double longitude) { /* ... */ }
```

Appelez `PackageHost.RegisterMessageHandlers(this)` une fois, typiquement dans `OnStart`, pour connecter chaque méthode `[MessageHandler]` d'une instance.

## Namespacing de la clé

La clé est namespacée sous le nom du package par défaut : `"DayInfo/GetSunInfo"` ici, pas `"GetSunInfo"`. Deux packages sans rapport qui choisissent la même clé ne peuvent pas déclencher accidentellement le handler de l'autre. L'appelant utilise la clé qualifiée :

```csharp
var result = await PackageHost.SendMessageAsync<SunInfo>(MessageScope.Create("DayInfo"), "DayInfo/GetSunInfo", new { date, timezone, latitude, longitude });
```

Passez `Shared = true` sur `[MessageHandler]` (et `shared: true` sur `SendMessage`/`SendMessageAsync`) pour une clé brute, non namespacée - pour un handler pensé comme une convention inter-packages que n'importe quel appelant peut atteindre sans connaître le nom de ce package.

## Portée

`MessageScope` cible un seul package, un groupe, tout l'Edge, ou tout le monde (`Package` / `Group` / `Edge` / `Others` / `All`). Choisissez le plus restreint qui atteint qui doit être atteint.

Atteindre un autre package - par nom, groupe, ou une portée de diffusion - nécessite une règle `Allow` sur la propre credential de l'appelant, laquelle a en plus besoin du scope de permission `messages:execute` (à ne pas confondre avec `MessageScope` ci-dessus - chevauchement de vocabulaire malheureux). L'identifiant propre d'un package reçoit `messages:execute` automatiquement ; un identifiant Console/Consumer qui appelle `SendMessage` depuis l'extérieur d'un package doit se le voir accorder explicitement. Voir [Contrôle d'accès](/fr/guide/architecture/access-control).
