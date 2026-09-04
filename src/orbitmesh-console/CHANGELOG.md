# Changelog - OrbitMesh Console

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match the `VERSION` file at
the root of this project, bumped by `cicd/release-static-site.ps1`.

## [1.2.13]

### Changed

- The top nav bar now stays visible while scrolling a long page (`position: sticky`) instead of
  scrolling away with the content.

## [1.2.12]

### Added

- Browser tab favicon - the Console had none, falling back to the browser's generic blank-page icon.
  New `favicon.ico` (multi-size: 16-256px), a simple orbit-ring-and-nodes mark on a dark rounded
  square, matching the app's own dark theme.

## [1.2.11]

### Fixed

- The Messages page showed the package name instead of the handler's own name (e.g. "OnvifDoods"
  instead of "DetectNow") for any non-shared handler. `store.js` built its internal dictionary key by
  re-prefixing the package name onto `MessageKey`, which the Server already sends fully qualified
  (`"PackageName/key"`) - producing a double-qualified key ("PackageName/PackageName/key") that broke
  `key.split('/')[1]`'s assumption of exactly one slash. Now derives the displayed name from
  `MessageHandler.MessageKey` directly (`.split('/').pop()`), which is correct regardless of
  qualification. Invoking a handler was never affected - that path already used `MessageKey` directly.

## [1.2.10]

### Added

- New "Scheduled Tasks" page (Administration) - fires a message on a cron schedule as a chosen
  credential. Edge/Package/Message handler are picked from dropdowns backed by real connected
  packages and their actually-declared `[MessageHandler]`s (reusing `store.messageHandlers`, the
  same data the Messages page already shows) instead of free-typed names, and per-parameter inputs
  are generated from the handler's own declared parameters - both meant to rule out a typo'd
  key/parameter going unnoticed in an unattended, timer-triggered send. New "Schedules" scope
  category (`schedules:read`/`schedules:manage`) in the Credentials page's scope matrix. See
  OrbitMesh.Server 1.2.15's CHANGELOG.

## [1.2.9]

### Security

- CodeMirror (vendored, `Libs/codemirror/modes/`): fixed two regexes whose alternatives could
  re-partition the same input multiple ways, causing exponential backtracking (ReDoS) on
  pathological input - the async-arrow whitespace/comment skip and the TypeScript generic-call
  lookahead in `javascript.js`. Verified matching behavior is unchanged on normal input.
- CodeMirror (vendored): the XML/HTML comment tokenizer in `xml.js` only recognized `-->` as the
  end of a comment, not the `--!>` "bogus comment" form real browsers also terminate on - a
  highlighter/filter relying on the old regex could still think it was inside a comment for
  content the browser already treats as live markup.

## [1.2.8]

### Fixed

- Expired/invalidated session no longer leaves the console stuck on a "logged in" shell with no
  working data. `store.isLoggedIn` used to only change on an explicit login/logout - it never
  reacted to the AccessKey cookie actually expiring or the server rejecting it (401/403), and the
  router's login guard only re-checks on navigation, so a user sitting on a page with no route
  change never got bounced to `/login`. Now: any 401/403 from the Management API forces a logout +
  redirect, and a periodic check (every 15s, plus on tab focus) catches the cookie expiring outright.

## [1.2.7] - 2026-08-21

### Added

- Credentials page: two new scopes in the matrix - `edge:connect` (Edges category) and
  `package:connect` (Packages category), marked as being for an Edge's/Package's own credential
  rather than an admin account, see OrbitMesh.Server 1.2.12's CHANGELOG. No action needed for
  existing Edge/Package credentials - the Server grants these automatically.

## [1.2.6] - 2026-08-21

### Added

- Credentials page: new "Messages" scope category (`messages:execute`) in the scope matrix - now
  required server-side before a credential can send a message (Messages page, or a Consumer API
  caller), see OrbitMesh.Server 1.2.11's CHANGELOG. Grant it to any credential that needs to keep
  sending messages after updating.

## [1.2.5] - 2026-08-20

### Added

- Credentials page: a "Locked-out IPs" panel lists IPs currently blocked by the brute-force
  protection (`LoginAttemptLimiter`), with an "Unlock" button to clear one early instead of
  restarting the server - polled every 5s, same pattern as the Edges page's Pending edges panel.

## [1.2.4] - 2026-08-19

### Changed

- Nav menu regrouped into dropdowns: Edges/Packages/Variables under a new "Fleet" group,
  Repository/Credentials/Configuration under a new "Administration" group - flattens what had
  grown into 10 top-level links down to Home, Telemetry, Messages, Console log, Fleet,
  Administration. Both group labels are non-navigating hover triggers (pure CSS, no new state),
  so clicking behaves the same for every group; each stays highlighted while any of its
  sub-pages is the active route.

## [1.2.3] - 2026-08-18

### Added

- App-wide footer (connection info, Console/Server version, copyright), replacing the "Connected
  to..." line that used to live only on the Home page. The Console's own version now comes from
  `GET update/sites`'s `CurrentVersion` for the `orbitmesh-console` project - the Server already
  tracks what's actually deployed (`StaticSiteUpdater`'s marker file) - instead of a hand-maintained
  constant with no way to stay in sync with the real `VERSION` file bumped at release time.
- Removing a package now asks whether to also delete its dedicated credential (checked by default) -
  previously the credential was silently left behind, orphaned, after the package it belonged to
  was gone.
- The Packages page's Settings editor (Manage panel and deploy wizard) now validates every
  `JsonObject`/`XmlDocument` setting before saving - same idea as the Configuration page's
  client-side `JSON.parse` check, but per-setting and type-aware, with the same `{Variable}` →
  placeholder substitution the Server uses so a token reference isn't flagged as invalid.
- New "Variables" page: named values (optionally marked secret, encrypted at rest, hidden behind a
  "Reveal" button instead of shown in the list) any package's settings can reference by writing
  `{Name}` in a setting's value - including inside a JSON setting, so it works for a package like
  ForecastIO/OpenWeather that bundles coordinates inside a JSON blob, not just a package with flat
  top-level settings. Change a Variable once and every package referencing it picks it up on its own
  - the token stays as literal text in whatever the Console reads/writes, so editing and saving one
  package's settings can never detach it from the shared value. The Packages page's Settings editor
  shows the available variable names as one-click-to-copy tokens to paste into a value.
- "Pending edges" panel on the Edges page: shows Edge connections waiting for approval (declared
  name, source IP, first/last seen), polled every 5s. Approve creates a matching credential + edge
  entry; the Server also pushes the new key straight to the Edge automatically if it's still
  connected. The reveal-once dialog (same as Credentials) still shows the key either way - wording
  adapts depending on whether the push was attempted (`pushed` in the response) - as a manual-paste
  fallback in case the Edge wasn't still connected to receive it.

## [1.0.0] - 2026-08-13

Baseline - v1. Everything up to this point is considered the starting line, not individually
logged. Every change from here on gets an entry above.
