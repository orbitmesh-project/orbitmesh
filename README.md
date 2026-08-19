# OrbitMesh

A stateless, self-hosted hub for edge devices and home automation packages.

Run small automation packages - weather feeds, network tools, smart-home integrations, anything
you write yourself - on your own devices, managed from one place. No database, no history: the
Console always shows the current state a package last reported.

**[Documentation](https://orbitmesh-project.github.io/orbitmesh/)** · **[Official packages](https://orbitmesh-project.github.io/orbitmesh/packages)**

## Components

- **Server** (`src/orbitmesh-server/OrbitMesh.Server`) - the central hub. Tracks connected Edges, holds the package repository, pushes configuration and updates.
- **Edge** (`src/orbitmesh-server/OrbitMesh.Edge`) - runs on each device. Downloads, runs and supervises its assigned packages.
- **Console** (`src/orbitmesh-console`) - the web UI: edges, packages, credentials, telemetry, logs.
- **Common** (`src/orbitmesh-server/OrbitMesh.Common`, [NuGet](https://www.nuget.org/packages/OrbitMesh.Common)) - the Package SDK (`PackageHost`) referenced by any package.

Official packages (DayInfo, OpenWeather, NetworkTools, ...) live in the sibling
[orbitmesh-packages](https://github.com/orbitmesh-project/orbitmesh-packages) repo.

## Getting started

```bash
sudo ./cicd/install.sh          # Linux/systemd
```
```powershell
.\cicd\install.ps1               # Windows, elevated prompt
```

See the [Quick install guide](https://orbitmesh-project.github.io/orbitmesh/guide/installation/) for details, or [Building a package](https://orbitmesh-project.github.io/orbitmesh/guide/sdk/) to write your own.

## Acknowledgments

The package/SDK model (a Server, small device-supervising Edges, and packages built against a shared
SDK) is inspired by [Constellation](https://github.com/myconstellation) by Sébastien Warin - its
Server and Sentinel (Edge) were never open-sourced, and its public packages/SDK repos haven't seen
activity since 2024. OrbitMesh is an independent implementation, not a fork.

## License

MIT - see [LICENSE](LICENSE).
