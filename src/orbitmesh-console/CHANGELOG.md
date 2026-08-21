# Changelog - OrbitMesh Console

Format: [Keep a Changelog](https://keepachangelog.com/). Versions match the `VERSION` file at
the root of this project, bumped by `cicd/release-static-site.ps1`.

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
