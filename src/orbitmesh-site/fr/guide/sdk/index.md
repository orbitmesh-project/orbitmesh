# Démarrage

Un package est une app console .NET classique qui référence `OrbitMesh.Common` et implémente `IPackage`. `PackageHost.Start<T>` gère le cycle de vie de la connexion - votre code n'a qu'à y réagir.

```csharp
using OrbitMesh.Package;

public static class Program
{
    private static void Main(string[] args) => PackageHost.Start<DayInfoPackage>(args);
}

public sealed class DayInfoPackage : IPackage
{
    public void OnStart()
    {
        // Appelé une fois le package démarré. Lancez votre travail de fond ici.
    }

    public void OnPreShutdown()
    {
        // Signalé juste avant l'arrêt - arrêtez d'accepter du nouveau travail.
    }

    public void OnShutdown()
    {
        // Nettoyage final.
    }

    [MessageHandler(Description = "Exemple d'appel exposé aux autres packages/à la Console.")]
    public string Ping() => "pong";
}
```

`IPackage` se limite à trois méthodes : `OnStart`, `OnPreShutdown`, `OnShutdown`. Tout le reste - settings, télémétrie, messages, logs - passe par la classe statique `PackageHost`.

- [Settings](/fr/guide/sdk/settings)
- [Télémétrie](/fr/guide/sdk/telemetry)
- [Messages (RPC)](/fr/guide/sdk/messages)
- [Logs](/fr/guide/sdk/logging)
- [PackageInfo.xml](/fr/guide/sdk/manifest)
- [Packaging et distribution](/fr/guide/sdk/packaging)
