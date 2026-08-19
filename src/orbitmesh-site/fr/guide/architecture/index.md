# Vue d'ensemble de l'architecture

<div style="overflow-x:auto">
<svg viewBox="0 0 760 460" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="La Console, l'Edge et le Package se connectent chacun directement au Server via leur propre hub SignalR. L'Edge lance et supervise en plus le Package comme un process OS local, séparément de cette connexion réseau.">
  <defs>
    <marker id="arch-arrow-fr" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
  </defs>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="52" y="336" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="46" y="330" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="572" y="336" width="150" height="64" rx="8" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="566" y="330" width="150" height="64" rx="8" />

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="2" x="298" y="198" width="164" height="66" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="380" y="226" text-anchor="middle">Server</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="244" text-anchor="middle">état, dépôt de packages, config</text>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="30" y="24" width="170" height="64" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="115" y="52" text-anchor="middle">Console</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="115" y="70" text-anchor="middle">UI admin Vue</text>

  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="560" y="24" width="170" height="64" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="645" y="52" text-anchor="middle">Consommateur externe</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="645" y="70" text-anchor="middle">client lecture seule</text>

  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="121" y="358" text-anchor="middle">Edge</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="121" y="376" text-anchor="middle">tourne sur un appareil</text>

  <text fill="var(--vp-c-text-1)" font-size="15" font-weight="600" x="641" y="358" text-anchor="middle">Package</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="641" y="376" text-anchor="middle">votre code, × N</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow-fr)" marker-start="url(#arch-arrow-fr)" x1="176" y1="82" x2="322" y2="205" />
  <rect fill="var(--vp-c-bg)" x="178" y="128" width="76" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="216" y="140" text-anchor="middle">ControlHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow-fr)" marker-start="url(#arch-arrow-fr)" x1="584" y1="82" x2="438" y2="205" />
  <rect fill="var(--vp-c-bg)" x="500" y="128" width="90" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="545" y="140" text-anchor="middle">ConsumerHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow-fr)" marker-start="url(#arch-arrow-fr)" x1="322" y1="257" x2="185" y2="335" />
  <rect fill="var(--vp-c-bg)" x="185" y="278" width="70" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="220" y="290" text-anchor="middle">EdgeHub</text>

  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#arch-arrow-fr)" marker-start="url(#arch-arrow-fr)" x1="438" y1="257" x2="588" y2="335" />
  <rect fill="var(--vp-c-bg)" x="470" y="278" width="104" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="522" y="290" text-anchor="middle">OrbitMeshHub</text>

  <path stroke="var(--vp-c-text-2)" stroke-width="1.5" fill="none" stroke-dasharray="5 4" marker-end="url(#arch-arrow-fr)" d="M 196 368 H 562" />
  <rect fill="var(--vp-c-bg)" x="300" y="380" width="164" height="16" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="382" y="392" text-anchor="middle">lance et supervise (process local)</text>
</svg>
</div>

**Un package parle directement au Server, pas via son Edge.** `PackageHost` ouvre sa propre connexion SignalR vers `OrbitMeshHub`, avec une URI de Server et un AccessKey que l'Edge lui transmet comme arguments de lancement. L'Edge n'est jamais sur le chemin des messages ou de la télémétrie. Edge↔Package est une relation locale : démarrer, arrêter, surveiller le process OS. Edge↔Server (`EdgeHub`) est une connexion séparée pour l'enregistrement et le contrôle à distance.

- **Server** détient le seul état persistant (`appsettings.json`) : Edges connus, identifiants, assignations de packages, et le dépôt de packages.
- **Edge** est un hôte léger. Se connecte au Server, télécharge ses packages assignés, lance chacun comme son propre process OS. Il ne relaye pas leur trafic.
- **Package** est votre code - une app console utilisant le SDK (voir [Créer un package](/fr/guide/sdk/)) - lancé par un Edge mais connecté directement au Server.
- **Console** est une SPA statique hébergée par le Server, qui parle au Server via les mêmes hubs/API que n'importe quel autre client.

## Quatre hubs SignalR

| Hub | Qui se connecte | Rôle |
| --- | --- | --- |
| `EdgeHub` | Les process Edge | Enregistrement, push de la liste de packages, contrôle à distance |
| `OrbitMeshHub` | Les packages eux-mêmes | Messages, telemetry items, settings, descripteurs de package |
| `ControlHub` | La Console | État live des edges/packages, logs, actions de contrôle à distance |
| `ConsumerHub` | Consommateurs externes en lecture seule | S'abonner aux messages/telemetry sans s'enregistrer comme un package |

Chaque type de connexion ne porte que ce dont il a besoin. La Console n'a jamais besoin de parler le protocole des packages juste pour observer ce qui se passe.
