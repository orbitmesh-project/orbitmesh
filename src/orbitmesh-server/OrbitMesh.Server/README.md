# OrbitMesh.Server

The central hub. Holds the only persistent state (`appsettings.json`): known Edges, credentials,
variables, package assignments, and the package repository. Exposes four SignalR hubs (`EdgeHub`,
`OrbitMeshHub`, `ControlHub`, `ConsumerHub`) and the Management REST API, and serves the Console (and
any other configured static site) via `FileServers`.

See [Architecture overview](https://orbitmesh-project.github.io/orbitmesh/guide/architecture/) for
the full picture.

## Configuration

`appsettings.json` (copy from `appsettings.json.example`) - see
[Access control](https://orbitmesh-project.github.io/orbitmesh/guide/architecture/access-control)
and [Variables](https://orbitmesh-project.github.io/orbitmesh/guide/architecture/variables).

## Running it

See [Running Server & Edge](https://orbitmesh-project.github.io/orbitmesh/guide/installation/running)
and [Background service setup](https://orbitmesh-project.github.io/orbitmesh/guide/installation/background-service).

## Releasing

Version in `<Version>` (`OrbitMesh.Server.csproj`), reported to the update server. Built via
`cicd/release-server.ps1` - see [cicd/README.md](../../../cicd/README.md).
