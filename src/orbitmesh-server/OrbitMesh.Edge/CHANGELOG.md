# Changelog - OrbitMesh.Edge

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Edge.csproj`, which is what's reported to the update server (see
`Services/EdgeSelfUpdater.cs` and `Services/EdgeUpdateCheckService.cs`).

## [1.1.5]

### Fixed

- `OrbitMesh.Edge.csproj` now sets `IncludeSourceRevisionInInformationalVersion=false` - without
  it the SDK appends `+<git-sha>` to `EdgeVersion.Current`, which broke `UpdateVersionComparer`'s
  exact-match fallback (see OrbitMesh.Server's own changelog for the full story) and left "update
  available" stuck on even right after a successful update.

## [1.1.4] - 2026-08-19

### Changed

- `Microsoft.Extensions.Hosting`/`.Systemd`/`.WindowsServices`/`.Http` bumped to 10.0.11,
  `NLog.Extensions.Logging` to 6.2.0.

## [1.1.3] - 2026-08-18

### Added

- `install.sh`/`install.ps1` now preconfigure the official update server (`https://updates.orbitmesh.org`)
  with its public signing key in `UpdateOptions.publicKeys` - a self-hosted update server just means a
  different `serverUrl` and key.
- Generates and persists a stable `InstanceId` (a GUID, `instance-id.txt` next to `appsettings.json`)
  on first run, sent on every connection attempt whether authorized yet or not (new
  `InstanceIdProvider`). Lets an Edge that hasn't been approved yet show up as one stable entry in
  the Server's pending-edges list across reconnect retries, instead of being indistinguishable from
  any other unrecognized attempt.
- Handles `EdgeApproved`, pushed by the Server the moment an admin approves this Edge from the
  pending list: writes the new AccessKey into its own `appsettings.json` (`Edge.OrbitMeshAccessKey`)
  and restarts itself (reusing the same relaunch mechanism as a self-update handoff) to connect with
  it - no manual copy-paste of the key required for the common case where the Edge is still running
  and connected at approval time.
- Handles `CheckForUpdate`, pushed by the Server when an admin clicks "Check for update" on the
  Console's Edges page: runs `EdgeUpdateCheckService`'s check-and-apply immediately instead of
  waiting for its own timer (`UpdateOptions.CheckIntervalMinutes`).
- Hub reconnect now retries forever (`IndefiniteRetryPolicy` in `OrbitMesh.Common`) instead of giving
  up permanently after ~21s - a Server reboot routinely takes longer than that, which left the Edge
  disconnected with no further retry attempt.
- `install.sh`'s generated systemd unit uses `KillMode=process` so `OrbitMesh.Updater` - spawned as
  this process's child during self-update - survives past the handoff instead of being killed with
  the rest of the cgroup. It also writes a sudoers rule scoped to `systemctl start/stop` for the
  unit, since the service runs as a non-root user; `ProcessRestarter` calls it via `sudo`.

### Fixed

- Self-restart (`RestartSelfAsync`, used by the Console's "Restart" button and the `EdgeApproved`
  auto-restart above) always spawned its replacement directly as its own child process. Under
  systemd/a Windows Service with `KillMode=process`, that child isn't tracked by the service
  manager at all - once the old process exits cleanly, the manager marks the unit inactive while
  the untracked replacement keeps running regardless, and a later `systemctl`/service restart then
  launches a second, competing instance instead of recognizing one is already up. Now detects a
  service-managed host and spawns a tiny detached watcher that waits for this process to actually
  exit, then asks the service manager itself to start the unit - exactly one tracked process at
  any time, same reasoning as `OrbitMesh.Updater` existing as a separate process for a full update.

## [1.0.0] - 2026-08-13

Baseline - v1. Everything up to this point is considered the starting line, not individually
logged. Every change from here on gets an entry above.
