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

Approve it in the Console (editable name, defaults to what it reported). The Server creates a matching credential and pushes the AccessKey straight back down the Edge's own connection - it saves it to `appsettings.json` and reconnects on its own. No copy-pasting.

If the Edge isn't still connected when you approve it, the Console shows the generated key once, to paste in by hand.

The Edge downloads any assigned package from the Server's package repository on startup, and re-checks whenever the Server signals a change.
