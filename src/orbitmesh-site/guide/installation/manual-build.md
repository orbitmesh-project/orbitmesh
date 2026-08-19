# Manual build

## Prerequisites

- .NET SDK (matching the packages' target framework, e.g. `net10.0`)
- PowerShell 5.1+ to run the `cicd/*.ps1` scripts
- `openssl` on PATH, only if you sign releases

## Building release artifacts

```powershell
Set-Location .\cicd

# 1) Packages (see Building a package)
.\build-packages.ps1

# 2) Server and Edge - optionally signed, portable by default (pass -Runtime for a
#    RID-specific build, e.g. linux-arm64)
.\release-server.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key
.\release-edge.ps1 -Version 1.2.0 -PrivateKeyPath D:\keys\release-signing.key

# 3) Console (a static site, served by the Server itself)
.\release-static-site.ps1 -SourceDir ..\src\orbitmesh-console -Slug orbitmesh-console -Version 1.2.0
```

This produces `cicd/build/orbitmesh-server-<version>.zip`, `orbitmesh-edge-<version>.zip`, `orbitmesh-console-<version>.zip`, a `manifest.json` describing each, and - if a signing key was supplied - `manifest.json.sig`.

Without `-PrivateKeyPath`, a release is unsigned. The Server/Edge self-update flow (see [Recovery & updates](/guide/architecture/recovery-updates)) still consumes it, just without signature verification.

## Reference: release scripts

| Script | Produces |
| --- | --- |
| `build-packages.ps1` | One `.zip` (and one `.nupkg`) per package under `src/orbitmesh-packages` |
| `release-server.ps1` | `orbitmesh-server-<version>.zip`, optionally signed, portable by default |
| `release-edge.ps1` | `orbitmesh-edge-<version>.zip`, optionally signed, portable by default |
| `release-static-site.ps1` | `<slug>-<version>.zip` for a static site (Console, or any other) |
| `release-updater.ps1` | `OrbitMesh.Updater`, for manual deployment |
| `install-windows-service.ps1` | Registers Server/Edge as a Windows service with update-safe restart settings |
| `install.sh` / `install.ps1` | Fresh install end to end (fetch latest release, .NET if missing, config, services) |
