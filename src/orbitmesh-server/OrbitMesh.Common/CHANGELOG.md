# Changelog - OrbitMesh.Common

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Common.csproj`, which is what gets published to
[nuget.org](https://www.nuget.org/packages/OrbitMesh.Common).

## [1.2.3]

### Fixed

- `RegisterMessageHandlers`'s dispatch error path built its `WriteError` message by concatenating a
  handler's own exception message directly into what became the composite format string - a handler
  exception containing literal `{`/`}` (e.g. an ffmpeg error like `"...not one of 40{0,1,3,4}..."`,
  hit by a real package) crashed `string.Format` itself with a secondary `FormatException`, replacing
  the actual error in the log with a confusing, unrelated stack trace. Now passes the message key and
  exception message as `{0}`/`{1}` arguments instead of folding them into the format string.

## [1.2.2] - 2026-08-21

### Fixed

- `PackageHost` never sent the `IsReconnection` header, so `OrbitMeshHub.OnConnectedAsync` treated
  every reconnect - including an ordinary transient network blip, not just a Server restart - as a
  brand new connection and purged the package's telemetry items. Subscribers would see values
  vanish for up to a full polling interval after any hiccup, with nothing actually wrong. Now set
  to `false` for the very first connect and flipped to `true` right after it succeeds, so every
  later negotiate (SignalR's own automatic reconnect, or the manual retry in `Closed`) correctly
  reports itself as a reconnection.

## [1.2.1] - 2026-08-19


### Changed

- `Microsoft.AspNetCore.SignalR.Client`/`Microsoft.Extensions.Http` bumped to 10.0.11.

## [1.2.0] - 2026-08-18

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
