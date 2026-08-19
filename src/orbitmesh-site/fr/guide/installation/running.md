# Lancer Server et Edge

## Server

Le Server est une app ASP.NET Core. `appsettings.json` pilote tout : URLs d'écoute, identifiants, edges, serveurs de fichiers pour la Console. Structure minimale :

```json
{
  "OrbitMesh": {
    "packagesRootDirectory": "packages",
    "listenUrls": ["http://+:8088"],
    "fileServers": [
      { "enable": true, "path": "/console-next", "physicalPath": "../console", "isSpa": true, "preserveFiles": [] }
    ],
    "credentials": [],
    "edges": []
  }
}
```

Partez d'une liste `credentials` vide. La Console détecte ça au premier chargement et vous guide pour créer le compte administrateur (`GET /rest/management/setup/status` / `POST /rest/management/setup/create-admin`). Rien à générer à la main.

Lancez-le directement avec `dotnet OrbitMesh.Server.dll`, ou installez-le comme [service d'arrière-plan](/fr/guide/installation/background-service) pour qu'il survive aux redémarrages et se mette à jour proprement.

## Edge

Chaque appareil devant faire tourner des packages a besoin de son propre process Edge, pointé vers le Server :

```json
{
  "Edge": {
    "OrbitMeshServerUri": "http://votre-server:8088",
    "OrbitMeshAccessKey": "",
    "EdgeName": "edge-salon",
    "LocalPackagesDirectory": "Packages"
  }
}
```

Aucun credential requis au départ. Lancez l'Edge avec un `OrbitMeshAccessKey` vide (ou faux) et il apparaît sous **Edges → Pending edges** dans la Console, identifié par un GUID qu'il génère et persiste au premier lancement (`instance-id.txt`). L'ID reste stable d'une reconnexion à l'autre, avant même d'avoir un nom reconnaissable.

Approuvez-le dans la Console (nom modifiable, pré-rempli avec ce qu'il a déclaré). Le Server crée un identifiant correspondant et pousse l'AccessKey directement sur la connexion de l'Edge - qui l'enregistre dans son `appsettings.json` et se reconnecte tout seul. Aucun copier-coller.

Si l'Edge n'est plus connecté au moment de l'approbation, la Console affiche quand même la clé générée une fois, à coller à la main.

L'Edge télécharge tout package qui lui est assigné depuis le dépôt de packages du Server au démarrage, et revérifie dès que le Server signale un changement.
