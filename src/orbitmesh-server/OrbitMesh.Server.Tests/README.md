# OrbitMesh.Server.Tests

Integration and unit tests for the Server's authentication and authorization. They start the real application
in-process (ASP.NET `TestServer`) and drive it over HTTP and SignalR, so they exercise the same middleware,
controllers and hubs as a deployed Server.

```bash
dotnet test src/orbitmesh-server/OrbitMesh.Server.Tests
```

## What they pin down

The security documentation (`SECURITY.md`, the "Access control" guide) describes five layers and a table of
which identity opens which door. The tests are that table, executable:

| File | Covers |
| --- | --- |
| `AccessMatrixTests` | Door x identity: Management, Controller, Consumer, an Edge's own door, the developer edge; public endpoints; the query-string wire form |
| `ScopeEnforcementTests` | Each Management endpoint needs exactly its own scope |
| `SessionTests` | Cookie attributes, sign-in refusals, sign-out, idle expiry, CSRF and cross-origin rules, session never usable as an AccessKey |
| `PersonDoorsTests` | Password and single sign-on sessions through every door; role mappings, live |
| `AccountLifecycleTests` | Disabling or resetting an account ends its sessions; secrets stored hashed/encrypted; legacy accounts upgraded |
| `LockoutTests` | The IP lockout: what counts, what does not, shared by REST, sign-in and hubs |
| `ConfigurationGuardTests` | The raw configuration editor: masked secrets, scope delegation, self-lockout, reserved paths |
| `VariantServerTests` | First-run setup, local sign-in off, single sign-on off or removed |
| `HubTests` | SignalR hubs accept and refuse the same callers as the REST doors |
| `AuthorizationRulesTests` | Per-credential rules for messages, groups and telemetry items |
| `ApiTokenTests` | API tokens: format, hash-only storage, doors, expiry, revocation, rotation, delegation, configuration-editor guards |
| `MetricsTests` | The public `/metrics` endpoint keeps working once there is traffic |
| `SecurityPartsTests` | Password hashing, role mapper, session store, key cipher |

## How the harness stays safe

`Harness/ServerFixture` gives every test class its own Server on a **temporary content root** (its own
`appsettings.json` and `keys/`). The Server project folder holds the developer's real, git-ignored
`appsettings.json` and `keys/`; `Harness/DevFilesGuard` fingerprints them before the first server starts and
fails the run if they have changed afterwards. Each client also gets its own fake remote address, so one
test's failed attempts can never lock another out.

A test that changes an account uses one reserved for it (see `Harness/Accounts`); do the same when adding one.

## Known limits

- The OpenID Connect redirect and token exchange are not exercised (they need an identity provider);
  single-sign-on sessions are created directly, which covers everything after the callback.
- Time-based rules that need minutes to elapse (lockout expiry, absolute session lifetime) are not covered.
