# Installation rapide

OrbitMesh a trois briques déployables : **Server**, **Edge**, et la **Console** (un site statique hébergé par le Server). Les trois sont construites et packagées depuis le dossier `cicd/`.

`cicd/install.sh` (Linux/systemd, ex. un Raspberry Pi) et `cicd/install.ps1` (Windows) font une installation fraîche de bout en bout : récupèrent la dernière release publiée de chaque composant (server, edge, console, `OrbitMesh.Updater`), installent .NET si manquant, écrivent un `appsettings.json` de départ, enregistrent et démarrent le service.

```bash
sudo ./install.sh
```
```powershell
.\install.ps1   # depuis une invite PowerShell en administrateur
```

Les deux installent dans l'emplacement système conventionnel de l'OS (`/opt/orbitmesh`, `Program Files\OrbitMesh`), en demandant chaque choix avec une valeur par défaut sensée. Validez avec Entrée partout pour une première installation. Aucun des deux scripts n'écrase une installation existante sans demander d'abord - relancer est sans risque.

C'est le chemin le plus rapide vers un Server + Console fonctionnels, prêts pour leur configuration admin au premier lancement. La suite couvre le build manuel, lancer les composants à la main, et la configuration en service d'arrière-plan.
