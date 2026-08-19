# OrbitMesh.Updater

The process Server/Edge hand off to during a self-update: waits for the parent to exit, swaps the
staged files into place, then relaunches it (directly, or via the service manager - see
`ProcessRestarter.cs`). Never self-updates itself, but still ships a signed manifest since most
release/update tooling expects one.

See [Recovery & updates](https://orbitmesh-project.github.io/orbitmesh/guide/architecture/recovery-updates)
for the full self-update flow.

## Releasing

Version in `<Version>` (`OrbitMesh.Updater.csproj`). Built via `cicd/release-updater.ps1` - see
[cicd/README.md](../../../cicd/README.md).
