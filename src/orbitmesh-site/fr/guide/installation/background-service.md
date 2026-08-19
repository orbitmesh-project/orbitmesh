# Lancer en service d'arrière-plan

Server et Edge se terminent proprement, plutôt que de crasher, lors d'une passation de self-update. Une politique "redémarrer sur n'importe quelle sortie" entrerait en concurrence avec cette passation et se battrait sur les fichiers en cours d'échange.

`install.sh`/`install.ps1` (voir [Installation rapide](/fr/guide/installation/)) configurent ça correctement pour vous. Pour le faire à la main :

## Windows

`cicd/install-windows-service.ps1` :

```powershell
.\install-windows-service.ps1 -ServiceName OrbitMeshServer -BinaryPath "C:\OrbitMesh\server\OrbitMesh.Server.dll" -DisplayName "OrbitMesh Server"
```

## Linux (systemd)

Une unit avec `Restart=on-failure` (et non `always`) - elle ne se déclenche que sur un vrai crash, pas sur le `exit(0)` propre de la passation de mise à jour. Réglez aussi :

- `Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` - beaucoup d'images Linux minimales (y compris une install fraîche de Raspberry Pi OS) n'embarquent pas `libicu`. Ce code ne fait jamais que des comparaisons ordinales, donc le mode invariant est sans risque.
- `KillMode=process` - le mode par défaut (`control-group`) tuerait aussi `OrbitMesh.Updater`, lancé comme process enfant lors d'une passation de self-update.
- Une règle sudoers permettant à l'utilisateur du service de faire `systemctl start`/`stop` sur sa propre unit sans mot de passe - l'utilisateur du service n'est pas root, et l'étape de redémarrage du self-update en a besoin.

`install.sh` met tout ça en place automatiquement. Voir sa sortie pour le contenu exact de l'unit/sudoers si vous les écrivez à la main.
