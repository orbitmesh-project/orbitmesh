# Recovery & updates

## Recovery options

Each package - and the Edge/Server processes themselves - has recovery options: restart on crash, up to N times within a reset window, then stay down for manual investigation.

```json
{ "restartAfterFailure": true, "numberOfRetry": 3, "resetCounterAfterMinutes": 15, "restartPackageAfterSeconds": 30 }
```

## Self-update

Server and Edge poll a configured update server, verify a downloaded release's signature against a trusted public key (if configured), then exit cleanly and hand off to `OrbitMesh.Updater` to swap the files in and relaunch.

`install.sh`/`install.ps1` point this at the official update server (`https://updates.orbitmesh.org`) by default, with the project's public signing key preconfigured in `publicKeys` - self-hosting your own update server is just a different `serverUrl` (or the `UPDATE_SERVER` environment variable when running the install script) and your own key.

See [Running as a background service](/guide/installation/background-service) for why the systemd/service restart policy has to cooperate with that handoff instead of racing it.
