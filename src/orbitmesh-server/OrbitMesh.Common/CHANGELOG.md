# Changelog - OrbitMesh.Common

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Common.csproj`, which is what gets published to
[nuget.org](https://www.nuget.org/packages/OrbitMesh.Common).

## [Unreleased]

### Added

- `MessageHandlerAttribute.Shared` (and a `shared` parameter on `RegisterMessageHandler`/`SendMessage`)
  - by default, message keys are namespaced under the sending package's name (`{PackageName}/{key}`)
    to prevent cross-package handler collisions; `shared: true` opts a key out for genuinely
    cross-package conventions (e.g. the saga response key, correlated by SagaId, not package identity).
- `IndefiniteRetryPolicy` (`OrbitMeshHubConnectionExtensions`) - replaces the finite `[0,3,3,5,10]`s
  reconnect backoff (which gave up permanently after ~21s) with one that keeps retrying every 10s
  after the initial backoff, so a package survives a Server restart/reboot instead of dying for good.

### Fixed

- `PackageOutputPath` pointed one level too shallow (`local-nuget` inside the wrong repo folder
  entirely) - fixed to the actual shared feed both `nuget.config` files reference.
