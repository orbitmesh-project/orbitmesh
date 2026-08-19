# Récupération et mises à jour

## Options de récupération

Chaque package - et les process Edge/Server eux-mêmes - a des options de récupération : redémarrer sur crash, jusqu'à N fois dans une fenêtre de reset, puis rester arrêté pour investigation manuelle.

```json
{ "restartAfterFailure": true, "numberOfRetry": 3, "resetCounterAfterMinutes": 15, "restartPackageAfterSeconds": 30 }
```

## Self-update

Server et Edge interrogent un serveur de mise à jour configuré, vérifient la signature d'une release téléchargée contre une clé publique de confiance (si configurée), puis sortent proprement et passent la main à `OrbitMesh.Updater` pour échanger les fichiers et relancer.

`install.sh`/`install.ps1` pointent par défaut vers le serveur de mise à jour officiel (`https://updates.orbitmesh.org`), avec la clé publique de signature du projet préconfigurée dans `publicKeys` - héberger votre propre serveur de mise à jour revient juste à changer `serverUrl` (ou la variable d'environnement `UPDATE_SERVER` lors de l'installation) et votre propre clé.

Voir [Service d'arrière-plan](/fr/guide/installation/background-service) pour pourquoi la politique de redémarrage systemd/service doit coopérer avec cette passation plutôt que d'entrer en course avec elle.
