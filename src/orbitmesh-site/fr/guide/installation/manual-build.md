# Build manuel

## Prérequis

- .NET SDK (correspondant au target framework des packages, ex. `net10.0`)
- PowerShell 5.1+ pour exécuter les scripts `cicd/*.ps1`
- `openssl` sur le PATH, uniquement si vous signez les releases

## Construire les artefacts de release

```powershell
Set-Location .\cicd

# 1) Packages (voir Créer un package)
.\build-packages.ps1

# 2) Server et Edge - optionnellement signés, portables par défaut (passez -Runtime
#    pour un build cible une RID précise, ex. linux-arm64)
.\release-server.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key
.\release-edge.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key

# 3) Console (un site statique, servi par le Server lui-même)
.\release-static-site.ps1 -SourceDir ..\src\orbitmesh-console -Slug orbitmesh-console -Version 1.2.0
```

Cela produit `cicd/build/orbitmesh-server-<version>.zip`, `orbitmesh-edge-<version>.zip`, `orbitmesh-console-<version>.zip`, un `manifest.json` décrivant chacun, et - si une clé de signature a été fournie - `manifest.json.sig`.

Sans `-PrivateKeyPath`, une release est simplement non signée. Le flux de mise à jour automatique Server/Edge (voir [Récupération et mises à jour](/fr/guide/architecture/recovery-updates)) peut quand même la consommer, juste sans vérification de signature.

## Référence : scripts de release

| Script | Produit |
| --- | --- |
| `build-packages.ps1` | Un `.zip` (et un `.nupkg`) par package sous `src/orbitmesh-packages` |
| `release-server.ps1` | `orbitmesh-server-<version>.zip`, optionnellement signé, portable par défaut |
| `release-edge.ps1` | `orbitmesh-edge-<version>.zip`, optionnellement signé, portable par défaut |
| `release-static-site.ps1` | `<slug>-<version>.zip` pour un site statique (Console, ou autre) |
| `release-updater.ps1` | `OrbitMesh.Updater`, pour déploiement manuel |
| `install-windows-service.ps1` | Enregistre Server/Edge comme service Windows avec les réglages de redémarrage compatibles mise à jour |
| `install.sh` / `install.ps1` | Installation fraîche de bout en bout (récupère la dernière release, .NET si manquant, config, services) |
