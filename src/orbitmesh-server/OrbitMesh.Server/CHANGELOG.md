# Changelog - OrbitMesh.Server

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match `<Version>` in
`OrbitMesh.Server.csproj`, which is what's reported to the update server (see
`Services/ServerSelfUpdater.cs` and `Services/UpdateCheckService.cs`).

## [1.4.0]

### Added

- **API tokens** (`Kind: Token`) for scripts, dashboards and kiosks that call the Management, Controller or
  Consumer API - not for Edges or packages. The Server generates the token as `omk_<id>_<secret>` (a public
  16-hex id and 256 bits of secret), returns it **once** and keeps only its SHA-256 (`TKN$...`). It is found by
  its id (one hash to check, instead of a decryption per credential), compared in constant time, can **expire**
  (`expiresUtc`), be **rotated** and be disabled or removed like any credential. The fixed `omk_` prefix lets a
  secret scanner (GitHub, gitleaks) recognise a leaked token. New endpoints: `POST rest/management/credentials/tokens`
  and `POST rest/management/credentials/{name}/rotate` (both `credentials:manage`, subject to the usual rule that
  you can only hand out scopes you hold). Existing Machine keys are untouched and nothing is migrated.
- A token can't hold `edge:connect` or `package:connect`, is refused at an Edge's or a package's own door (even if
  assigned to one by hand-editing `appsettings.json`), can't be assigned to an Edge or a package through the
  Management API or the configuration editor, and can't be created or have its secret chosen through the
  ordinary credential save or the configuration editor.

## [1.3.3]

### Fixed

- **`/metrics` answered 500 as soon as the Server had served some traffic**, and kept doing so until restart.
  The Prometheus exporter (1.17.0-beta.1) had been built for an older OpenTelemetry core than the one installed
  (1.20.1) and threw a `TypeLoadException` the first time it had to export the request-duration histogram, which
  only exists once requests have been served. Updated to 1.20.1-beta.1.
- A message or telemetry request that a credential's own authorization rules refuse answered 500 (an exception
  from `Forbid()`, there being no authentication scheme to forbid with) instead of 403. The refusal itself held;
  the status code and the log noise were wrong.

### Changed

- The administrator created by first-run setup (`setup/create-admin`) holds every scope a person can hold - the
  same 17 as an Administrator account and as a single-sign-on `"*"` mapping - instead of also holding
  `edge:connect` and `package:connect`, which only matter to Machine credentials. Existing accounts are unchanged.

### Added

- `OrbitMesh.Server.Tests`, an automated suite for the authentication and authorization (doors by identity,
  sessions, single sign-on, the configuration editor's guards, SignalR hubs), with a GitHub workflow that runs it.

## [1.3.2]

### Fixed

- **A role mapping's `"*"` is every scope a person can hold**: all of them except `edge:connect` and
  `package:connect`, which only gate the Edge/package hub connections that a person's session can never open.
  This is what an Administrator account holds, so such an account can now map a group to `"*"` - it was refused,
  since an account may only hand out scopes it holds itself and `"*"` included those two. The two scopes are
  also ignored if listed explicitly in a mapping.

## [1.3.1]

Single sign-on can be set up from the Console (Administration → Single sign-on, Console 1.3.1).

### Changed

- `Auth:Oidc:DisplayName` (the name in the Console's "Sign in with ..." button) defaults to `SSO` instead of
  "Single sign-on". An install that never set it now shows "Sign in with SSO".
- The configuration endpoint refuses a change that turns `Auth:LocalLoginEnabled` off while single sign-on
  isn't enabled and complete (authority, client ID, client secret), which would leave no way to sign in.

## [1.3.0]

Console sign-in reworked: server-side sessions instead of a replayable key, and single sign-on through an
OpenID Connect provider such as Authelia.

### Added

- **Sessions.** `POST rest/auth/login` (name + password) starts a session: an opaque token in an
  `HttpOnly`, `SameSite=Strict` cookie, of which only the SHA-256 is kept (`keys/sessions.json`, so a restart
  doesn't sign everybody out). It expires after `Auth:SessionIdleMinutes` (30) of inactivity and
  `Auth:SessionMaxHours` (24) at the most, and ends on `POST rest/auth/logout`, when the account is disabled
  or removed, or when its password is reset (the session doing the reset is spared). `GET rest/auth/me`
  returns who the cookie belongs to and what it may do; `GET rest/auth/config` tells the sign-in page which
  methods are on. The session works for the Console's Management REST, `ControlHub` and `ConsumerHub`
  connections only, never to pose as an Edge or a package.
- **Single sign-on (OpenID Connect)** under `OrbitMesh:Auth:Oidc`: authorization-code flow with PKCE, the
  Server being a confidential client. The provider's groups are mapped to scopes by `RoleMappings` (read
  live); a user in no mapped group is refused. Signed-in users are named `oidc:<login>` (a prefix local
  credentials may no longer use). Tested against a stand-in provider; Authelia's own configuration is in the
  Access control guide. Needs a restart to change the provider settings. `Auth:LocalLoginEnabled` can turn the
  password form off.
- With single sign-on configured, first-run `setup/create-admin` is closed: the groups decide who administers.

### Changed

- **Human credentials hash the password itself** (PBKDF2-SHA256, 600,000 iterations, only ever run at
  sign-in) instead of a client-computed SHA-1 that doubled as the bearer key. An account created by an
  earlier version still signs in, and its hash is upgraded at that first sign-in - after which the old derived
  key is no longer accepted as an AccessKey. A script that used a Human account's key must use a Machine
  credential. `setup/create-admin` takes `Password` (the old `AccessKey` form is still accepted).
- **A verified AccessKey is remembered** (by hash, in memory) until the configuration reloads, and the
  credential it belongs to is resolved once per request, instead of an AES decrypt / PBKDF2 per credential on
  every REST call and every hub method.
- A request that presents no AccessKey at all (e.g. a Console whose session just expired, retrying) no longer
  counts toward the per-IP failed-attempt lockout; only an actual wrong key does.
- `GET configuration` masks `auth.oidc.clientSecret`; changing or removing an OIDC role mapping follows the
  same delegation rule as credentials, and the configuration editor refuses a change that would remove the
  caller's own `configuration:write`, for single-sign-on callers too.

### Compatibility

- Update the Console together with the Server (Console 1.3.0): an older Console signs in with the old derived
  key, which keeps working only until the account's first sign-in with a new Console.
- Edges, packages and dashboards (kiosk, ...) are unaffected: they present their AccessKey as before.

## [1.2.22]

### Fixed

- An Edge configured by hand (no `InstanceId` on file - only approval from the pending list records one)
  that failed to connect when its process started - typically because the Server was still starting or
  restarting for an update - showed up in **Pending edges** as a brand-new Edge next to its own,
  disconnected, entry, and was never told to retry its real connection, so it stayed disconnected until
  restarted. `POST rest/enroll` now also answers `rejected` (recording nothing) when the declared Edge name
  is already configured, which makes the Edge retry its normal connection. Approval already refused an
  existing name, so no legitimate enrollment is lost; a different machine that reuses an existing name just
  needs another `Edge:EdgeName`.
- A stuck pending entry like that expires on its own (15 minutes after the Edge stops polling) or can be
  dismissed from the Console.

## [1.2.21]

Security review follow-up, part 2 (the design decisions).

### Security

- **Scope delegation.** `credentials:manage` and `configuration:write` no longer amount to root: a caller
  can only create or change a credential whose scopes it holds itself, can't edit or remove a credential
  carrying scopes it doesn't hold (so a lesser admin can't take over, or delete, a more privileged one),
  and can't approve an Edge without `edge:connect`. The raw configuration editor applies the same rule,
  refuses plaintext AccessKeys, and refuses FileServers paths that are empty/`/`, don't start with `/`
  (which made every request throw, locking out the admin who saved it), or shadow `/rest`, `/signalr`,
  `/metrics`, `/openapi` or `/packages` - file servers are served without authentication.
- The last remaining credential can no longer be removed (it would reopen first-run setup).
- **Secrets masked on read.** `configuration:read` returns `********` for credential AccessKeys
  (PBKDF2 hashes / encrypted blobs), the update token, NuGet feed API keys, secret variables and
  Password-typed package settings; so do `packages:read`'s package and settings endpoints. Saving the
  placeholder back keeps the stored value (a new item can't be saved with a placeholder).
- **Failed-attempt lockout now covers the SignalR hubs** (it only covered REST): `EdgeHub`, `OrbitMeshHub`,
  `ControlHub` and `ConsumerHub` count failed connects against the same per-IP limiter and refuse a locked-out
  IP. A successful request no longer resets an IP's failure count.
- Machine AccessKeys are compared in constant time, and `FindCredential` only resolves enabled credentials.
- **Self-updates are verified.** A release is refused when `UpdateOptions.PublicKeys` is empty (unless
  `AllowUnsignedReleases` is set), when its signed manifest version isn't the announced one, or when it
  isn't newer than the running version; the update token is only sent to the update server's own host.
  `ServerSelfUpdater`, `StaticSiteUpdater` and `ManagementController` pass the versions through.
- **Response hardening** on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options:
  SAMEORIGIN` + CSP `frame-ancestors 'self'` (and `base-uri`/`object-src`/`form-action`),
  `Referrer-Policy: no-referrer`, `Cache-Control: no-store` on `/rest`, and
  `Strict-Transport-Security` over HTTPS. The Console and Kiosk can no longer be framed by another site.

### Added

- `OrbitMesh:TrustedProxies` - IP addresses of reverse proxies whose `X-Forwarded-For`/`-Proto` are
  trusted (none by default). Without it, behind a proxy every client shares the proxy's address.
- HTTPS: `listenUrls` may contain `https://` entries given a certificate in the standard Kestrel section.
- Docs: a "Securing the Server" section and a corrected Edge-enrollment description (the approval is no
  longer "pushed"); `SECURITY.md` updated.

### Changed

- Dependencies are unchanged from 1.2.20. First-run setup is intentionally unchanged (no bootstrap
  token); the new docs call out creating the admin account right after the first start.

## [1.2.20]

Security review follow-up.

### Security

- `rest/enroll` (unauthenticated) is now bounded and authenticated per Edge: the InstanceId must be a
  GUID, text fields are length-capped, the request body is limited to 4KB, at most 100 entries (5 per
  source IP) are held, and a pending entry expires after 15 minutes without a poll. Every call carries
  an `EnrollSecret` header (only its SHA-256 is kept), so knowing an InstanceId - visible to any
  `edges:read` user in the pending list - is no longer enough to collect an approved AccessKey or to
  take over another Edge's pending entry. Status responses are `Cache-Control: no-store`.
- The pending-edge list no longer exposes anything but identity fields (name, machine, platform,
  address, timestamps); the Console now shows machine and platform so an admin can recognise what
  they're approving.
- Approving a pending Edge validates the chosen name (1-64 letters/digits/`_`/`-`, not a reserved
  pseudo-edge name such as `Consumer` or `Management`, no `.`), closing a collision with
  `<edge>.<package>` credential names.
- `EdgeHub` only drops an Edge's pending entry on connect when the claimed InstanceId is the one
  configured for that same Edge name (the header is client-supplied).
- `rest/orbitmesh/SendMessage` (GET and POST) now requires the `messages:execute` scope like the hub and
  consumer equivalents; `Authorizations.Messages` alone is default-Allow for most credentials.
- A package's AccessKey is no longer kept on (or broadcast with) the package state shown to
  `packages:read` ControlHub subscribers - the Edge echoes back the exact description it was pushed,
  which includes the plaintext key.
- `upload-package` resolves the package file name through the same containment check as every other
  path (the name comes from inside the uploaded zip), and package name `.` is rejected.
- Unauthenticated-connection log lines in `EdgeHub` now go through `ForLog()`.

### Added

- The approval carries the approved Edge name to the Edge (`EnrollStatusResponse.EdgeName`), so
  renaming an Edge at approval time no longer leaves it connecting under a name its credential
  doesn't match.

### Changed

- Dependencies bumped (Microsoft.* 10.0.13, OpenTelemetry 1.20.x, Cronos 0.13, NLog 6.2.2).
  `Microsoft.OpenApi` stays on 2.x (2.12.2): 3.x is incompatible with the source generator in
  `Microsoft.AspNetCore.OpenApi` 10.0.13.

## [1.2.19]

### Changed

- Replaced the pending-Edge approval flow's SignalR-push mechanism with a plain REST poll: a new
  unauthenticated `POST rest/enroll` (records the attempt) + `GET rest/enroll/{instanceId}/status`
  (polled every few seconds by the Edge) let an unapproved Edge learn about its approval without
  keeping a live, unauthorized SignalR connection open and waiting on `EdgeHub.OnConnectedAsync` to
  push a credential down it. `EdgeHub` now aborts an unauthorized connection outright instead of
  special-casing a "pending" one that stays open.
- This also fixes a latent bug in the old push path: `ApprovePendingEdge` pushed the new AccessKey
  via `edgeHub.Clients.Client(pending.ConnectionId).SendAsync(...)`, which does **not** throw for a
  stale/disconnected ConnectionId - `pushed: true` could come back to the Console even when nothing
  actually received the key. The new flow has no such race: the Edge fetches the key itself on its
  next status poll, whenever that happens to land.
- `PendingEdgeRegistry` now keeps an approved entry around (with the new key attached) instead of
  removing it immediately, so a poll after approval can still find it - purged either when the Edge
  reconnects for real over SignalR with the new key, or after 10 minutes, whichever comes first.

## [1.2.18]

### Added

- `OrbitMeshHub.SubscribeTelemetryItems` now logs both outcomes (allowed/denied) of the
  `Authorizations.TelemetryItems` check it already ran but never reported - a denied subscription
  previously failed completely silently (no exception, no response to the caller), which made a
  deny-by-default Telemetry policy impossible to verify from the outside. Logged via the Server's own
  `ILogger` (console/file), not the Console's per-package log page.
- `OrbitMeshHub.SendMessage` had the exact same silent-denial gap 1.2.18 fixed for Telemetry
  subscriptions, for Messages: neither a missing `messages:execute` scope nor a failing
  `Authorizations.Messages` rule logged anything, and `PackageHost.SendMessage` is fire-and-forget on
  the sending side, so a denied message looked identical to a delivered one from the sender's own log
  (confirmed against a real credential: the sender logged "sent", nothing ever arrived). Now logs all
  three outcomes (missing scope / failed rule / allowed) via the Server's own `ILogger`.

## [1.2.17]

### Fixed

- SignalR's own default `MaximumReceiveMessageSize` (32KB) was never overridden, so any package
  sending a message or Telemetry Item bigger than that (e.g. a base64-encoded camera snapshot, ~30KB
  JPEG -> ~40KB+ once base64/JSON-wrapped) had its connection silently killed mid-send - with no
  exception surfaced to the sending client, whose `InvokeAsync` call just hung forever instead of
  failing cleanly. Raised to 4MB, comfortably covering a reasonably-sized payload without allowing
  arbitrarily large messages.

## [1.2.16]

### Fixed

- `ScheduledTaskOptions.LastRunUtc` was silently corrupted every time it round-tripped through
  config: `System.Text.Json` (the write path) serializes a UTC `DateTime` correctly, but
  `Microsoft.Extensions.Configuration.Binder` (the read path used by `IOptionsMonitor`) parses that
  same string with plain `DateTime.Parse`, which has no "Z" handling of its own and silently
  reinterprets it as local time - shifting the stored instant by the machine's UTC offset every
  time the option was re-read. On a task scheduled later in the day than the shift, this could push
  the computed "last run" past the next due occurrence, so `ScheduledTaskRunner` saw zero
  occurrences to catch up on and stopped firing entirely until a matching occurrence happened to
  line up again by coincidence. `LastRunUtc` is now a `DateTimeOffset?`, which round-trips through
  the same binder without reinterpretation.
- `CredentialOptions.LastUsedUtc` had the identical `DateTime`/Configuration-Binder corruption -
  same fix, now a `DateTimeOffset?`. Purely a display bug here (a wrong "Last Used" timestamp in
  the Console's Credentials page), not a functional one.

## [1.2.15]

### Added

- Scheduled tasks: fire a message on a cron schedule (`ScheduledTaskRunner`, new `ScheduledTaskOptions`
  section) - the automation equivalent of a human using the Console's Messages page. Each task runs
  as a chosen credential, through the same `CheckMessageAuthorization`/`messages:execute` check as
  any other sender - not a way around the permission model, just another caller of it. New scopes
  `schedules:read`/`schedules:manage`, new `GET/POST rest/management/scheduledtasks` and `DELETE
  rest/management/scheduledtasks/{name}`. An occurrence missed while the Server was down (update,
  crash, ...) is skipped by default; `CatchUpIfMissed` fires one catch-up send instead (never one
  per missed occurrence - this brings state back in line, it doesn't replay every missed day).
  Cron parsing via the new `Cronos` dependency.

## [1.2.14]

### Security

- `CheckMessageAuthorization`'s saga-response bypass trusted `scope.IsSaga`/`SagaId` unconditionally -
  both are entirely client-controlled (`SagaId` is even accepted as a raw query parameter on the
  Consumer REST `SendMessage` endpoint), so any credential holding just `messages:execute` could
  reach any target by claiming to be answering a saga that never happened, skipping
  `Authorizations.Messages` entirely (CodeQL `cs/user-controlled-bypass`). New `SagaRegistry`
  service tracks sagas this server actually saw go out (registered by `ConsumerController`,
  `ConsumerHub`, `OrbitMeshHub` right after a real request is authorized); the bypass now only
  fires for a `SagaId` still pending in that registry, and only when the response is addressed
  back to that saga's real originator - each entry expires after 5 minutes and is consumed
  (single-use) on a successful match. **No action needed** - existing saga request/response usage
  (`PackageHost`'s `WithSaga`/`OnSagaResponse`/`SendResponse`) is unaffected.

## [1.2.13]

### Security

- Sanitized user-controlled values (request path, `EdgeName`/`PackageName` read from a header or
  query string, credential name, HTTP method) before writing them into log messages -
  `PackageFileMiddleware`, `AccessKeyAuthenticationMiddleware`, `ManagementController`. Without
  this, a crafted header/query value containing CR/LF could forge fake-looking extra log entries
  (CodeQL `cs/log-forging`). New `LogSanitizerExtensions.ForLog()` escapes `\r`/`\n`/line-separator
  characters rather than stripping them, so the original value is still fully recoverable from the
  log.

## [1.2.12] - 2026-08-21

### Security

- Extended the `messages:execute` fix from 1.2.11 to Edges and Packages, which previously needed no
  scope at all to connect - `EdgeHub`/`OrbitMeshHub` only ever checked `CheckAccess` (no scope
  requirement for this access type) plus `Authorizations`. Two new scopes: `edge:connect`
  (`EdgeHub.IsAuthorized`) and `package:connect` (`OrbitMeshHub.OnConnectedAsync`), both required
  just to open the connection; `OrbitMeshHub.SendMessage` now also requires `messages:execute`,
  matching the Consumer-side fix. Scopes stay the coarse "can this credential act as an Edge/Package
  at all" gate, Authorizations remains the fine one underneath.
  **No action needed for existing Edges/Packages**: `EnsurePackageCredential` now grants
  `package:connect`/`messages:execute` on every deploy or Groups change, `ApprovePendingEdge` grants
  `edge:connect` to newly-approved Edges, and - unlike 1.2.11's Console-credential change - a
  one-time startup migration in `Program.cs` additively grants the right scope to every
  already-configured Edge/Package credential still missing it, so upgrading doesn't lock any of
  them out.

## [1.2.11] - 2026-08-21

### Security

- `ConsumerHub.SendMessage`/`ConsumerController.SendMessage` (REST) were gated only by
  `Authorizations.Messages`, whose `DefaultAuthorization` is `Allow` unless a credential has
  explicit rules - so any enabled Human/Console credential, including one meant to be read-only,
  could invoke any package's message handlers. Added a new `messages:execute` scope
  (`OrbitMeshScope.cs`), now required by both entry points before `Authorizations` is even
  consulted - restoring the two-level "Scope authorizes the action class, Authorizations restricts
  the target" model `OrbitMeshScope.cs`'s own doc comment already describes but this surface didn't
  actually implement.
  **Action required**: any existing credential that sends messages (the Console's Messages page,
  or a Machine credential like a kiosk/dashboard calling `/rest/consumer/SendMessage`) needs
  `messages:execute` added explicitly, or it will stop working after this update.

## [1.2.10] - 2026-08-20

### Added

- `LoginAttemptLimiter`'s in-memory brute-force lockouts are now visible and clearable from the
  Console instead of only via a full server restart - `GET rest/management/security/lockouts`
  (`configuration:read`) lists currently locked-out IPs, `DELETE
  rest/management/security/lockouts/{ip}` (`configuration:write`) clears one early. Shown as a
  "Locked-out IPs" panel on the Credentials page, polled every 5s like the Pending edges panel.

## [1.2.9] - 2026-08-20

### Security

- `PackageFileMiddleware` (`/packages/{file}`) stripped `".."` from the requested path but didn't
  reject a rooted one - `Path.Combine(root, relativePath)` discards `root` entirely when
  `relativePath` is itself rooted (e.g. `C:/...`), and colons are valid unencoded characters in a
  URL path segment, so `GET /packages/C:/OrbitMesh/keys/credential-encryption.key` reached the
  filesystem unmangled. This route only requires the lowest access tier (any Edge/package
  credential, not admin), so any registered Edge could read arbitrary files readable by the server
  process - including the AES key that decrypts every Machine credential's AccessKey. Fixed with
  the same containment check already used by `PackageInstance.ResolveContained` on the Edge side:
  canonicalize with `Path.GetFullPath` and reject anything that doesn't resolve inside the packages
  root directory.

- `ManagementController.ResolveZipPath` had the same rooted-path gap as above, reachable by any
  `packages:deploy`-scoped credential (not full admin) - `RenamePackageFile`'s body-supplied
  `NewName` and a NuGet-install's manifest-supplied package name could move/read/delete an arbitrary
  `.zip` file outside `PackagesRootDirectory`. Replaced with `TryResolveZipPath`, applying the same
  containment check at every call site (`GetPackage`, `RenamePackageFile`, `RemovePackageFile`,
  `GetPackageIcon`, `GetSettingXsdSchema`, `InstallFromNuGet`, `DownloadPackage`,
  `ValidateJsonAndXmlSettings`).

- `InstallFromNuGet` extracted a downloaded `.nupkg`'s `content/` entries via
  `ZipArchiveEntry.ExtractToFile` directly, which (unlike `ZipFile.ExtractToDirectory`) has no
  zip-slip protection of its own - a crafted entry name (`content/../../../foo`) could write outside
  the temp extraction directory before the manifest was ever validated. Now checks each entry's
  resolved destination stays within the temp directory and rejects the whole install otherwise.

- `DownloadPackage` fetched an admin-supplied `request.Url` server-side with no restriction - a
  `packages:deploy`-scoped credential could make the server issue requests to internal-only
  addresses or cloud instance-metadata endpoints (`169.254.169.254`) it can reach but the caller
  can't (blind SSRF). Now resolves the host and rejects loopback/link-local/private ranges before
  connecting, and disables automatic redirects (a passed check could otherwise be defeated by a 30x
  to an internal address).

## [1.2.8] - 2026-08-19

### Fixed

- `PreserveLiveState` moved `packages/` into the staging directory instead of copying it, before the
  handoff to `OrbitMesh.Updater` even succeeds - if anything went wrong between that move and a
  completed swap (the Updater killed before it could run, this process crashing, ...), the only copy
  was left stranded in an abandoned staging directory, which `ApplyAsync`'s next attempt deletes
  outright as its own "start clean" first step. Now copies, matching `appsettings.json`/`keys/` -
  the live directory keeps its real `packages/` until a swap actually completes.

- `CredentialUsageTracker.RecordUse` was only ever called from `AccessKeyAuthenticationMiddleware`
  (the REST gate) - packages, Edges, the Console, and Consumers all authenticate over SignalR, not
  REST, so their credentials' "Last Used" stayed "Never" forever. Now also recorded from each Hub's
  `OnConnectedAsync` (`OrbitMeshHub`, `EdgeHub`, `ControlHub`, `ConsumerHub`) right after their
  existing access check succeeds.

## [1.2.7] - 2026-08-19

### Fixed

- `UpdateVersionComparer.IsNewer` fell back to a plain string comparison when either version wasn't
  a parseable `System.Version` - the SDK appends `+<git-sha>` (SemVer build metadata) to
  `AssemblyInformationalVersionAttribute` by default in any build done inside a git working tree
  (CI included), so the running version never string-matched the clean version the update server
  reports, and "update available" stuck around forever after a successful update. Now strips the
  `+...` suffix before comparing, and `OrbitMesh.Server.csproj` sets
  `IncludeSourceRevisionInInformationalVersion=false` so the suffix isn't there to strip in the
  first place.

## [1.2.6] - 2026-08-19

### Fixed

- Self-update's handoff to `OrbitMesh.Updater` (`ServerSelfUpdater.LaunchUpdater`) spawned it via a
  bare `"dotnet"` command name, relying on `PATH` to resolve it - not guaranteed (`install.sh`'s
  systemd unit launches the Server itself via `dotnet`'s absolute path precisely because it isn't).
  On Linux this doesn't fail loudly: `Process.Start` succeeds (`fork()` succeeds), only the child's
  `exec()` fails, silently, before `OrbitMesh.Updater`'s own code - and its log file - ever run, so
  the update looked applied but never actually happened. Now uses `Environment.ProcessPath` (the
  exact `dotnet` running the current process), same fix `RelaunchCommand.Capture()` already uses for
  the analogous "relaunch myself" problem.

## [1.2.5] - 2026-08-19

### Fixed

- Pinned `Microsoft.OpenApi` to 2.12.0 - letting it float to 3.x broke the OpenAPI XML-comment
  source generator (`IOpenApiMediaType.Example` became read-only there), and 2.3.9 (a prior lower
  pin) carried a high-severity DoS vulnerability (GHSA-v5pm-xwqc-g5wc, fixed from 2.7.5). Updated
  `NuGetFeedClient`'s search filter for `NuGet.Protocol` 7.9's `SearchFilter.PackageTypes` ->
  `PackageType` rename.

## [1.2.4] - 2026-08-18

### Fixed

- `EdgeHub` could resurrect a pending entry for an Edge that was already approved: a stale
  AccessKey the old process still had cached during its own restart handoff could race the
  approval and land one more unauthorized call, re-adding it to `IPendingEdgeRegistry` with
  nothing left to ever clear it again (the Edge doesn't call the unauthorized path once it
  reconnects with its real key). `RecordPendingAttempt` now checks whether the InstanceId already
  matches a configured Edge first - if so it removes any pending entry instead of recording a new
  attempt.

- Self-update didn't preserve `keys/credential-encryption.key` across the swap
  (`ServerSelfUpdater.PreserveLiveState`) - a lost key means a fresh one gets generated on next
  start, silently making every previously-encrypted Machine credential undecryptable. That alone
  didn't error at startup (a credential's ciphertext isn't touched until something tries to use
  it), but `OrbitMeshDirectory.MatchesAccessKey` only caught `FormatException` around the decrypt,
  not the `CryptographicException`/`AuthenticationTagMismatchException` AES-GCM throws for a
  ciphertext that doesn't authenticate - so `GetCredentialName` (called on every authenticated
  request, to scan the full credential list for a name match) would 500 on completely unrelated
  requests the moment any one credential in the list had gone stale this way. Both fixed: the key
  now survives a self-update, and a credential that fails to decrypt is now just treated as "not a
  match" instead of crashing the request.

### Added

- `install.sh`/`install.ps1` now preconfigure the official update server (`https://updates.orbitmesh.org`)
  with its public signing key, and the official NuGet feed (`https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json`)
  in `nuGetFeeds` - both are plain entries in `appsettings.json` and can be edited or added to for a
  self-hosted update server/feed.
- `SetPackageSettings` now rejects (400) a `JsonObject`/`XmlDocument`-typed setting whose value
  doesn't parse, resolved against the deploying package's own manifest - `{Variable}` tokens are
  substituted with a `0` placeholder first (valid JSON either bare or already inside quotes) so a
  legitimate token reference isn't flagged as malformed.
- Variables: named values (`VariableOptions`, `GET/POST rest/management/variables`,
  `DELETE .../{name}`) any package's settings can reference by writing `{Name}` anywhere in a
  setting's value, including inside a JSON setting - `IOrbitMeshDirectory.GetSettings` gained a
  `substituteVariables` parameter that replaces every token with the Variable's value, but only at
  the points settings are actually delivered to a connected package (`OrbitMeshHub.RequestSettings`,
  `OrbitMeshController.GetSettings`, `ControlHub.ReloadServerConfiguration`/`UpdatePackageSettings`)
  - never when the Console reads/writes a package's settings, so editing one package's settings can
  never bake in and silently detach a value that was meant to keep tracking the shared Variable. A
  Variable can be marked secret, encrypting its value at rest with the same `AccessKeyCipher` used
  for Machine credentials; `GET variables` never includes a secret's value, `GET
  variables/{name}/reveal` decrypts it on demand.
- Each package now gets its own auto-provisioned Machine credential (`{edge}.{package}`,
  `InsertOrUpdatePackage`/`EnsurePackageCredential`) instead of falling back to the Edge's own
  credential (see `PackageInstance.cs`'s AccessKey resolution). Its `Authorizations` default to
  `Deny` with Rules scoped to only its own edge+package telemetry, its own configured Groups, and
  Messages addressed to itself/its own edge/its own Groups - `SyncPackageCredentialAuthorizations`
  rebuilds these Rules on every deploy and on `SetPackageGroups`, so they stay in sync. Cross-package
  telemetry/messaging (e.g. a dashboard, or genuine inter-package RPC) now needs an explicit Rule
  added on that credential - previously every package shared the Edge's key with full default-Allow
  access to every other edge/package's telemetry and messages.
- `PackageHost`/`[MessageHandler]` message keys are now namespaced under the sending package's own
  name by default (`"PackageName/Key"`, see `MessageHandlerAttribute.Shared` and the `shared`
  parameter on `SendMessage`/`RegisterMessageHandler`) - two unrelated packages picking the same key
  (e.g. both handling `"Play"`) no longer cross-trigger each other's handler. The saga response key
  is exempt (fixed system key, correlated by SagaId, not package identity).
- Pending-edges list and approval flow. `EdgeHub.OnConnectedAsync` no longer aborts a connection
  that fails authorization but presents a well-formed `InstanceId` (see
  `OrbitMeshHeaderNames.InstanceId`, `EdgeDescription.InstanceId`) - it's kept open (no capability
  granted; every other hub method independently re-checks authorization) and recorded by the new
  `IPendingEdgeRegistry`, instead of being rejected and forgotten. `RegisterEdge` returning `false`
  while unauthorized doubles as a heartbeat, refreshing the pending entry every ~3s via the Edge's
  own existing retry loop. `GET/POST/DELETE /rest/management/edges/pending[...]` list, approve, and
  dismiss pending attempts. Approving creates a Machine credential + `EdgeOptions` entry (which
  gained an optional `InstanceId` field, set here) and pushes the new AccessKey down the Edge's
  still-open pending connection (`EdgeClientMethodNames.EdgeApproved`) - fire-and-forget, so the
  response also always carries the key as a manual-paste fallback in case the Edge had disconnected
  in the interim. `InstanceId` isn't enforced as an extra `CheckAccess` gate yet - `EdgeName` +
  `AccessKey` still fully gate access as before.
- `ControlHub.CheckForEdgeUpdate(edgeName)` (requires `updates:manage`) pushes
  `EdgeServerMethodNames.CheckForUpdate` to a connected Edge, so the Console can trigger an update
  check/apply on demand instead of only ever waiting for the Edge's own polling timer.
- `install.sh`'s generated systemd unit uses `KillMode=process` so `OrbitMesh.Updater` - spawned as
  this process's child during self-update - survives past the handoff instead of being killed with
  the rest of the cgroup. It also writes a sudoers rule scoped to `systemctl start/stop` for the
  unit, since the service runs as a non-root user; `ProcessRestarter` calls it via `sudo`. The rule's
  `systemctl` path is now canonicalized with `readlink -f` (sudo resolves it via its own
  `secure_path`, which can land on a different but equivalent symlinked path than root's own `$PATH`
  at install time - a mismatch there silently falls back to requiring a password), and
  `verify_sudoers_rule` proves the rule actually works for the real service user right after the
  unit is enabled, instead of only surfacing a problem mid self-update with no terminal to fix it from.
  Both the sudoers rule and the systemd unit/verification step now key off whether `server`/`edge`
  are actually present on disk (`SERVER_PRESENT`/`EDGE_PRESENT`), not whether they were freshly
  re-fetched this run - re-running the script with "n" to every overwrite prompt (e.g. just to pick
  up an `OrbitMesh.Updater` fix) used to silently skip both, leaving an already-running service with
  no sudoers rule at all.

## [1.0.0] - 2026-08-13

Baseline - v1. Everything up to this point (credentials/scopes, file servers, self-update,
package registry, telemetry, groups, ...) is considered the starting line, not individually
logged. Every change from here on gets an entry above.
