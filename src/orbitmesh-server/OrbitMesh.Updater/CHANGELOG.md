# Changelog - OrbitMesh.Updater

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Updater.csproj`.

## [1.1.1] - 2026-08-18

### Changed

- `RestartMode.Systemd` now runs `sudo systemctl start`/`stop` instead of a bare `systemctl` call
  (`ProcessRestarter.cs`) - the unit's `SERVICE_USER` isn't root, so it needs the sudoers rule
  `install.sh` writes to restart its own service.
