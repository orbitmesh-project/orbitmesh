# OrbitMesh CICD

Scripts to install, build, and publish OrbitMesh.

Official packages (DayInfo, OpenWeather, ...) live in the separate `orbitmesh-packages` repo
(sibling of this one) - `build-packages.ps1` moved there, see its own `cicd/README.md`.

## Prerequisites

- PowerShell 5.1+
- .NET SDK (`dotnet` on PATH)
- `openssl` on PATH, only for signed releases

## Fresh install

```bash
sudo ./install.sh          # Linux/systemd, e.g. a Raspberry Pi
```
```powershell
.\install.ps1               # Windows, elevated prompt
```

Fetches the latest published release of each component (server, edge, console, updater), installs
.NET if missing, writes a starter config, sets up the systemd/Windows services. See
[the install guide](../src/orbitmesh-site/guide/installation/index.md) for details.

## Building releases

```powershell
Set-Location .\cicd

.\release-server.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key
.\release-edge.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key
.\release-static-site.ps1 -SourceDir ..\src\orbitmesh-console -Slug orbitmesh-console -Version 1.2.0
```

Outputs: `cicd/build/<slug>-<version>.zip`, `cicd/build/manifest.json`, and `manifest.json.sig` if
`-PrivateKeyPath` was given.

## Scripts

| Script | Does | Key params |
| --- | --- | --- |
| `release-server.ps1` | Build, publish, sign, zip `OrbitMesh.Server` | `-Version`, `-Runtime` (empty = portable), `-PrivateKeyPath` |
| `release-edge.ps1` | Same, for `OrbitMesh.Edge` | Same |
| `release-static-site.ps1` | Package a static site (Console, or any other) | `-SourceDir`, `-Slug`, `-ExcludeFiles` |
| `release-updater.ps1` | Build, publish, sign, zip `OrbitMesh.Updater` (never self-updates itself, but still ships a manifest - most update services require one) | Same as `release-server.ps1` |
| `install-windows-service.ps1` | Register Server/Edge as a Windows Service, update-safe restart settings | `-ServiceName`, `-BinaryPath` |
| `install.sh` / `install.ps1` | Fresh install end to end | see `-?`/comments |

Notes:
- No `-PrivateKeyPath` → unsigned release.
- Zips are built via `New-ZipArchive` to guarantee `/`-separated paths (Linux-compatible).
- Update-server slugs: `orbitmesh-server`, `orbitmesh-edge`, `orbitmesh-console`,
  `orbitmesh-updater` (this last one only needed for `install.sh`/`install.ps1` to fetch it - self-update
  never touches it).
