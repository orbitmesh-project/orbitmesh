# Access control

Every connection - Edge, package, dashboard, REST call, or a person in the Console - goes through the same short chain of checks. This page shows the chain first, then each link in detail.

## The layers at a glance

A request has to get past five checks, in this order. Each can refuse it; the first refusal ends it.

<div style="overflow-x:auto">
<svg viewBox="0 0 760 760" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="A request passes five checks in order: network and lockout, authentication, door, scope, then authorization rules. Each one can refuse it; if none does, it is allowed.">
  <defs>
    <marker id="lay-en-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
    <marker id="lay-en-stop" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-danger-1)" />
    </marker>
  </defs>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="20" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="44">1 · Network &amp; lockout</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="64">TLS and reverse proxy (yours), TrustedProxies, CORS closed</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="81">by default, per-IP lockout: 10 failures in 5 minutes</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="98">bans that address for 15 minutes.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-en-stop)" x1="500" y1="66" x2="528" y2="66" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="28" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="50">429 locked-out address</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="66">Cross-origin browser call</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="82">blocked by CORS</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-en-arrow)" x1="265" y1="112" x2="265" y2="130" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="127">passes</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="132" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="156">2 · Authentication - who are you?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="176">An AccessKey (Machine credential), or the session cookie of</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="193">someone signed in with a password or single sign-on.</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="210">A wrong key counts toward the lockout; no credential at all does not.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-en-stop)" x1="500" y1="178" x2="528" y2="178" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="140" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="162">403 Forbidden</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="178">or the hub connection is</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="194">closed</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-en-arrow)" x1="265" y1="224" x2="265" y2="242" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="239">passes</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="244" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="268">3 · Door - which entry point?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="288">Management API, Console hubs, Consumer, or Edge / Package.</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="305">A session only opens the Console doors; an Edge or package</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="322">key only opens the door it was issued for.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-en-stop)" x1="500" y1="290" x2="528" y2="290" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="252" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="274">403 wrong door</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="290">a session cannot pose as</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="306">an Edge or a package</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-en-arrow)" x1="265" y1="336" x2="265" y2="354" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="351">passes</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="356" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="380">4 · Scope - what kind of action?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="400">The identity must hold the scope the action needs:</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="417">credentials:manage, packages:control, edge:connect,</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="434">messages:execute...</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-en-stop)" x1="500" y1="402" x2="528" y2="402" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="364" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="386">403 "requires the</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="402">&lt;scope&gt; scope"</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-en-arrow)" x1="265" y1="448" x2="265" y2="466" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="463">passes</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="468" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="492">5 · Authorization rules - on which target?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="512">Allow / Deny rules on the credential for messages, message</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="529">groups and telemetry items (single sign-on users have none:</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="546">their scopes are the only gate).</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-en-stop)" x1="500" y1="514" x2="528" y2="514" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="476" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="498">403 on REST</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="514">denied or ignored on the hubs</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-en-arrow)" x1="265" y1="560" x2="265" y2="578" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="575">passes</text>
  <rect fill="var(--vp-c-green-soft)" stroke="var(--vp-c-green-1)" stroke-width="1.5" x="30" y="580" width="470" height="44" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="265" y="607" text-anchor="middle">Allowed</text>
  <rect fill="none" stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x="30" y="644" width="700" height="96" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="13" font-weight="600" x="46" y="668">Administrative actions add guard rails on top</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="688">Scope delegation (you can only hand out scopes you hold), the last credential cannot be removed,</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="704">a configuration change cannot revoke your own access, secrets are masked on read, and every</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="720">state-changing Management call is written to the Audit log with the caller's name.</text>
</svg>
</div>

| # | Layer | Question | Enforced by | Tune with |
| --- | --- | --- | --- | --- |
| 1 | Network & lockout | May this address even try? | Your TLS / reverse proxy, `LoginAttemptLimiter`, the CORS policy | `listenUrls`, `TrustedProxies`, `allowedOrigins` |
| 2 | Authentication | Who is it? | AccessKey check, or the session cookie | Credentials page, `Auth` options |
| 3 | Door | Is this the right entry point for this identity? | Access class by path / hub; the credential assigned to an Edge or package | An Edge's and a package's `credential` |
| 4 | Scope | May it do this kind of action? | `RequiresScope` on each Management endpoint, a check in each `ControlHub` method, `edge:connect` / `package:connect` / `messages:execute` on the other hubs | A credential's `scopes`, or `roleMappings` |
| 5 | Authorization rules | On this particular target? | `Authorizations` on the credential | Credentials → Authorizations |

Layers 4 and 5 are the same two-level model as AWS IAM: a scope authorizes an *action class*, a rule restricts the *target*.

## Who can use which door

| Door | Used by | Machine key | API token | Password session | SSO session |
| --- | --- | :---: | :---: | :---: | :---: |
| Management API (`/rest/management`) | The Console, scripts | Yes - any credential holding at least one scope, then the endpoint's own scope | Same | Yes | Yes |
| `ControlHub` | The Console's live view | Yes (same rule) | Same | Yes | Yes |
| `ConsumerHub`, `/rest/consumer` | Dashboards, the Console | Yes - any enabled credential, then scopes and rules | Same | Yes | Yes |
| `EdgeHub` | An Edge process | Only the credential assigned to that Edge, with `edge:connect` | **No** | No | No |
| `OrbitMeshHub`, `/rest/orbitmesh` | Packages | Only the credential assigned to that package (or its Edge), with `package:connect` | **No** | No | No |
| The `Developer` edge | Local development | A key holding `developer` | REST only | No | No |
| `/packages/{file}` (package zips) | An Edge fetching its packages | The Edge's own credential (`EdgeName` = that Edge, `PackageName` = `Edge`, with `edge:connect`), or any key holding `packages:read` | With `packages:read` | With `packages:read` | With `packages:read` |

A few doors are open **on purpose**, each bounded: `/rest/enroll` (an Edge asking to be approved), `/rest/management/setup/*` (first run only), `/rest/auth/*` (sign-in), static sites under a `FileServers` path, and `/metrics` (Prometheus). Anything you don't want public - `/metrics`, or a site that stores a key in its own config file - belongs behind `localhostOnly`, your reverse proxy or a firewall.

## Four kinds of identity

| | Machine credential | API token | Human credential | Single sign-on user |
| --- | --- | --- | --- | --- |
| For | Edges and packages (the Server hands their key out when it launches them) | Scripts, dashboards, kiosks calling the Console's APIs | A person with a local account | A person signed in through Authelia |
| Proves itself with | Its AccessKey, on every request or connection | Its token `omk_<id>_<secret>`, as the AccessKey | A password, once - then a session | The identity provider, once - then a session |
| Stored as | AES-256-GCM in `appsettings.json`; the key is in `keys/` | Only a SHA-256 hash - the token itself can't be recovered | A salted PBKDF2 hash | Nothing - its groups arrive with the sign-in |
| Lasts | Until reset, disabled or removed | Until its expiry (optional), or disabled, rotated or removed | Session: 30 min idle, 24 h at most | Same |
| Rights come from | `scopes` + `authorizations` of the credential | Same | Same, read live | `roleMappings` of its groups; no per-target rules |
| Appears in the audit log as | The credential name | The credential name | The credential name | `oidc:<login>` |

A Machine key is shown once, when created or reset. It can authenticate an Edge, a package or a script, but it can never be used to sign in to the Console.

### API tokens

Prefer an **API token** to a Machine key for anything that is not an Edge or a package. Create one on the Credentials page (kind *API token*): the Server generates it, shows it **once**, and keeps only its hash. Compared with a Machine key:

- It is recognisable: the fixed `omk_` prefix lets a secret scanner flag a token that leaks into a repository or a log. On GitHub, add the pattern `omk_[0-9a-f]{16}_[A-Za-z0-9_-]{43}` as a custom secret-scanning pattern.
- It is found by its public id (`omk_<id>_…`), so you can tell which token an alert is about, and checking it costs one hash instead of a decryption per credential.
- It can **expire**, can be **rotated** (new secret, old one dead at once, same name, scopes and rules) and **revoked** on its own like any credential.
- It can never hold `edge:connect` or `package:connect`, and it is refused at an Edge's or a package's own door.

It is sent exactly like any AccessKey (`AccessKey` header, or the query string where a header can't be set). Existing Machine keys keep working; nothing is migrated.

## Scopes




A credential holds a list of named **scopes** (PAT-style, like a GitHub token): `edges:read`, `packages:manage`, `credentials:manage`, `updates:manage`, and so on. See `OrbitMeshScope` for the full list. Each scope gates one slice of the Management API/`ControlHub` - or, for three scopes below, the base Edge/package/consumer protocol itself.

An Edge's own credential needs `edge:connect` just to open its connection to `EdgeHub`; a package's own credential needs `package:connect` to open its connection to `OrbitMeshHub`, and `messages:execute` to send a message from `SendMessage` (a Console/Consumer credential needs the same scope to invoke a message handler via `ConsumerHub`/the REST `SendMessage` endpoint). Without the matching scope, `Authorizations` below is never even consulted - a credential missing `edge:connect`/`package:connect` can't connect at all, regardless of what its `Authorizations` say. A package's own credential gets both scopes automatically (`EnsurePackageCredential`, re-applied on every deploy or Groups change); an Edge approved from the pending list gets `edge:connect` the same way. Every other scope is purely admin-facing.

## Authorization rules




`authorizations` is a finer-grained layer underneath scopes: per-target Allow/Deny rules for messages, groups and telemetry items. Same two-level model as AWS IAM - a scope authorizes the action class, a rule restricts the target.

Each package gets its own auto-provisioned Machine credential instead of sharing its Edge's. It defaults to `Deny` on all three, with just enough `Allow` rules to reach its own edge/package's telemetry, its own configured groups, and messages addressed to itself:

```json
{
  "name": "salon.DayInfo",
  "accessKey": "ENC$...",
  "kind": "Machine",
  "scopes": ["package:connect", "messages:execute"],
  "authorizations": {
    "messages": {
      "defaultAuthorization": "Deny",
      "rules": [
        { "authorization": "Allow", "scope": "Package", "args": "DayInfo" },
        { "authorization": "Allow", "scope": "Edge", "args": "salon" }
      ]
    },
    "groups": { "defaultAuthorization": "Deny", "rules": [] },
    "telemetryItems": {
      "defaultAuthorization": "Deny",
      "rules": [{ "authorization": "Allow", "edgeName": "salon", "packageName": "DayInfo", "name": "*" }]
    }
  }
}
```

Reaching another package's telemetry, or sending it a targeted message, needs an explicit `Allow` rule on that credential (Console → Credentials → Authorizations). Broadcasting to everyone (`All`/`Others` message scope) needs one too.

## Signing in to the Console




Machine callers (Edges, packages, dashboards such as the kiosk) present their `AccessKey` as ever. **People** don't: the Console signs in with a username and password and gets a *session* - an opaque token in an `HttpOnly`, `SameSite=Strict` cookie that scripts can't read, kept server-side as a hash. It expires after 30 minutes without use (`OrbitMesh:Auth:SessionIdleMinutes`) and after 24 hours at the most (`SessionMaxHours`), ends when you sign out, and is cut if the account is disabled, removed or has its password reset. The Console only works from the Server's own origin.

An account created by an earlier version keeps working; its stored hash is upgraded the first time it signs in.

## Single sign-on with Authelia (OpenID Connect)




Instead of (or besides) local accounts, the Console can sign people in through an OpenID Connect provider. This is written for [Authelia](https://www.authelia.com/); any provider that supports the authorization-code flow with PKCE and returns a groups claim works.

How the exchange runs:

<div style="overflow-x:auto">
<svg viewBox="0 0 760 560" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="Single sign-on sequence: the browser asks OrbitMesh to sign in, is redirected to Authelia, signs in there, comes back with a code, OrbitMesh exchanges the code for the user's identity and groups, maps the groups to scopes, and starts a session cookie.">
  <defs>
    <marker id="oidc-en-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
  </defs>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="30" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="110" y="35" text-anchor="middle">Browser</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="110" y="52" text-anchor="middle">the Console</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="110" y1="62" x2="110" y2="540" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="2" x="300" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="380" y="35" text-anchor="middle">OrbitMesh Server</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="52" text-anchor="middle">confidential client</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="380" y1="62" x2="380" y2="540" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="570" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="650" y="35" text-anchor="middle">Authelia</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="650" y="52" text-anchor="middle">OpenID Connect provider</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="650" y1="62" x2="650" y2="540" />
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="112" y1="100" x2="376" y2="100" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="92" text-anchor="middle">① GET /rest/auth/oidc/login</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="378" y1="152" x2="114" y2="152" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="144" text-anchor="middle">② 302 to the provider (+ PKCE)</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="112" y1="204" x2="646" y2="204" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="380" y="196" text-anchor="middle">③ the user signs in there (password, 2FA...)</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="648" y1="256" x2="114" y2="256" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="380" y="248" text-anchor="middle">④ 302 back with ?code=...</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="112" y1="308" x2="376" y2="308" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="300" text-anchor="middle">⑤ GET /rest/auth/oidc/callback?code=...</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="382" y1="360" x2="646" y2="360" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="515" y="352" text-anchor="middle">⑥ code + client secret + PKCE verifier</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" stroke-dasharray="5 4" marker-end="url(#oidc-en-arrow)" x1="648" y1="412" x2="384" y2="412" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="515" y="404" text-anchor="middle">⑦ ID token, then userinfo (groups)</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="205" y="442" width="350" height="56" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="12" font-weight="600" x="380" y="464" text-anchor="middle">⑧ groups → scopes (role mappings)</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="481" text-anchor="middle">no mapped group = refused, otherwise a session starts</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-en-arrow)" x1="378" y1="542" x2="114" y2="542" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="534" text-anchor="middle">⑨ session cookie + 302 to the Console</text>
</svg>
</div>

**1. Register OrbitMesh in Authelia** (`configuration.yml`; check the option names against the documentation of your Authelia version):

```yaml
identity_providers:
  oidc:
    clients:
      - client_id: orbitmesh
        client_name: OrbitMesh
        client_secret: '$pbkdf2-sha512$...'      # the hash of the secret you put in OrbitMesh below
        public: false
        authorization_policy: two_factor
        redirect_uris:
          - https://orbitmesh.example.com/rest/auth/oidc/callback
        scopes: [openid, profile, email, groups]
        response_types: [code]
        grant_types: [authorization_code]
        require_pkce: true
        pkce_challenge_method: S256
        token_endpoint_auth_method: client_secret_post
```

**2. Configure the Server.** In the Console, open **Administration → Single sign-on**: it has every setting below, shows the redirect URI to register at the provider, and keeps the client secret masked. Or edit `appsettings.json` (section `OrbitMesh`) by hand:

```json
"auth": {
  "oidc": {
    "enable": true,
    "displayName": "Authelia",
    "authority": "https://auth.example.com",
    "clientId": "orbitmesh",
    "clientSecret": "the-plain-secret",
    "roleMappings": [
      { "group": "orbitmesh-admins", "scopes": [ "*" ] },
      { "group": "orbitmesh-viewers", "scopes": [ "edges:read", "packages:read", "telemetry:read" ] }
    ]
  }
}
```

`displayName` is the name in the sign-in button ("Sign in with Authelia"; "SSO" if left out). `clientSecret` can also come from the `OrbitMesh__Auth__Oidc__ClientSecret` environment variable. Restart the Server after changing the provider settings; role mappings apply immediately.

**Who gets what.** A user is given the union of the scopes of every mapping whose `group` they belong to (`"*"` means every scope a person can hold - all but `edge:connect` and `package:connect`, which belong to Machine credentials); a user in none of them is refused. Mappings are read live, so editing one applies to sessions already open. These users appear as `oidc:<login>` in the audit log, have no credential entry, and are not subject to per-credential `authorizations` rules (scopes are the gate).

**Things to know.**

- Behind a reverse proxy, keep the original `Host` header and list the proxy in `OrbitMesh:TrustedProxies`: the redirect URI sent to Authelia is built from them, and must match `redirect_uris` exactly.
- Keep a local admin account as a way in if Authelia is down; `auth.localLoginEnabled: false` removes the password form.
- With single sign-on enabled there is no first-run "create the admin" step - the groups decide.
- Don't put the Console behind Authelia's forward-auth at the proxy as well: Edges, packages and dashboards must reach `/signalr` and `/rest` with their AccessKey.

## Guard rails on administration

- **Scope delegation.** `credentials:manage` and `configuration:write` are not root: you can only create or change a credential (or a role mapping) whose scopes you hold yourself, and you can't edit or remove a credential carrying scopes you don't hold.
- **The last credential can't be removed**, so the Server can't be put back in first-run state through the API.
- **No self-lockout through the configuration editor**: a change that would remove your own `configuration:write` is refused (for single sign-on users too, judged on their groups).
- **Secrets are masked on read** (`********`): credential keys and hashes, the update token, NuGet feed keys, secret variables, Password settings, the single sign-on client secret. Saving the placeholder back keeps the stored value.
- **Plaintext keys and reserved paths are refused** by the configuration editor (`/rest`, `/signalr`, `/metrics` and `/packages` can't be hijacked by a file server).
- **Everything is audited**: each state-changing Management call, every sign-in, refusal and sign-out, goes to the `Audit` log category with the caller's resolved name and address.

## Deployment checklist

- Serve over **HTTPS** (a TLS reverse proxy or Kestrel itself). Without it AccessKeys and passwords cross the network in clear and the session cookie can't be marked `Secure`.
- Behind a proxy, list it in **`TrustedProxies`** and keep the original `Host` header: the lockout, `localhostOnly`, and the single sign-on redirect address all depend on it.
- Create the **first admin right away** (or configure single sign-on, which closes that step).
- **Back up `appsettings.json` and the `keys/` folder together.** Without `keys/credential-encryption.key` the Machine keys can't be read back; `keys/` also holds the sessions and the single sign-on state, so keep its permissions tight.
- Use **API tokens**, with an expiry, for scripts, dashboards and kiosks - keep Machine keys for Edges and packages.
- Give each Machine credential or token **only the scopes it needs**, and tighten its `authorizations` (a dashboard that only displays telemetry needs `telemetry:read`, not more).
- **Restrict `/metrics`** and keep secrets out of static sites.
- Set **`UpdateOptions:PublicKeys`** so self-updates are verified.

The full list of what is and isn't protected, with the known limitations, is in [`SECURITY.md`](https://github.com/orbitmesh-project/orbitmesh/blob/main/SECURITY.md); the HTTPS and proxy setup is in [Running Server & Edge](/guide/installation/running#securing-the-server).
