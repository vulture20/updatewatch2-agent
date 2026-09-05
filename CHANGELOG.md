# Changelog

All notable changes to the UpdateWatch2 Agent are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md. This file tracks the **agent**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol bump is called out inline below where a
change caused one, but this changelog isn't that changelog.

## [0.6.1] - 2026-09-05

### Fixed

- The agent's IP address never showed up in the admin overview:
  `RegisterAsync` always sent `IpAddress: null` (an unimplemented TODO,
  not a transient issue). Now resolved against the server's own
  address/port, so a multi-homed machine reports the interface it
  actually uses to reach the server rather than an arbitrary local IP.

## [0.6.0] - 2026-09-05

### Added

- The agent now receives and acts on a remote-triggered install command
  (`updatewatch2-agent#4`): `HeartbeatWorker` picks up a pending install
  request from the server's `alive` response and invokes a new
  `IUpdateChecker.InstallAsync` (a placeholder on both platforms, same
  caveat as `CheckAsync`'s own lack of real Windows Update API
  integration), then acknowledges the outcome back to the server.

### Changed

- Protocol version bumped to `0.4.0`, matching the server.

## [0.5.0] - 2026-09-04

### Added

- Proactive client certificate renewal before expiry, and
  `RegistrationWorker` turned into a persistent maintenance loop so a
  certificate lost mid-lifetime (not just at startup) is recovered
  without a service restart.
- Self-heals after the server stops trusting a certificate the agent
  still has loaded (e.g. an admin-mediated re-issuance while the agent
  keeps running), distinct from recovering a genuinely lost certificate.
- Licensed the project under AGPL-3.0-or-later.
- Release packaging: a Windows NSIS installer and Linux `.deb`/`.rpm`
  packages, built and attached to a GitHub Release on every `vX.Y.Z` tag
  push.

### Fixed

- Corrected the copyright holder name in the README.
- A real, timing-dependent test flake in `HeartbeatWorker`'s
  rejection-counting test, caught by the new release workflow's own
  `dotnet test` gate.

## [0.4.0] - 2026-09-04

### Added

- Certificate renewal before expiry, authenticated by the agent's
  current still-valid client certificate rather than a registration
  token.

## [0.3.0] - 2026-09-04

### Added

- Detects a protocol-version mismatch against the server, piggybacked on
  the existing heartbeat cadence (logs a warning, not a hard rejection).

### Fixed

- Bootstrap registration traffic (fetching the CA certificate, every
  registration poll) poisoned the shared HTTP connection pool, causing
  every post-registration call to silently present no client
  certificate at all. Bootstrap traffic now uses its own, separate
  connection pool.

## [0.2.0] - 2026-09-04

### Added

- End-to-end registration: `RegistrationWorker` drives the
  register-then-poll-until-approved-and-certified flow
  (`updatewatch2-agent#1`).

## [0.1.1] - 2026-09-03

### Added

- The compiled Windows binary now embeds the application icon.

## [0.1.0] - 2026-09-03

### Added

- Initial scaffold: the .NET Generic Host Worker Service (configuration,
  server communication, update-check modules) and generated branding
  assets.
