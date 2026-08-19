# Héberger d'autres sites

`FileServers` n'est pas spécifique à la Console. C'est une liste - le Server peut servir plusieurs sites statiques depuis le même process/port, chacun avec son propre chemin et son propre dossier physique :

```json
{
  "fileServers": [
    { "enable": true, "path": "/console", "physicalPath": "console", "isSpa": true, "updateProjectSlug": "orbitmesh-console" },
    { "enable": true, "path": "/dashboard", "physicalPath": "dashboard", "isSpa": true, "updateProjectSlug": "my-dashboard", "preserveFiles": ["config.json"] }
  ]
}
```

`preserveFiles` préserve un fichier à travers les self-update - utile pour un `config.json` qui contient l'URL du Server et l'AccessKey propres à cet appareil, qu'aucun build de release ne peut connaître à l'avance.

Packagez-le et publiez-le comme la Console : `release-static-site.ps1 -SourceDir ... -Slug my-dashboard -Version 1.0.0 -ExcludeFiles config.json`. Le `StaticSiteUpdater` du Server le suit et le met à jour ensuite comme n'importe quel autre site.

## Se connecter depuis ce site

Un dashboard ne s'enregistre pas comme un package - il se connecte à `ConsumerHub` en tant que client lecture seule. Voir [Contrôle d'accès](/fr/guide/architecture/access-control) pour le fonctionnement de l'`AccessKey` et des règles d'autorisation ; un Consumer a juste besoin d'un identifiant activé, sans scope.

Depuis `ConsumerHub`, il peut :

- S'abonner à des telemetry items précis et recevoir les mises à jour poussées.
- S'abonner à des groupes de messages, et en envoyer lui-même.

Il ne voit jamais les settings des packages ni les actions de contrôle - ça, c'est pour les packages et la Console, via `OrbitMeshHub`/`ControlHub`.

## Client JS

La Console embarque déjà un petit wrapper SignalR générique - `Scripts/signalr-client.js` - avec une factory faite pour exactement ce cas :

```js
import { createOrbitMeshConsumer } from "./signalr-client.js";

const consumer = createOrbitMeshConsumer("https://orbitmesh.example.com", accessKey, "dashboard");
consumer.client.registerTelemetryItemLink("salon", "DayInfo", "Sunrise", "String", (item) => {
  console.log(item.Value);
});
await consumer.connection.start();
```

Il ne dépend de rien de propre à la Console - juste `@microsoft/signalr` chargé sur la page. Rien n'empêche un dashboard externe d'importer ce fichier dès aujourd'hui.

::: tip TODO
Extraire `signalr-client.js` de `orbitmesh-console/Scripts/` vers un emplacement partagé (un petit package npm, ou un dossier dans un dépôt commun) pour qu'un dashboard externe puisse en dépendre sans copier le fichier.
:::
