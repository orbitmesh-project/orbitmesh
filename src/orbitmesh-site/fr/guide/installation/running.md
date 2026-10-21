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

![Page Edges - un edge connecté avec son OS, sa version de runtime/agent, son identifiant et son nombre de packages](/screenshots/edges.jpg)

Approuvez-le dans la Console (nom modifiable, pré-rempli avec ce qu'il a déclaré - tout ce qui s'affiche est auto-déclaré par celui qui s'est enrôlé, n'approuvez donc qu'un edge dont vous reconnaissez la machine, la plateforme et l'adresse). Le Server crée un identifiant correspondant. En attente, l'Edge interroge toutes les quelques secondes l'endpoint `rest/enroll` du Server ; une fois approuvé, il récupère la nouvelle AccessKey (et le nom sous lequel vous l'avez approuvé), enregistre les deux dans son `appsettings.json` et redémarre. Aucun copier-coller.

La Console affiche aussi la clé générée une fois, en secours, à coller à la main.

L'Edge télécharge tout package qui lui est assigné depuis le dépôt de packages du Server au démarrage, et revérifie dès que le Server signale un changement.

## Sécuriser le Server

- **Créez le compte administrateur dès le premier démarrage.** Tant que `create-admin` n'a pas été appelé, n'importe qui pouvant atteindre le port peut le créer (avec tous les scopes). Démarrez le Server sur un réseau de confiance, ou filtrez le port, jusque-là.
- **Utilisez HTTPS.** Les AccessKeys circulent dans les en-têtes des requêtes : en HTTP simple, elles sont lisibles par quiconque est sur le chemin. Placez le Server derrière un reverse proxy TLS, ou laissez Kestrel terminer le TLS avec un certificat :

  ```json
  {
    "OrbitMesh": { "listenUrls": ["https://+:8443"] },
    "Kestrel": { "Certificates": { "Default": { "Path": "server.pfx", "Password": "..." } } }
  }
  ```

  Les réponses HTTPS portent `Strict-Transport-Security`. Côté Edge, `Edge:RequireHttps` à `true` refuse de démarrer avec une `OrbitMeshServerUri` en HTTP simple vers une autre machine (par défaut, un simple avertissement est loggé, pour qu'une installation LAN existante continue de marcher).
- **Derrière un reverse proxy, listez-le dans `OrbitMesh:TrustedProxies`** (son adresse IP). Sinon tous les clients semblent venir du proxy : le verrouillage après échecs (10 tentatives par 5 minutes par IP, partagé entre REST et les hubs SignalR) devient global, et un serveur de fichiers `localhostOnly` est ouvert à tous les clients d'un proxy placé sur la même machine.
- **Les mises à jour doivent être vérifiables.** Renseignez `UpdateOptions:PublicKeys` avec la clé de signature des releases ; chaque release est alors contrôlée contre son manifeste signé (signature, hash des fichiers, version annoncée et plus récente que celle en cours). Sans clé, les mises à jour sont refusées sauf si vous mettez explicitement `UpdateOptions:AllowUnsignedReleases` à `true`. Le jeton de mise à jour n'est envoyé qu'à l'hôte du serveur de mises à jour.
- **La Console et le Kiosk ne peuvent pas être intégrés dans le cadre (frame) d'un autre site** (`frame-ancestors 'self'`).
