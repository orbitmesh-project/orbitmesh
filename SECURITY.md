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
| `credentials:manage` | Create, edit, remove or reset credentials. The most sensitive scope - it can grant every other scope to a new or existing credential. |
| `configuration:read` | View the raw server configuration. |
| `configuration:write` | Edit the raw server configuration or global recovery options. |
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

## What's already in place

- **PBKDF2 password hashing** (Human) and **AES-256-GCM encryption** (Machine) - see above.
- **Rate limiting** (`LoginAttemptLimiter`) - 10 failed attempts from the same IP within 5 minutes
  triggers a 15-minute lockout for that IP, independent of which credential was being tried.
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
- **No expiry on Machine credentials.** A generated key is valid until manually reset or the credential
  is disabled/removed - there's no time-limited token support yet.
- **Authorizations are still a single flat list of rules** (Messages/Groups/TelemetryItems), not scoped
  per-credential-kind or bulk-editable; this predates the scope system and hasn't changed.
