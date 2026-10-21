# Running Server & Edge

## Server

The Server is an ASP.NET Core app. `appsettings.json` drives everything: listen URLs, credentials, edges, file servers for the Console. Minimal shape:

```json
{
  "OrbitMesh": {
    "packagesRootDirectory": "packages",
    "listenUrls": ["http://+:8088"],
    "fileServers": [
      { "enable": true, "path": "/console-next", "physicalPath": "../console", "isSpa": true, "preserveFiles": [] }
    ],
    "credentials": [],
    "edges": []
  }
}
```

Start with an empty `credentials` list. The Console detects this on first load and walks you through creating the administrator account (`GET /rest/management/setup/status` / `POST /rest/management/setup/create-admin`). Nothing to generate by hand.

Run it directly with `dotnet OrbitMesh.Server.dll`, or install it as a [background service](/guide/installation/background-service) so it survives reboots and self-updates cleanly.

## Edge

Each device that runs packages needs its own Edge process, pointed at the Server:

```json
{
  "Edge": {
    "OrbitMeshServerUri": "http://your-server:8088",
    "OrbitMeshAccessKey": "",
    "EdgeName": "edge-salon",
    "LocalPackagesDirectory": "Packages"
  }
}
```

No credential needed up front. Start the Edge with an empty (or wrong) `OrbitMeshAccessKey` and it shows up under **Edges → Pending edges** in the Console, identified by a GUID it generates and persists on first run (`instance-id.txt`). The ID stays stable across reconnects, before it has a recognizable name.

![Edges page - a connected edge with its OS, runtime/agent version, credential and package count](/screenshots/edges.jpg)

Approve it in the Console (editable name, defaults to what it reported - everything shown is self-declared by whoever enrolled, so only approve an edge whose machine, platform and address you recognise). The Server creates a matching credential. While pending, the Edge polls the Server's `rest/enroll` endpoint every few seconds; once approved it picks up the new AccessKey (and the name you approved it under), saves both to `appsettings.json` and restarts. No copy-pasting.

The Console also shows the generated key once, as a fallback to paste in by hand.

The Edge downloads any assigned package from the Server's package repository on startup, and re-checks whenever the Server signals a change.

## Securing the Server

- **Create the administrator account right after the first start.** Until `create-admin` has run, anyone who can reach the port can create it (and the account has every scope). Start the Server on a network you trust, or firewall the port, until that's done.
- **Use HTTPS.** AccessKeys travel in request headers, so plain HTTP exposes them to anyone on the path. Either put the Server behind a TLS reverse proxy, or let Kestrel terminate TLS itself with a certificate:

  ```json
  {
    "OrbitMesh": { "listenUrls": ["https://+:8443"] },
    "Kestrel": { "Certificates": { "Default": { "Path": "server.pfx", "Password": "..." } } }
  }
  ```

  Responses over HTTPS carry `Strict-Transport-Security`. On the Edge, set `Edge:RequireHttps` to `true` to refuse starting with a plain-HTTP `OrbitMeshServerUri` to another machine (by default it only logs a warning, so an existing LAN install keeps working).
- **Behind a reverse proxy, list it in `OrbitMesh:TrustedProxies`** (its IP address). Otherwise every client appears to come from the proxy: the failed-login lockout (10 attempts per 5 minutes per IP, shared by REST and the SignalR hubs) becomes global, and a file server marked `localhostOnly` is open to every client of a same-host proxy.
- **Self-updates must be verifiable.** Set `UpdateOptions:PublicKeys` to the release signing key; each release is then checked against its signed manifest (signature, file hashes, and that its version is the announced one and newer than what's running). With no key configured, updates are refused unless you explicitly set `UpdateOptions:AllowUnsignedReleases` to `true`. The update token is only ever sent to the update server's own host.
- **The Console and Kiosk may not be embedded in another site's frame** (`frame-ancestors 'self'`).
