# Hosting other sites

`FileServers` isn't Console-specific. It's a list - the Server can serve more than one static site from the same process/port, each with its own path and physical folder:

```json
{
  "fileServers": [
    { "enable": true, "path": "/console", "physicalPath": "console", "isSpa": true, "updateProjectSlug": "orbitmesh-console" },
    { "enable": true, "path": "/dashboard", "physicalPath": "dashboard", "isSpa": true, "updateProjectSlug": "my-dashboard", "preserveFiles": ["config.json"] }
  ]
}
```

`preserveFiles` keeps a file untouched across self-updates - useful for something like `config.json` holding this specific device's Server URL and AccessKey, which no release build could know in advance.

Package and release it the same way as the Console: `release-static-site.ps1 -SourceDir ... -Slug my-dashboard -Version 1.0.0 -ExcludeFiles config.json`. The Server's `StaticSiteUpdater` then tracks and updates it like any other site.

## Connecting from that site

A dashboard doesn't register as a package - it connects to `ConsumerHub` as a read-only client. See [Access control](/guide/architecture/access-control) for how the `AccessKey` and its authorization rules work; a Consumer only needs an enabled credential, no scopes.

From `ConsumerHub` it can:

- Subscribe to specific telemetry items and get pushed updates.
- Subscribe to message groups, and send messages itself.

It never sees package settings or control actions - those are for packages and the Console, over `OrbitMeshHub`/`ControlHub`.

## JS client

The Console already ships a small, generic SignalR wrapper - `Scripts/signalr-client.js` - with a factory built for exactly this:

```js
import { createOrbitMeshConsumer } from "./signalr-client.js";

const consumer = createOrbitMeshConsumer("https://orbitmesh.example.com", accessKey, "dashboard");
consumer.client.registerTelemetryItemLink("salon", "DayInfo", "Sunrise", "String", (item) => {
  console.log(item.Value);
});
await consumer.connection.start();
```

It has no dependency on the Console itself - just `@microsoft/signalr` loaded on the page. Nothing stops an external dashboard site from importing this file today.

::: warning The dashboard's key is only as private as the site
A dashboard authenticates with a **Machine credential**'s `accessKey` (people's sessions are for the Console only). The page's own JavaScript has to know it, so anyone who can open the page - or fetch a `config.json` stored next to it - has the key. Give it **only the scopes it needs** (`telemetry:read` for a display-only dashboard, plus `messages:execute` if it sends commands) and tighten its `authorizations`; restrict the site with `localhostOnly` or your proxy if it shouldn't be public. See [Access control](/guide/architecture/access-control).
:::

::: tip TODO
Extract `signalr-client.js` out of `orbitmesh-console/Scripts/` into a shared location (a small npm package, or a folder under a common repo) so an external dashboard site can depend on it without copying the file.
:::
