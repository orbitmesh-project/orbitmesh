# Quick install

OrbitMesh has three deployable pieces: **Server**, **Edge**, and the **Console** (a static site the Server hosts). All three are built and packaged from the `cicd/` folder.

`cicd/install.sh` (Linux/systemd, e.g. a Raspberry Pi) and `cicd/install.ps1` (Windows) do a fresh install end to end: fetch the latest published release of each component (server, edge, console, `OrbitMesh.Updater`), install .NET if missing, write a starter `appsettings.json`, register and start the service.

```bash
sudo ./install.sh
```
```powershell
.\install.ps1   # from an elevated PowerShell prompt
```

Both install under the OS-conventional system location (`/opt/orbitmesh`, `Program Files\OrbitMesh`), prompting for each choice with a sensible default. Press Enter through on a first run. Neither script overwrites an existing install without asking first - re-running is safe.

This is the fastest path to a running Server + Console, ready for first-run admin setup. Read on for manual builds, running components by hand, or background-service setup.
