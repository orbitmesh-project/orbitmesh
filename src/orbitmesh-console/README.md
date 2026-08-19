# OrbitMesh Console

The admin web UI: edges, packages, credentials, telemetry, logs, variables.

Vanilla Vue 3 (via CDN, no build step) + hand-written SignalR wrappers (`Scripts/signalr-client.js`).
Served by the Server itself as a static site (`FileServers` in `appsettings.json`), not a separate
process.

## Running it

Nothing to build - point the Server's `fileServers` entry at this folder and it's served. See
[Quick install](https://orbitmesh-project.github.io/orbitmesh/guide/installation/).

## Releasing

Version tracked in `VERSION`, bumped and packaged by `cicd/release-static-site.ps1`. See
[cicd/README.md](../../cicd/README.md).
