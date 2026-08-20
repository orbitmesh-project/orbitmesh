# Changelog - OrbitMesh.Server

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Server.csproj`, which is what's reported to the update server (see
`Services/ServerSelfUpdater.cs` and `Services/UpdateCheckService.cs`).

## [1.2.8] - 2026-08-19

### Fixed

- `PreserveLiveState` moved `packages/` into the staging directory instead of copying it, before the
  handoff to `OrbitMesh.Updater` even succeeds - if anything went wrong between that move and a
  completed swap (the Updater killed before it could run, this process crashing, ...), the only copy
  was left stranded in an abandoned staging directory, which `ApplyAsync`'s next attempt deletes
  outright as its own "start clean" first step. Now copies, matching `appsettings.json`/`keys/` -
  the live directory keeps its real `packages/` until a swap actually completes.

- `CredentialUsageTracker.RecordUse` was only ever called from `AccessKeyAuthenticationMiddleware`
  (the REST gate) - packages, Edges, the Console, and Consumers all authenticate over SignalR, not
  REST, so their credentials' "Last Used" stayed "Never" forever. Now also recorded from each Hub's
  `OnConnectedAsync` (`OrbitMeshHub`, `EdgeHub`, `ControlHub`, `ConsumerHub`) right after their
  existing access check succeeds.

## [1.2.7] - 2026-08-19

### Fixed

- `UpdateVersionComparer.IsNewer` fell back to a plain string comparison when either version wasn't
  a parseable `System.Version` - the SDK appends `+<git-sha>` (SemVer build metadata) to
  `AssemblyInformationalVersionAttribute` by default in any build done inside a git working tree
  (CI included), so the running version never string-matched the clean version the update server
  reports, and "update available" stuck around forever after a successful update. Now strips the
  `+...` suffix before comparing, and `OrbitMesh.Server.csproj` sets
  `IncludeSourceRevisionInInformationalVersion=false` so the suffix isn't there to strip in the
  first place.

## [1.2.6] - 2026-08-19

### Fixed

- Self-update's handoff to `OrbitMesh.Updater` (`ServerSelfUpdater.LaunchUpdater`) spawned it via a
  bare `"dotnet"` command name, relying on `PATH` to resolve it - not guaranteed (`install.sh`'s
  systemd unit launches the Server itself via `dotnet`'s absolute path precisely because it isn't).
  On Linux this doesn't fail loudly: `Process.Start` succeeds (`fork()` succeeds), only the child's
  `exec()` fails, silently, before `OrbitMesh.Updater`'s own code - and its log file - ever run, so
  the update looked applied but never actually happened. Now uses `Environment.ProcessPath` (the
  exact `dotnet` running the current process), same fix `RelaunchCommand.Capture()` already uses for
  the analogous "relaunch myself" problem.

## [1.2.5] - 2026-08-19

### Fixed

- Pinned `Microsoft.OpenApi` to 2.12.0 - letting it float to 3.x broke the OpenAPI XML-comment
  source generator (`IOpenApiMediaType.Example` became read-only there), and 2.3.9 (a prior lower
  pin) carried a high-severity DoS vulnerability (GHSA-v5pm-xwqc-g5wc, fixed from 2.7.5). Updated
  `NuGetFeedClient`'s search filter for `NuGet.Protocol` 7.9's `SearchFilter.PackageTypes` ->
  `PackageType` rename.

## [1.2.4] - 2026-08-18

### Fixed

- `EdgeHub` could resurrect a pending entry for an Edge that was already approved: a stale
  AccessKey the old process still had cached during its own restart handoff could race the
  approval and land one more unauthorized call, re-adding it to `IPendingEdgeRegistry` with
  nothing left to ever clear it again (the Edge doesn't call the unauthorized path once it
  reconnects with its real key). `RecordPendingAttempt` now checks whether the InstanceId already
  matches a configured Edge first - if so it removes any pending entry instead of recording a new
  attempt.

- Self-update didn't preserve `keys/credential-encryption.key` across the swap
  (`ServerSelfUpdater.PreserveLiveState`) - a lost key means a fresh one gets generated on next
  start, silently making every previously-encrypted Machine credential undecryptable. That alone
  didn't error at startup (a credential's ciphertext isn't touched until something tries to use
  it), but `OrbitMeshDirectory.MatchesAccessKey` only caught `FormatException` around the decrypt,
  not the `CryptographicException`/`AuthenticationTagMismatchException` AES-GCM throws for a
  ciphertext that doesn't authenticate - so `GetCredentialName` (called on every authenticated
  request, to scan the full credential list for a name match) would 500 on completely unrelated
  requests the moment any one credential in the list had gone stale this way. Both fixed: the key
  now survives a self-update, and a credential that fails to decrypt is now just treated as "not a
  match" instead of crashing the request.

### Added

- `install.sh`/`install.ps1` now preconfigure the official update server (`https://updates.orbitmesh.org`)
  with its public signing key, and the official NuGet feed (`https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json`)
  in `nuGetFeeds` - both are plain entries in `appsettings.json` and can be edited or added to for a
  self-hosted update server/feed.
- `SetPackageSettings` now rejects (400) a `JsonObject`/`XmlDocument`-typed setting whose value
  doesn't parse, resolved against the deploying package's own manifest - `{Variable}` tokens are
  substituted with a `0` placeholder first (valid JSON either bare or already inside quotes) so a
  legitimate token reference isn't flagged as malformed.
- Variables: named values (`VariableOptions`, `GET/POST rest/management/variables`,
  `DELETE .../{name}`) any package's settings can reference by writing `{Name}` anywhere in a
  setting's value, including inside a JSON setting - `IOrbitMeshDirectory.GetSettings` gained a
  `substituteVariables` parameter that replaces every token with the Variable's value, but only at
  the points settings are actually delivered to a connected package (`OrbitMeshHub.RequestSettings`,
  `OrbitMeshController.GetSettings`, `ControlHub.ReloadServerConfiguration`/`UpdatePackageSettings`)
  - never when the Console reads/writes a package's settings, so editing one package's settings can
  never bake in and silently detach a value that was meant to keep tracking the shared Variable. A
  Variable can be marked secret, encrypting its value at rest with the same `AccessKeyCipher` used
  for Machine credentials; `GET variables` never includes a secret's value, `GET
  variables/{name}/reveal` decrypts it on demand.
- Each package now gets its own auto-provisioned Machine credential (`{edge}.{package}`,
  `InsertOrUpdatePackage`/`EnsurePackageCredential`) instead of falling back to the Edge's own
  credential (see `PackageInstance.cs`'s AccessKey resolution). Its `Authorizations` default to
  `Deny` with Rules scoped to only its own edge+package telemetry, its own configured Groups, and
  Messages addressed to itself/its own edge/its own Groups - `SyncPackageCredentialAuthorizations`
  rebuilds these Rules on every deploy and on `SetPackageGroups`, so they stay in sync. Cross-package
  telemetry/messaging (e.g. a dashboard, or genuine inter-package RPC) now needs an explicit Rule
  added on that credential - previously every package shared the Edge's key with full default-Allow
  access to every other edge/package's telemetry and messages.
- `PackageHost`/`[MessageHandler]` message keys are now namespaced under the sending package's own
  name by default (`"PackageName/Key"`, see `MessageHandlerAttribute.Shared` and the `shared`
  parameter on `SendMessage`/`RegisterMessageHandler`) - two unrelated packages picking the same key
  (e.g. both handling `"Play"`) no longer cross-trigger each other's handler. The saga response key
  is exempt (fixed system key, correlated by SagaId, not package identity).
- Pending-edges list and approval flow. `EdgeHub.OnConnectedAsync` no longer aborts a connection
  that fails authorization but presents a well-formed `InstanceId` (see
  `OrbitMeshHeaderNames.InstanceId`, `EdgeDescription.InstanceId`) - it's kept open (no capability
  granted; every other hub method independently re-checks authorization) and recorded by the new
  `IPendingEdgeRegistry`, instead of being rejected and forgotten. `RegisterEdge` returning `false`
  while unauthorized doubles as a heartbeat, refreshing the pending entry every ~3s via the Edge's
  own existing retry loop. `GET/POST/DELETE /rest/management/edges/pending[...]` list, approve, and
  dismiss pending attempts. Approving creates a Machine credential + `EdgeOptions` entry (which
  gained an optional `InstanceId` field, set here) and pushes the new AccessKey down the Edge's
  still-open pending connection (`EdgeClientMethodNames.EdgeApproved`) - fire-and-forget, so the
  response also always carries the key as a manual-paste fallback in case the Edge had disconnected
  in the interim. `InstanceId` isn't enforced as an extra `CheckAccess` gate yet - `EdgeName` +
  `AccessKey` still fully gate access as before.
- `ControlHub.CheckForEdgeUpdate(edgeName)` (requires `updates:manage`) pushes
  `EdgeServerMethodNames.CheckForUpdate` to a connected Edge, so the Console can trigger an update
  check/apply on demand instead of only ever waiting for the Edge's own polling timer.
- `install.sh`'s generated systemd unit uses `KillMode=process` so `OrbitMesh.Updater` - spawned as
  this process's child during self-update - survives past the handoff instead of being killed with
  the rest of the cgroup. It also writes a sudoers rule scoped to `systemctl start/stop` for the
  unit, since the service runs as a non-root user; `ProcessRestarter` calls it via `sudo`. The rule's
  `systemctl` path is now canonicalized with `readlink -f` (sudo resolves it via its own
  `secure_path`, which can land on a different but equivalent symlinked path than root's own `$PATH`
  at install time - a mismatch there silently falls back to requiring a password), and
  `verify_sudoers_rule` proves the rule actually works for the real service user right after the
  unit is enabled, instead of only surfacing a problem mid self-update with no terminal to fix it from.
  Both the sudoers rule and the systemd unit/verification step now key off whether `server`/`edge`
  are actually present on disk (`SERVER_PRESENT`/`EDGE_PRESENT`), not whether they were freshly
  re-fetched this run - re-running the script with "n" to every overwrite prompt (e.g. just to pick
  up an `OrbitMesh.Updater` fix) used to silently skip both, leaving an already-running service with
  no sudoers rule at all.

## [1.0.0] - 2026-08-13

Baseline - v1. Everything up to this point (credentials/scopes, file servers, self-update,
package registry, telemetry, groups, ...) is considered the starting line, not individually
logged. Every change from here on gets an entry above.
