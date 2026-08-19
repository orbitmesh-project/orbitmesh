# Architecture overview

<div style="overflow-x:auto">
<svg viewBox="0 0 760 460" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="Console, Edge and Package each connect directly to the Server over their own SignalR hub. Edge additionally spawns and supervises the Package as a local OS process, separately from that network connection.">
  <defs>
    <marker id="arch-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
  </defs>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="52" y="336" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="46" y="330" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="572" y="336" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="566" y="330" width="150" height="64" rx="8" />

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="2" x="298" y="198" width="164" height="66" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="380" y="226" text-anchor="middle">Server</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="244" text-anchor="middle">state, package repo, config</text>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="30" y="24" width="170" height="64" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="115" y="52" text-anchor="middle">Console</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="115" y="70" text-anchor="middle">Vue admin UI</text>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="560" y="24" width="170" height="64" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="645" y="52" text-anchor="middle">External consumer</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="645" y="70" text-anchor="middle">read-only client</text>

  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="121" y="358" text-anchor="middle">Edge</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="121" y="376" text-anchor="middle">runs on a device</text>

  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="641" y="358" text-anchor="middle">Package</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="641" y="376" text-anchor="middle">your code, × N</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow)" marker-start="url(#arch-arrow)" x1="176" y1="82" x2="322" y2="205" />
  <rect fill="var(--vp-c-bg)" x="178" y="128" width="76" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="216" y="140" text-anchor="middle">ControlHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow)" marker-start="url(#arch-arrow)" x1="584" y1="82" x2="438" y2="205" />
  <rect fill="var(--vp-c-bg)" x="500" y="128" width="90" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="545" y="140" text-anchor="middle">ConsumerHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow)" marker-start="url(#arch-arrow)" x1="322" y1="257" x2="185" y2="335" />
  <rect fill="var(--vp-c-bg)" x="185" y="278" width="70" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="220" y="290" text-anchor="middle">EdgeHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow)" marker-start="url(#arch-arrow)" x1="438" y1="257" x2="588" y2="335" />
  <rect fill="var(--vp-c-bg)" x="470" y="278" width="104" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="522" y="290" text-anchor="middle">OrbitMeshHub</text>

  <path stroke="var(--vp-c-text-2)" stroke-width="1.5" fill="none" stroke-dasharray="5 4" marker-end="url(#arch-arrow)" d="M 196 368 H 562" />
  <rect fill="var(--vp-c-bg)" x="300" y="380" width="164" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="382" y="392" text-anchor="middle">spawns &amp; supervises (local process)</text>
</svg>
</div>

**A package talks directly to the Server, not through its Edge.** `PackageHost` opens its own SignalR connection to `OrbitMeshHub`, using a Server URI and AccessKey the Edge hands it as launch arguments. The Edge is not on the data path for messages or telemetry. Edge↔Package is a local relationship: start, stop, monitor the OS process. Edge↔Server (`EdgeHub`) is a separate connection for registration and remote control.

- **Server** holds the only persistent state (`appsettings.json`): known Edges, credentials, package assignments, and the package repository.
- **Edge** is a thin host. Connects to the Server, downloads its assigned packages, launches each as its own OS process. It doesn't proxy their traffic.
- **Package** is your code - a console app using the SDK (see [Building a package](/guide/sdk/)) - spawned by an Edge but connected to the Server directly.
- **Console** is a static SPA the Server hosts, talking to the Server over the same hubs/API any other client could use.

## Four SignalR hubs

| Hub | Who connects | Purpose |
| --- | --- | --- |
| `EdgeHub` | Edge processes | Registration, package-list push, remote control |
| `OrbitMeshHub` | Packages themselves | Messages, telemetry items, settings, package descriptors |
| `ControlHub` | The Console | Live edge/package state, logs, remote control actions |
| `ConsumerHub` | External read-only clients | Subscribe to messages/telemetry without registering as a package |

Each connection type carries only what it needs. The Console never has to speak the package protocol just to watch what's happening.
