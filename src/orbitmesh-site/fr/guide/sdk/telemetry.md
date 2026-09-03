# Télémétrie

Les telemetry items sont la façon dont un package publie son état pour que la Console, ou d'autres packages, le voient :

```csharp
PackageHost.PushTelemetryItem("SunInfo", new SunInfo { Sunrise = ..., Sunset = ... });
```

`[TelemetryItem]` marque un type comme payload de télémétrie - documentaire/outillage uniquement, ça ne change pas la sérialisation.

Chaque telemetry item poussé apparaît automatiquement sur la page Telemetry de la Console - rien à enregistrer ni configurer côté Console :

![Page Telemetry listant les items poussés par les packages connectés](/screenshots/telemetry.jpg)

## Consommer la télémétrie d'un autre package

Utilisez `[TelemetryItemLink]` sur une propriété et appelez `PackageHost.RegisterTelemetryItemLinks(this)` une fois. Le champ reste synchronisé automatiquement :

```csharp
[TelemetryItemLink("Weather", "CurrentTemperature")]
public double? OutsideTemperature { get; set; }
```

Ou abonnez-vous de façon impérative plutôt que via une propriété :

```csharp
PackageHost.RegisterTelemetryItemCallback(item => { /* ... */ }, package: "Weather", name: "CurrentTemperature");
```
