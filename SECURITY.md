# Security

This document describes OrbitMesh's authentication and authorization model: what a credential is,
what scopes it can hold, and what's already in place versus what's a known limitation.

## Credentials

A credential is either:

- **Human** — a console login (username + password). The server never stores the password itself,
  only a PBKDF2-SHA256 hash (20,000 iterations, random salt per credential). It cannot be reversed or
  displayed again after creation.
- **Machine** — a bearer token for an Edge or a package (the PAT equivalent on most other platforms).
  The server stores it encrypted (AES-256-GCM) rather than hashed, because it has to hand the real value
  back out to an Edge/package that needs to authenticate with it. The encryption key lives in its own
  file (`keys/credential-encryption.key`, generated on first run, outside `appsettings.json`), so a leak
  of the config file alone doesn't hand over every Machine credential - both files have to be
  compromised together.

Either kind's key/password is shown **once**, at creation or reset, in the Console UI. It is never
displayed again afterwards - if it's lost, reset it and get a new one.

## Scopes

A credential's permissions are a list of named scopes, not a single "admin or not" flag. This is the
same two-level model as AWS IAM: a scope authorizes an action *class*; the existing per-credential
Message/Group/TelemetryItem authorization rules (unchanged by this system) can still restrict *which*
specific targets that action applies to, once the scope already allows it.

| Scope | Grants |
|---|---|
| `edges:read` | View edges and their live connection state. |
| `edges:write` | Add, remove, or restart edges. |
| `packages:read` | View package instances, live status, logs and settings. |
| `packages:control` | Start, stop, restart or reload a running package. |
| `packages:manage` | Add, remove or reconfigure a package instance (settings, groups, recovery). |
| `packages:deploy` | Upload, install or remove packages in the Package Repository. |
| `telemetry:read` | View telemetry item values. |
| `telemetry:purge` | Delete stored telemetry item history. |
| `credentials:read` | View the list of credentials (never their keys). |
| `credentials:manage` | Create, edit, remove or reset credentials. The most sensitive scope, but delegation is limited: a holder can only grant scopes it holds itself, and can't edit or remove a credential carrying scopes it doesn't hold. |
| `configuration:read` | View the raw server configuration (secrets are masked). |
| `configuration:write` | Edit the raw server configuration or global recovery options. Subject to the same delegation rule for credentials; plaintext AccessKeys and reserved file-server paths are refused. |
| `updates:manage` | Check for and apply Server/Console/package updates. |
| `developer` | Direct developer workflows (messages, telemetry) without deploying a package to a real Edge. |

Scopes are checked at two levels:

1. **Connection-level** (`AccessKeyAuthenticationMiddleware` / `ControlHub.OnConnectedAsync`) - a
   coarse gate: does this credential hold *any* scope at all, for this connection family
   (Controller/live console, or Management/REST API)? This only rules out a credential with no
   business connecting to that family in the first place.
2. **Per-action** (`RequiresScopeAttribute` on every Management REST endpoint; an explicit
   `RequireScope(...)` call at the top of every mutating `ControlHub` method) - the specific scope that
   action actually needs. This is what makes a genuinely read-only credential possible: it can hold a
   live `ControlHub` connection and receive edge/package/telemetry updates, but every control or write
   RPC on that same connection still rejects it.

## First-run setup

A fresh install has zero credentials. `GET /rest/management/setup/status` and
`POST /rest/management/setup/create-admin` are the only unauthenticated endpoints - the Console uses
them to walk a first-run visitor through creating the sole admin account (all scopes) before anything
else works. `create-admin` re-checks "zero credentials" inside the same write lock it uses to persist
the new one, so two concurrent first-run requests can't both succeed.

**Known limitation:** there is no bootstrap secret. Until the first admin exists, whoever reaches the
port first creates it (with every scope, including `packages:deploy`, i.e. code execution on every
Edge). Create the account immediately after the first start, on a trusted network or with the port
firewalled. The last remaining credential can't be deleted, so the server can't be put back into this
state through the API.

## Edge enrollment

An Edge with no valid credential enrolls through the unauthenticated `rest/enroll` endpoints
(`POST` to register, `GET .../status` to poll). Because it is unauthenticated it is bounded and
validated: the InstanceId must be a GUID, text fields are length-capped, at most 100 entries (5 per
source IP) are held, a pending entry expires 15 minutes after its last poll, and each Edge sends a
persisted secret (only its SHA-256 is stored) with every call - so knowing an InstanceId, which any
`edges:read` user can see, is not enough to collect the approved key or hijack another Edge's entry.
The approved key is held in memory only until the Edge reconnects with it (or 10 minutes). The
pending list shows only self-declared data: approve an Edge you recognise.

## What's already in place

- **PBKDF2 password hashing** (Human) and **AES-256-GCM encryption** (Machine) - see above.
- **Rate limiting** (`LoginAttemptLimiter`) - 10 failed attempts from the same IP within 5 minutes
  triggers a 15-minute lockout for that IP, independent of which credential was being tried. It covers
  REST and all four SignalR hubs, and a successful request does not reset the count (so an attacker
  holding one valid key can't interleave successes to avoid the lockout). Behind a reverse proxy, list
  it in `OrbitMesh:TrustedProxies` so the real client address is used.
- **Constant-time key comparison** for Machine credentials.
- **Secrets masked on read** - `configuration:read` and `packages:read` never return credential
  keys/hashes, the update token, NuGet feed API keys, secret variables or Password-typed package
  settings; the Console's editors round-trip a placeholder that keeps the stored value.
- **Signed, version-bound updates** - a release must carry a manifest signed by a key in
  `UpdateOptions:PublicKeys`, whose file hashes match, and whose signed version is the announced one and
  newer than the running one (no downgrade/replay). With no key configured, updates are refused unless
  `UpdateOptions:AllowUnsignedReleases` is set. The update token is only sent to the update server's
  own host.
- **Response hardening** - `X-Content-Type-Options`, `X-Frame-Options`/`frame-ancestors`,
  `Referrer-Policy: no-referrer` (AccessKeys can appear in query strings), `Cache-Control: no-store` on
  `/rest`, and HSTS over HTTPS. The Console cookie is `SameSite=Strict` (and `Secure` over HTTPS).
- **Audit logging** - every mutating (`POST`/`PUT`/`DELETE`/`PATCH`) call to `/rest/management` is
  logged with the caller's resolved credential name, to a separate long-retention log category (`Audit`
  in `NLog.config`).
- **Self-lockout guard** - `POST /rest/management/configuration` (the raw-JSON config editor) refuses to
  save a change that would revoke the calling credential's own `configuration:write` scope.
- **Console login is username/password only** - Machine (bearer-token) credentials cannot be used to log
  into the Console UI, only to authenticate an Edge or package.

## Known limitations

- **No scope self-lockout guard outside the raw config editor.** Editing a credential's own scopes
  through the normal Credentials page (`POST /rest/management/credentials`) does not check whether the
  change would revoke the caller's own access, unlike the raw-JSON config editor above. Be careful
  removing `credentials:manage` from the account you're currently using.
- **Plain HTTP by default.** The Server listens on `http://+:8088` out of the box. Use a TLS reverse
  proxy or Kestrel's own certificate (see the "Securing the Server" section of the docs); the Edge only
  warns about a plain-HTTP server URL unless `Edge:RequireHttps` is set.
- **The Console's session credential is a replayable value.** The browser derives a key from the username
  and password and sends it on every request (stored in a script-readable cookie). A server-issued,
  expiring, revocable session token would be better; it isn't implemented yet.
- **Authorizations default to Allow** for Edge and Human credentials created through the Console or by
  approving an Edge (package credentials auto-provisioned by the Server default to Deny). Scopes are the
  real gate; tighten the rule sets per credential where it matters.
- **No expiry on Machine credentials.** A generated key is valid until manually reset or the credential
  is disabled/removed - there's no time-limited token support yet.
- **Authorizations are still a single flat list of rules** (Messages/Groups/TelemetryItems), not scoped
  per-credential-kind or bulk-editable; this predates the scope system and hasn't changed.
