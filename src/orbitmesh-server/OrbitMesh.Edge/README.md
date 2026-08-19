# OrbitMesh.Edge

Runs on each device. Connects to the Server (`EdgeHub`), downloads its assigned packages, launches
and supervises each as its own OS process - it's a thin host, not on the data path for messages or
telemetry (those go straight from each package to the Server via `OrbitMeshHub`).

## Configuration

`appsettings.json` (copy from `appsettings.json.example`): `Edge.OrbitMeshServerUri`,
`Edge.OrbitMeshAccessKey` (blank until approved from the Console), `Edge.UpdateOptions` for
self-update.

## Running it

See [Running Server & Edge](https://orbitmesh-project.github.io/orbitmesh/guide/installation/running)
and [Background service setup](https://orbitmesh-project.github.io/orbitmesh/guide/installation/background-service).

## Releasing

Version in `<Version>` (`OrbitMesh.Edge.csproj`), reported to the update server. Built via
`cicd/release-edge.ps1` - see [cicd/README.md](../../../cicd/README.md).
