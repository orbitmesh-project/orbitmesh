# Running as a background service

Server and Edge exit cleanly, rather than crashing, when handing off to a self-update. A plain "restart on any exit" policy would race that handoff and fight over the files being swapped in.

`install.sh`/`install.ps1` (see [Quick install](/guide/installation/)) configure this correctly for you. To do it by hand:

## Windows

`cicd/install-windows-service.ps1`:

```powershell
.\install-windows-service.ps1 -ServiceName OrbitMeshServer -BinaryPath "C:\OrbitMesh\server\OrbitMesh.Server.dll" -DisplayName "OrbitMesh Server"
```

## Linux (systemd)

A unit with `Restart=on-failure` (not `always`) - it only fires on an actual crash, not the update handoff's clean `exit(0)`. Also set:

- `Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` - many minimal Linux images (including a fresh Raspberry Pi OS install) don't ship `libicu`. This codebase only does ordinal string comparisons, so invariant mode is safe.
- `KillMode=process` - the default (`control-group`) would also kill `OrbitMesh.Updater`, spawned as this process's child during a self-update handoff.
- A sudoers rule letting the service's own user run `systemctl start`/`stop` on its own unit without a password - the user running the service isn't root, and the restart step of self-update needs it.

`install.sh` sets all of this up automatically. See its output for the exact unit/sudoers content if writing them by hand.
