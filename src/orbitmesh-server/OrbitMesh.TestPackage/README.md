# OrbitMesh.TestPackage

Minimal sanity-check package exercising `PackageHost`'s connect/settings/telemetry/message-handler
surface end-to-end against a real Server/Edge. Dev-only fixture, not a distributable package - it
stays in this repo and references `OrbitMesh.Common` via `ProjectReference`, not published anywhere.

## Running it

Deploy it to a local Edge like any other package (see
[Quick install](https://orbitmesh-project.github.io/orbitmesh/guide/installation/)), or run it
directly against a Server you already have up: `dotnet run -- <serverUri> <edgeName> <packageName> <accessKey>`.
