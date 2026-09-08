# Changelog

All notable changes to the UpdateWatch2 Agent are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md. This file tracks the **agent**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol bump is called out inline below where a
change caused one, but this changelog isn't that changelog.

## [0.14.0] - 2026-09-08

### Changed

- Default server port changed from 8443 to 8796, matching the
  server's own default renumbering (server v0.19.0) — 8443 is common
  enough to collide with something else already running on a host.
  Not a breaking change for an already-registered agent: the port is
  only ever read as a fallback when nothing else supplied a value, so
  an existing registry/config entry with an explicit port is
  untouched.

## [0.13.1] - 2026-09-08

### Fixed

- A successful remote-triggered install left the admin UI's
  pending-updates list stale until `UpdateCheckWorker`'s own next
  jittered tick, up to `UpdateCheckIntervalMinutes` (plus jitter)
  later. `HeartbeatWorker` now triggers the same check-and-report
  logic immediately after `InstallOutcome.Succeeded` via a new
  `IUpdateCheckTrigger` interface, so the list is current the moment
  install-ack lands.

## [0.13.0] - 2026-09-08

### Added

- Downloaded self-update packages no longer accumulate forever in the
  local staging directory — `SelfUpdate/SelfUpdateStagingCleaner`,
  invoked on the same `HeartbeatWorker` cadence as every other
  maintenance concern, deletes anything older than the new
  `AgentOptions.SelfUpdateStagingRetentionDays` (default 90,
  admin-configurable), always keeping the single most-recently-
  downloaded file regardless of age.

## [0.12.4] - 2026-09-08

### Fixed

- Linux self-update deadlocked on every attempt (found via a real
  incident): `LinuxPackageApplier` ran `dpkg -i`/`rpm -U` as a plain
  child process, but the *old* package's `prerm` hook stops
  `updatewatch2-agent.service` mid-transaction to release the file
  lock — and systemd's default `KillMode=control-group` sends SIGTERM
  to every process in that cgroup, including the `dpkg`/`rpm` child
  the agent had just spawned, killing the install before it could
  finish. Fixed by launching the install inside a brand-new detached
  `systemd-run --scope`, which gets its own separate cgroup so
  `prerm` stopping the agent's unit no longer reaches back and kills
  the install performing it. A genuine install failure now surfaces
  via `journalctl -u updatewatch2-agent-selfupdate-*.scope` rather
  than this agent's own log, since `LinuxPackageApplier` can no
  longer observe the detached scope's exit code.
- A CI-only flaky test
  (`HeartbeatWorker_retries_after_a_non_shutdown_...`) — the same
  race class already documented on a sibling test: with a
  zero-duration heartbeat interval, extra `SendAliveAsync` calls could
  race in before `StopAsync()` took effect, breaking an exact
  call-count assertion. Fixed the same way the sibling test already
  does — a racing call after cancellation is requested throws
  immediately instead of incrementing the counter.

## [0.12.3] - 2026-09-08

### Fixed

- The compiled Windows binary's file-version resource (Explorer's
  "Details" tab) was always `1.0.0.0`/`1.0.0` regardless of
  `AgentVersion.Current`, since the csproj never set `<Version>`.
  Fixed by reading it from the repo-root `VERSION` file at build time,
  the same source `release.yml` already reads for the NSIS installer
  and the `fpm` package version.

## [0.12.2] - 2026-09-08

### Fixed

- `installer/linux/updatewatch2-agent.service` never set
  `WorkingDirectory=`, so systemd defaulted the agent's cwd to `/` —
  the .NET Generic Host derives `ContentRootPath` from cwd, and its
  config-reload file watcher then recursively `inotify`-watched the
  *entire* filesystem tree reachable from `/`, including a large
  NFS-mounted share on the host that surfaced this. That walk happens
  synchronously during host construction, before any application code
  runs, so `RegistrationWorker` never got to log a single line —
  indistinguishable from a hang, and easy to mistake for the
  token/CA-mismatch class of registration problem. Fixed by pinning
  `WorkingDirectory=/opt/updatewatch2-agent` in the unit; live-
  confirmed (correct content root logged immediately, registration
  followed within the same second).

## [0.12.1] - 2026-09-07

### Changed

- README rewritten with badges, a feature overview grouped by area, an
  explicit project-status section calling out which pieces aren't yet
  live-verified, step-by-step Windows/Linux installation instructions,
  and a full German translation (`README.de.md`).

### Fixed

- A network timeout during registration (`HttpClient`'s own request
  timeout) surfaces as a `TaskCanceledException`, which the loop's
  inner safeguard treated identically to every other
  `OperationCanceledException` and let fall through into the outer
  shutdown handler — silently and permanently ending
  `RegistrationWorker` for the rest of the process's life, with zero
  log output. Found via a real incident: a deleted-then-recreated
  agent never re-registered despite repeated service restarts. Fixed
  by checking `stoppingToken.IsCancellationRequested` explicitly, so
  only a genuine shutdown is let through; the identical latent pattern
  in `HeartbeatWorker`/`UpdateCheckWorker` was fixed the same way.
- The `LogLevel` setting (registry value/`agent.conf` field) was never
  actually applied — `Program.cs` read a configuration key nothing in
  this codebase ever set, instead of `AgentOptions.LogLevel`, so it
  always silently fell back to the hardcoded `INFO` default. Also
  fixed by writing the resolved level directly into
  `Logging:LogLevel:Default`, matching the server's own documented
  finding that `SetMinimumLevel(...)` alone doesn't reliably take
  effect.

## [0.12.0] - 2026-09-07

### Added

- Reacts to the server's new `certificateRotationPending` field by
  renewing its client certificate immediately, rather than waiting for
  its own expiry-driven `CertificateRenewalLeadTimeDays` window —
  closes the exposure window where CA root rotation never rotated an
  already-onboarded agent's own leaf, only the server's. Reuses the
  existing `POST .../renew` call and the existing renew-and-hot-swap
  logic (refactored into a shared `RenewClientCertificateAsync`
  helper); no acknowledgement needed, since the field is self-
  correcting on the next heartbeat.

### Changed

- Protocol version bumped to `0.8.0`, matching the server.

## [0.11.1] - 2026-09-06

### Added

- CI now runs `dotnet test` on every push/PR to `main`
  (`.github/workflows/ci.yml`), not only on a release tag push —
  closing a gap where a commit that broke the test suite could sit on
  `main` indefinitely with nothing surfacing the failure
  (`updatewatch2-agent#11`).

### Fixed

- A `.deb`/`.rpm` upgrade left an already-configured, already-running
  agent stopped after the upgrade completed. `prerm.sh` stops the old
  service before dpkg/rpm unpacks the new files, on the assumption
  that `postinst.sh` starts it back up — but `postinst.sh`
  unconditionally printed "no server configured yet" guidance and left
  the service stopped, even when the config already had a real
  `ServerAddress` from before the upgrade. Found by a real `dpkg -i`
  upgrade on a real host. Fixed by having `postinst.sh` check for a
  non-empty `ServerAddress` and `systemctl restart` the unit when one
  is found.

## [0.11.0] - 2026-09-06

### Added

- Self-updates the agent's own binary/service to a newer release the
  server offers (`updatewatch2-agent#14`), reacting to the
  `agentUpdateAvailable` field the server already surfaces on the
  `alive` response. Independently re-checks the offer is actually
  newer than `AgentVersion.Current`, downloads the asset for this
  platform via a new `IServerClient.DownloadFileAsync`, and verifies
  its SHA-256 against the server-published value before ever applying
  it — a mismatch aborts and deletes the download untouched.
  `Windows.WindowsInstallerApplier` silently re-runs the NSIS
  installer; `Linux.LinuxPackageApplier` shells out to `dpkg -i`/
  `rpm -U` then restarts the unit; `NoOpAgentSelfUpdater` covers a
  Linux host with neither package manager. No acknowledgement call
  needed — the offer naturally stops once the next successful
  heartbeat after restarting self-reports the new `AgentVersion`.
  **Neither apply step is live-verified yet** — no Windows host was
  available, and the Linux dev sandbox has no systemd.

### Changed

- Protocol version bumped to `0.7.0` to catch up with the server's
  (this build is the first to actually depend on the `0.7.0` shape
  the server introduced for `updatewatch2-server#14`).

## [0.10.0] - 2026-09-06

### Added

- A real Linux update checker (`updatewatch2-agent#8`), replacing the
  unconditional `NoOpUpdateChecker`: `UpdateCheck/Linux/AptUpdateSession`
  for Debian-derived distros (`apt-get update` then
  `apt list --upgradable`, reboot-required via
  `/var/run/reboot-required`) and `DnfUpdateSession` for RPM-based
  distros (`dnf`/`yum check-update`, reboot-required via
  `needs-restarting -r`), picked at startup by a new
  `LinuxPackageManagerDetector` with a `NoOpUpdateChecker` fallback for
  neither. The apt/Debian search path was live-verified against the
  dev sandbox's own real package cache, correctly finding and parsing
  all 40 of its real pending upgrades; the install path and the entire
  dnf/yum session were not.

### Fixed

- Both sessions force `LC_ALL=C`/`LANG=C` on the shelled-out child
  process — without it, `apt list --upgradable` under a non-English
  locale (caught by hand in the project's own dev sandbox, which
  printed German output) would have silently broken the English-
  pattern output parser.

## [0.9.0] - 2026-09-05

### Added

- Real Windows Update API integration for update detection and
  install (`updatewatch2-agent#7`), via late-bound COM
  (`WuaUpdateSession`) against WUApiLib — searches with
  `IsInstalled=0 and IsHidden=0 and Type='Software'` (excluding driver
  updates), never triggers a reboot itself, and reads reboot-required
  state independently via `Microsoft.Update.SystemInfo`.
  `WindowsUpdateChecker` itself is no longer Windows-gated, giving it
  real unit test coverage under this project's Linux CI against a
  hand-written fake session. **Not live-verified against a real
  Windows Update Agent** — no Windows host was available when this was
  written; treat it as a well-researched first implementation.

## [0.8.0] - 2026-09-05

### Added

- Supports server CA root rotation (`updatewatch2-server#6`): this agent
  now trusts a COLLECTION of CA roots, not just one — `FileCaTrustStore`
  can hold more than one certificate at a time, and
  `PinnedServerCertificateValidator` accepts the server's TLS leaf if it
  chains to any of them. On every heartbeat, `HeartbeatWorker` fetches
  the server's full published root bundle (`GET /api/agent/ca-certificates`)
  and adds any root this agent doesn't already trust — purely additive,
  never removing a root on its own. This is what lets an agent pre-trust
  a root an admin has prepared but not yet activated, so activation
  (the moment the server's own leaf switches roots) never interrupts
  this agent's connectivity, provided it already had at least one
  heartbeat's worth of time to pick the new root up first.

### Changed

- Protocol version bumped to `0.6.0`, matching the server.

## [0.7.0] - 2026-09-05

### Added

- Every alive heartbeat now reports this agent's current
  `DnsName`/`OperatingSystem`/`IpAddress`/`AgentVersion` (`updatewatch2-agent#6`),
  re-resolved fresh each time rather than only at registration. Closes
  the gap where an already-certified agent's metadata in the admin
  overview was frozen at whatever it reported at first contact — the
  server never re-runs registration for a certified agent, so the
  heartbeat is the only remaining channel to keep it current.

### Changed

- Protocol version bumped to `0.5.0`, matching the server.

## [0.6.2] - 2026-09-05

### Changed

- The reported operating system is no longer just the generic
  `RuntimeInformation.OSDescription` string on Windows (e.g. "Microsoft
  Windows 10.0.26200") — a friendlier name (e.g. "Windows 11 25H2",
  "Windows Server 2025 Standard") is now shown ahead of it, with the raw
  value kept in parentheses. Correctly distinguishes Windows 10 from 11
  by build number, not the registry's `ProductName` value, which is
  known to still read "Windows 10 ..." on genuine Windows 11 installs.

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
