# Changelog

All notable changes to the UpdateWatch2 Agent are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md. This file tracks the **agent**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol bump is called out inline below where a
change caused one, but this changelog isn't that changelog.

## [0.15.3] - 2026-09-11

### Fixed

- **Argument-injection risk in the new selective-install command building (0.15.2), found by an automated security review of the pushed commit, not by testing.** `AptUpdateSession`/`DnfUpdateSession.DownloadAndInstallAsync` spliced the requested `packageNames` directly onto the `apt-get`/`dnf` argument list with no `--` end-of-options marker — a value starting with `-` would be parsed by the package manager as an additional flag rather than a positional package name (option smuggling), not the classic shell-metacharacter injection this project never has to worry about in the first place (`ShellCommand` passes each argument straight through `ProcessStartInfo.ArgumentList`, never a shell). Neither Debian nor RPM package names may start with `-` by policy, so nothing was actually relying on that being enforced anywhere upstream — a real package name was never at risk in the normal flow, but nothing prevented a crafted value either. Fixed by inserting a `--` marker before the package-name list in both classes (`apt-get`'s and `dnf`'s GNU-style argument parsers both treat everything after `--` as strictly positional, regardless of what it starts with) — the standard, complete fix for this class of issue. The argument-building logic was pulled out into a new testable `BuildInstallArgs` static method on each class (mirroring `AptOutputParser`/`DnfOutputParser`'s own public-static-method convention) specifically so this could get real unit test coverage, including a test asserting the `--` marker is actually present — the shelling-out half itself remains untested, same as the rest of these two classes.

## [0.15.2] - 2026-09-11

### Added

- **Supports installing only a specific subset of pending updates, at the user's explicit request (server-side: "Schaffe eine Möglichkeit nur bestimmte Updates zu installieren und manche auszusparen") — protocol `0.9.0`.** `IUpdateChecker.InstallAsync` gained an `IReadOnlyList<string>? packageIds` parameter — null (the original behavior) installs everything currently pending; a non-null list restricts installation to updates whose own `PackageId` is in it. `WuaUpdateSession.DownloadAndInstall` filters its re-searched WUA collection by each pending update's own KB-article number; `AptUpdateSession`/`DnfUpdateSession.DownloadAndInstallAsync` use `apt-get install --only-upgrade <names>` / `dnf update <names>` instead of the unscoped `dist-upgrade`/bare `update` when a selection is given — both package managers already accept specific package names as trailing arguments for exactly this. `HeartbeatWorker` threads the server's `alive`-response `installUpdateIds` field straight through to `InstallAsync`, the same way `installRequested` already drove the call at all. **One honestly-scoped limitation**: a Windows update with no KB article at all (`PackageId` null — rare, but already a known possibility per `WuaUpdateSession`'s own doc comment) can't be individually named on the wire, so it's always included in any install regardless of selection rather than becoming permanently un-installable the moment any selection is made — every Linux update always has a non-null `PackageId`, so this gap is Windows-only. New real test coverage for the selection-matching/argument-building logic in `WindowsUpdateCheckerTests`/`LinuxUpdateCheckerTests`/`WorkerTests`; the underlying OS-level commands themselves (WUA's COM filtering, apt's `--only-upgrade`, dnf/yum's scoped `update`) were not live-run against a real Windows Update Agent or Linux package manager — same standing caveat this codebase already carries for every other OS-level install path (WUApiLib never verified on real Windows; a real apt/dnf install never run in this project's own dev sandbox, to avoid mutating it). The orchestration around them (selection matching, server-response wiring) was live-verified against a real running server over a genuine mTLS handshake — see the server repo's own changelog for that run's details.

## [0.15.1] - 2026-09-10

### Fixed

- **A real production crash, reported via a Windows Event Viewer APPCRASH
  entry for agent v0.14.2: an unhandled `System.OperationCanceledException`
  from `WindowsServiceLifetime.StopAsync` (`Host.StopAsync` ->
  `WaitForShutdownAsync` -> `RunAsync` -> `Run` -> `Main`), terminating the
  entire process instead of stopping cleanly.** Generic Host's own
  shutdown path throws this when stopping all `IHostedService`s takes
  longer than `HostOptions.ShutdownTimeout` (a 5-second default) — nothing
  in `Microsoft.Extensions.Hosting` catches it, so left unhandled it
  crashes the whole process. `Program.cs`'s bare `host.Run()` had no
  try/catch around it at all. A very plausible contributor to why 5
  seconds wasn't enough: `WindowsUpdateChecker`'s `Task.Run(() => ...,
  ct)` around `WuaUpdateSession`'s synchronous COM calls only cancels the
  work item before it starts — once a real Windows Update search/
  download/install is actually running, there is no way for this
  codebase to abort it mid-call, so a stop requested while one is in
  flight can legitimately take much longer than any short timeout allows.
  Fixed two ways: `HostOptions.ShutdownTimeout` raised to 30 seconds (a
  mitigation for ordinary in-flight work like an HTTP retry — not a full
  fix, since a real Windows Update install still can't be bounded by any
  timeout this codebase controls), and `host.Run()` wrapped in a
  try/catch for `OperationCanceledException` that logs a Warning directly
  to the Windows Event Log (the app's own DI-backed logging is already
  disposed by the time this exception reaches `Main` — `RunAsync`'s own
  `finally` disposes the host before the exception propagates out of it)
  and exits instead of crashing. Not unit-testable in this project's
  Linux-based suite (`WindowsServiceLifetime`/`Program.Main` aren't
  something `dotnet test` on `ubuntu-latest` can exercise at all) — treat
  this as a well-reasoned fix for a real, evidenced crash, not a
  live-reverified one; re-confirm on a real Windows host that a shutdown
  timing out now logs a Warning and exits cleanly instead of crashing,
  the next time this is easy to trigger on purpose (e.g. stop the service
  while a real Windows Update install is in progress).

## [0.15.0] - 2026-09-09

### Added

- New NSIS silent-install switch `/CACERT=<path>` (Windows): pre-seeds the
  server's CA root certificate at `%ProgramData%\UpdateWatch2\ca.pem` —
  the same fixed path `FileCaTrustStore` reads on Windows — *before* the
  service's first start, so `RegistrationWorker.EnsureCaPinnedAsync`'s
  existing "already pinned, skip the fetch" early-return applies from this
  agent's very first tick. Closes the trust-on-first-use (TOFU) window a
  freshly installed, un-pre-seeded agent otherwise has at its own very
  first contact with the server — a network attacker present at exactly
  that moment could previously hand it a malicious CA. Get the file from
  the server admin UI's Certificates tab (server v0.26.0's new
  session-authenticated `GET /api/admin/certificate-authority/download`)
  before running this installer. Omitting the switch leaves TOFU behavior
  completely unchanged — fully backward compatible.
- `installer/linux/postinst.sh` now normalizes ownership/permissions
  (`root:root`, `644` — world-readable, since unlike `agent.conf`/
  `agent.pfx` this is a public certificate, not a secret) of a
  `/etc/updatewatch2/ca.pem` an admin has manually pre-staged ahead of the
  first `systemctl start`. `fpm`-built `.deb`/`.rpm` packages have no
  install-time parameter mechanism, so unlike the Windows NSIS switch
  above, closing TOFU on Linux stays a manual scp-then-place workflow —
  this just makes a hand-placed file forgiving of whatever permissions it
  happened to arrive with, never creating or fetching one itself.
- New `RegistrationWorkerTests` case,
  `Skips_fetching_the_CA_certificate_when_one_is_already_pinned_but_registration_still_proceeds`,
  covering the specific gap the two changes above depend on (CA already
  pinned, but no client certificate yet) — distinct from the existing
  `Skips_the_network_entirely_when_a_client_certificate_is_already_stored`
  test, which skips via an entirely different branch.

### Fixed

- `installer/nsis/setup.nsi` called `SetShellVarContext all` only inside
  `Section "Uninstall"`, right before its one existing `$APPDATA`
  reference — its own comment incorrectly claimed this was already set in
  `.onInit`, but it never was. Harmless until now, since nothing during
  install itself ever referenced `$APPDATA` — but the new `/CACERT=` copy
  step above does, and without this fix would have silently resolved
  `$APPDATA` to the installing user's own per-user roaming profile instead
  of the shared `%ProgramData%` path `FileCaTrustStore` actually reads.
  Found while implementing the `/CACERT=` feature, not by a live install —
  fixed by adding the same call, independently, to both `.onInit` and
  `Section "UpdateWatch2 Agent" SEC_MAIN` (the same belt-and-suspenders
  pattern this script already uses for `SetRegView 64`), and correcting
  the now-inaccurate comment in the Uninstall section.

## [0.14.3] - 2026-09-09

### Fixed

- A fresh admin-issued re-issuance `RegistrationToken`, placed while this
  agent was still running fine on its old certificate (suspected
  compromise, not loss), used to sit unused indefinitely unless an admin
  also separately cleared `ClientCertificateThumbprint`/deleted the local
  certificate by hand — undocumented, and easy to miss, since
  `RegistrationWorker`'s certificate-present branch never looked at
  `RegistrationToken` at all. Recovery only happened once the server
  rejected the still-presented old certificate enough times for
  `HeartbeatWorker`'s self-heal (`updatewatch2-server#11`/
  `updatewatch2-agent#5`) to notice and drop it. `RegistrationWorker` now
  detects a config-store token that differs from the one it already
  consumed even while a certificate is still present locally, and
  proactively drops that old certificate (and clears the attached
  `SslOptions.ClientCertificates`) before registering with the new token
  — the same drop-and-recover move self-heal already performs after a
  rejection, just triggered directly by the token itself instead of
  waiting on a rejection. No config/registry field beyond
  `RegistrationToken` needs to be touched by hand anymore for this case;
  README/README.de now call out the difference for anyone still on an
  older build.

## [0.14.2] - 2026-09-09

### Fixed

- After an admin-mediated certificate re-issuance while this agent
  kept running, reconnection sometimes appeared to simply never
  happen — the log kept repeating "Alive heartbeat rejected with
  status Forbidden — this agent's certificate may no longer be
  trusted by the server." Root cause: `HeartbeatWorker`'s self-heal
  (`updatewatch2-server#11`/`updatewatch2-agent#5`) needs up to two
  heartbeat intervals to even decide the certificate is rejected, and
  `RegistrationWorker` then didn't notice the certificate was gone
  until its own next scheduled poll — up to
  `CertificateMaintenanceIntervalSeconds` later (15 minutes by
  default) — with nothing logged in between to say recovery was even
  pending. Recovery did eventually happen, just slowly enough that an
  admin watching for a few minutes reasonably concluded it wasn't
  working. New `Certificates/IRegistrationWakeSignal`: self-heal now
  wakes `RegistrationWorker` immediately instead of it waiting out
  that interval, collapsing the worst case from up to ~25 minutes
  down to effectively the self-heal threshold delay only. Also
  defensively clears the shared HTTP handler's stale in-memory client
  certificate (and wakes registration) even when the local
  certificate store is already empty by the time self-heal runs —
  e.g. a certificate removed by something other than this agent's own
  code — a case the previous "already gone, nothing to do" early
  return silently skipped.

## [0.14.1] - 2026-09-09

### Fixed

- The compiled Windows binary's Company/Product/Copyright file-version
  resource fields (Explorer's Details tab) were blank — `<Version>`
  already fixed File version/Product version (agent v0.12.3), but
  `<Company>`/`<Product>`/`<Copyright>` were never set at all. Now
  reads "Copyright (C) 2026 Thorsten Schröpel", matching the copyright
  line already used in README.md and the NSIS installer's own version
  resource. Live-verified: a real self-contained win-x64 publish's
  compiled `.exe` genuinely carries the string in its version
  resource, not just reasoned about from the csproj change.
- The NSIS installer never placed a copy of the license alongside the
  installed binary — only shown once on the license-acceptance page
  during setup, with no way to find it again afterward without
  re-running the installer. `LICENSE` is now installed as
  `LICENSE.txt` next to `UpdateWatch2.Agent.exe` in the install
  directory, and removed on uninstall. Live-verified: a real
  `makensis` build with the new `File` instruction compiles clean.

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
