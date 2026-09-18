# Changelog

All notable changes to the UpdateWatch2 Agent are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning follows [SemVer](https://semver.org/), starting at `0.x.x`
(beta) per the project's CLAUDE.md and reaching `1.0.0` — ending the beta
phase — at the user's explicit request. This file tracks the **agent**
version specifically — one of CLAUDE.md's four independent version
numbers (server, agent, transfer protocol, DB schema), which evolve on
their own schedules; a protocol bump is called out inline below where a
change caused one, but this changelog isn't that changelog.

## [1.0.19] - 2026-09-18

### Added

- **Native arm64 `.deb`/`.rpm` packages, closing #22** — raised after the same direct user question as #23 ("Wäre auch ein arm64-Release denkbar?"), whose own codebase-survey analysis found no blocker: `AptUpdateSession`/`DnfUpdateSession` and the Linux self-update escape hatch (`LinuxPackageApplier`'s `systemd-run --scope`) only ever shell out to architecture-independent tools, `dotnet publish -r linux-arm64` is a pure cross-compile flag exactly like the existing `linux-x64` one, and `fpm` already supports `--architecture arm64`. `release.yml`'s `linux-packages` job is now a matrix over `{linux-x64/amd64 on ubuntu-latest, linux-arm64/arm64 on ubuntu-24.04-arm}` (`fail-fast: false`, `continue-on-error: true` per leg, matching this job's existing "don't block the Windows release" precedent) — `ubuntu-24.04-arm` is a real, native GitHub-hosted arm64 runner, free for this public repo, chosen over QEMU emulation for the same "native beats emulation for a .NET SDK build" reasoning `updatewatch2-server#23`'s Docker multi-arch image already established. `fpm`'s architecture translation was verified directly in this session, not assumed: `--architecture amd64` already silently relied on fpm translating that to the RPM convention `x86_64` (the existing package), and `--architecture arm64` was confirmed the same session to translate to `aarch64` for RPM (and stays `arm64` for `.deb`, matching Debian's own convention) by building throwaway test packages of each and inspecting the resulting filenames. Artifacts are now `linux-deb-{amd64,arm64}`/`linux-rpm-{amd64,arm64}` (four, not two) so the two architectures' packages don't collide in the same workflow run; the `release` job downloads all four, each `continue-on-error: true` like before.
- **The job's own real value-add, taken up on the user's explicit choice when asked ("Full: native arm64 runner + smoke test" over a minimal cross-compile-only fix): a genuine smoke test now runs the actual staged binary — on both architectures, not just arm64 — before packaging it.** Since GitHub-hosted `ubuntu-24.04-arm` is native hardware (not emulated), this is real extra verification the whole Linux install path has never had, closing part of the "not live-verified" gap CLAUDE.md already discloses for it. On a fresh runner with no `/etc/updatewatch2/agent.conf`, the binary starts exactly like a genuinely fresh install (empty `ServerAddress`), and `RegistrationWorker` logs "Registration attempt failed" on its own retry loop rather than crashing — the CI step starts the binary, confirms it's still running after 8s (not crashed on startup), confirms that log line appears (proving real code ran, not just process launch), sends `SIGTERM`, and confirms a clean exit within 10s. Both the bash liveness/SIGTERM-handling logic and the expected log line were validated for real in this session before being written into the workflow — the log-line/exit-code logic against a throwaway fake script, and the real `win-arm64`-equivalent Linux cross-publish (`dotnet publish -r linux-arm64`) against the actual csproj, confirming a genuine self-contained build succeeds with no architecture-specific blocker.
- **A real mistake made and fixed during this same session, worth recording:** validating the smoke-test approach by directly running the compiled binary locally (rather than only in CI) turned out to be unsafe in this project's own dev sandbox specifically, because that sandbox (hostname `hpn54l`, the same host referenced throughout this file's other live-incident notes) already runs a real, registered agent as a systemd service, and `LinuxFileConfigStore`'s config path (`/etc/updatewatch2/agent.conf`) is a fixed, unoverridable absolute path — a local test process picks up the real config/certificate with no way to sandbox it. An overly broad `pkill -f UpdateWatch2.Agent` cleanup command (matching on the bare binary name, not a path-scoped pattern) killed the real systemd-managed process alongside the throwaway test one; since it exited cleanly (SIGTERM → graceful shutdown → exit 0), the unit's `Restart=on-failure` policy correctly did *not* treat that as a failure requiring a restart, so it stayed down until manually restarted (`systemctl start updatewatch2-agent`, confirmed healthy afterward — no config corruption, no leftover partial apt-cache files from the also-interrupted pre-download it had briefly kicked off). The `linux-packages` job's own smoke-test step avoids this entirely by construction — it only ever runs on a disposable, freshly-provisioned CI runner, never against a real host's `/etc/updatewatch2` — and the workflow file itself now carries an explicit warning comment against ever running this smoke test's approach against a real host by hand.

## [1.0.18] - 2026-09-18

### Added

- **Windows-on-ARM support: `release.yml` now publishes a second, native `arm64` installer alongside the existing `x64` one, closing #23** — raised after a direct user question ("Wäre auch ein arm64-Release denkbar? Erstelle eine Analyse."), whose own codebase-survey analysis found no blocker in the agent's own application code (no pinned .NET `RuntimeIdentifier`, no architecture-specific `PackageReference`) but flagged one genuinely new question: whether the NSIS installer itself needed to become ARM64-aware. It doesn't — `dotnet publish -r win-arm64` is a pure cross-compile flag exactly like the existing `win-x64` step (confirmed for real in this session: cross-published a genuine self-contained `win-arm64` build from this project's own Linux dev sandbox and verified the resulting `UpdateWatch2.Agent.exe` is a real `PE32+ ... ARM64` executable via `file`), and `setup.nsi`'s installer/uninstaller stub stays a plain 32-bit NSIS executable for both architectures — Windows-on-ARM has run x86 binaries under emulation since its very first release, and `SetRegView 64`/`$PROGRAMFILES64` already resolve to the native 64-bit registry view/Program Files location on any 64-bit Windows (ARM64 included) regardless of the calling process's own architecture, the exact mechanism this script already relies on to install correctly from a 32-bit stub on 64-bit x64 Windows today. `setup.nsi` gained a new `ARCH` define (default `x64`, backward compatible with every existing invocation that doesn't pass one) used only to name the output file (`UpdateWatch2Agent-Setup-<version>-<arch>.exe`) and compute `PUBLISH_DIR`'s own default (`..\..\publish\win-${ARCH}`) — no other line in the script changed. `windows-installer` now publishes and packages both `win-x64` and `win-arm64` sequentially in the same job (no matrix — there's no meaningful parallelism to gain from two cross-compiles that don't touch each other, and a single job lets both installers keep sharing the existing `windows-installer` artifact/its `UpdateWatch2Agent-Setup-*.exe` glob without touching the `release` job's download step at all). Both `makensis` invocations were run for real in this session (with placeholder publish-output files, since the actual `dotnet publish` output isn't needed to validate the script itself) and produced the correctly-named `-x64.exe`/`-arm64.exe` outputs; the default-ARCH (no `/DARCH=`) invocation was also re-confirmed to still produce `-x64.exe` unchanged, for backward compatibility with any existing manual/local build habit. **Not live-verified against a real Windows-on-ARM device** — no such host has ever been available to this project; same standing caveat as every other Windows-specific claim in this codebase (the WUApiLib COM integration above all), now extended to a second architecture rather than a new kind of gap.

## [1.0.17] - 2026-09-18

### Added

- **A new `AllowUnauthenticatedPackages` config setting (Linux only), default `false`, at the user's explicit request** ("Es fehlt im Linux-deb-Agent eine Option um '--allow-unauthenticated' einzuschalten. Diese sollte in die Config-Datei mit aufgenommen werden und standardmäßig ausgeschaltet sein. Gibt es eine ähnliche Option für dnf/rpm?") — closes the gap the `hpn54l` "unauthenticated packages" incident (CLAUDE.md, the `AptUpdateSession.DownloadAndInstallAsync` note) left open: that incident's real fix was importing the repository's missing GPG key on the host itself, but there was no way for an admin who's deliberately decided to trust an unsigned/local repository (e.g. an air-gapped mirror that will never be signed) to tell this agent to stop refusing it. When enabled, `AptUpdateSession` passes apt-get's own `--allow-unauthenticated` on both `DownloadAndInstallAsync` and `DownloadOnlyAsync`; `DnfUpdateSession` passes the dnf/yum equivalent, `--nogpgcheck` (there's no dnf/yum flag named identically to apt's — `--nogpgcheck` is the closest match: like `--allow-unauthenticated`, it disables signature verification on the transaction rather than requiring a signature it can't check), on the same two calls. Both `BuildInstallArgs`/`BuildDownloadOnlyArgs` static methods take a new `allowUnauthenticated` parameter, placed before the existing `--` end-of-options marker like every other flag those methods build, with real unit test coverage confirming the flag's presence/absence and its position relative to `--`. Deliberately local-only, not something the server can push the way `LogLevel`/the update-check cadence are — a real security-relevant trust decision (bypassing package authentication) an admin has to opt into on a given host's own config file, not something that should be settable fleet-wide from the admin UI. A `LogWarning` fires on every install/pre-download call while enabled, so it shows up in the agent's own log the same way other security-relevant states in this codebase (e.g. `PinnedServerCertificateValidator`'s TOFU acceptance) already do. `installer/linux/postinst.sh`'s starter config and both READMEs' configuration reference tables were updated to match.

## [1.0.16] - 2026-09-17

### Added

- **Real apt/dnf pre-downloading, closing the Linux gap in `IUpdateChecker.PreDownloadAsync` — at the user's explicit request ("Setze den Pre-Download auch für Linux um.")**, protocol bumped to `1.5.0`. `ILinuxUpdateSession` gains `DownloadOnlyAsync`, implemented for both real sessions: `AptUpdateSession` runs `apt-get -y --download-only dist-upgrade` (populates the apt cache without unpacking/installing); `DnfUpdateSession` runs `<dnf|yum> -y update --downloadonly` (`--downloadonly` is core to dnf, but requires the separate `yum-plugin-downloadonly` package on yum — if missing, the command fails and this reports it as an ordinary `PreDownloadResult` failure rather than crashing). Both always download everything currently pending — mirroring `WuaUpdateSession.DownloadOnly`'s own semantics — there's no selective/partial pre-download concept, unlike `DownloadAndInstallAsync`'s optional package-name scoping. `LinuxUpdateChecker.PreDownloadAsync` is no longer a no-op, wrapping the session call in the same try/catch shape `WindowsUpdateChecker.PreDownloadAsync` already uses. `AliveRequest`/`AliveResult` gain a second, independent `PreDownloadLinuxUpdatesEnabled` field alongside the existing Windows one — `HeartbeatWorker` resolves which one applies to this agent via a single `OperatingSystem.IsWindows()` check (the same un-abstracted pattern `OperatingSystemDescriber` already uses elsewhere in this codebase) before writing into the still-platform-agnostic `IPreDownloadPolicyState`, so `UpdateCheckWorker` and every `IUpdateChecker` implementation stay unaware there are two toggles on the wire at all. **Not live-verified against a real apt/dnf host** — same standing caveat `DownloadAndInstallAsync`'s own install half already carries (this project's dev sandbox is Debian-based and running a real download/install here would mutate it); only the pure argument-building halves (`BuildDownloadOnlyArgs`) have real unit test coverage.

## [1.0.15] - 2026-09-17

### Added

- **The server can now also push a per-agent alive-heartbeat interval override, alongside the existing LogLevel/update-check interval/jitter — at the user's explicit request** ("Mache bitte auch die Client-Einstellungen für den Alive-Intervall in dem Agent-Einstellungsdialog verfügbar."), protocol bumped to `1.4.0`. `Communication.AliveRequest` gains `ActualAliveIntervalMinutes` (this agent's own current value, sent every heartbeat for admin visibility, same reasoning as the other three); `AliveResult` gains `DesiredAliveIntervalMinutes` (the server's pushed value, null = no override). `HeartbeatWorker.ApplyPushedSettings` applies it the exact same way as the update-check interval/jitter fields already work: mutate the shared `AgentOptions` singleton, persist via `IAgentConfigStore.Save`. Live-applied for free, with genuinely zero extra plumbing needed — this very worker's own `ExecuteAsync` loop reads `options.AliveIntervalMinutes` fresh for its `Task.Delay` right after `ApplyPushedSettings` returns on the same tick, so a pushed change takes effect on the very next wait, not the one after. The new debug logging added in v1.0.14 for this same mechanism (config sent/received, config written) was extended to include this fourth field too, rather than left as a silent gap in exactly the diagnostics that release just added.

## [1.0.14] - 2026-09-17

### Added

- **Debug-level logging for the per-agent settings-push mechanism (CLAUDE.md), reported by the user as missing** ("Es fehlen noch diverse Debugmeldungen im Agent, u. a. Schreiben der Config, Empfangen der Config, Senden der Config, Änderung des LogLevels"). The existing HTTP-level debug lines around the `alive` call only ever said a request happened, not what per-agent-settings values were actually exchanged, which is what's needed to diagnose that specific mechanism. `ServerClient.SendAliveAsync` now logs the `Actual*` values it's about to send (LogLevel/UpdateCheckIntervalMinutes/UpdateCheckJitterSeconds) before the request, and the `Desired*` values it received back once the response is parsed. `HeartbeatWorker.ApplyPushedSettings` logs the old-to-new LogLevel transition when a server-pushed override actually changes it, and logs immediately before/after `IAgentConfigStore.Save(options)` writes the change to the local registry/config file.

## [1.0.13] - 2026-09-17

### Added

- **The server can now push a per-agent LogLevel/update-check-interval/jitter override that this agent enforces live and persists back into its own registry/config file — closing a gap that stood since `AgentOptions.LogLevel`'s own doc comment first admitted "pushed centrally from the server UI (not implemented yet)", and issue #21 — at the user's explicit request** ("LogLevel des Agents über den Server setzen - steht im Konzept - wurde aber nie umgesetzt... Änderungen am Server sollen auch auf die Agents zurückgespiegelt (Registry bzw. Configfile) werden. Änderungen an Registry bzw. Configfile sollen wiederum am Server zu sehen sein. Diese Logik soll für alle (auch spätere) Einstellungen am Server für den Agent gelten. Bei Konflikten siegt immer der Server."). `Communication.AliveRequest` gains `ActualLogLevel`/`ActualUpdateCheckIntervalMinutes`/`ActualUpdateCheckJitterSeconds` (this agent's own current, actually-effective values, sent every heartbeat regardless of whether a server override is active — purely for admin visibility, so a manual registry/config-file edit shows up server-side too); `AliveResult` gains the `Desired*` counterparts (null = no override, in which case the local file stays authoritative). "The server always wins on conflict" is implemented by unconditional apply, not a timestamp comparison: `HeartbeatWorker`'s new `ApplyPushedSettings` overwrites the live `AgentOptions` singleton whenever a non-null `Desired*` value differs from the current one — including a value a human just hand-edited locally — then persists via `IAgentConfigStore.Save(options)` (own try/catch, non-fatal; a failed disk/registry write still leaves the live value applied). `UpdateCheckIntervalMinutes`/`UpdateCheckJitterSeconds` are live-applied for free, with no extra plumbing: `UpdateCheckWorker.NextDelay()` already reads `options.*` fresh on every loop iteration, so mutating the shared singleton is all that's needed.
- **A pushed LogLevel change now takes effect immediately, without an agent restart, at the user's explicit request.** Replaced the old startup-only mechanism (`builder.Configuration["Logging:LogLevel:Default"]` + `SetMinimumLevel`, plus a Windows-only `Logging:EventLog:LogLevel:Default` write to override `AddWindowsService()`'s own hardcoded Warning floor on `EventLogLoggerProvider`) with a new `Configuration.ILogLevelState`/`LogLevelState` — a mutable singleton (same minimal volatile-field shape as the existing `IPreDownloadPolicyState`) two `AddFilter` delegates read from on every single logging call: `builder.Logging.AddFilter((_, level) => level >= logLevelState.Current)` for the generic floor, and `builder.Logging.AddFilter<EventLogLoggerProvider>((_, level) => level >= logLevelState.Current)` (registered after `AddWindowsService()`'s own, same "last rule of equal specificity wins" reasoning the previous fix already established) for the EventLog-specific one. Since `AddFilter` delegates are re-evaluated fresh on every logging call by design, `logLevelState.Update(...)` takes effect with **no** `IConfigurationRoot.Reload()` call anywhere — genuinely simpler than the server's own equivalent fix (`AdminSettingsStore.Apply`), for a different reason: this sidesteps the framework's config-binding path entirely instead of fighting it. **Live-verified before trusting this**, matching the same throwaway-console-harness methodology CLAUDE.md documents for the original agent LogLevel/EventLog fixes: a harness mirroring this exact `Host.CreateApplicationBuilder` + `appsettings.json` setup confirmed a `LogLevelState.Update()` call changed DEBUG-message visibility immediately, correctly starting from and overriding `appsettings.json`'s own `Logging:LogLevel:Default: "Information"`. The Windows-`EventLogLoggerProvider`-specific half keeps this codebase's standing "not live-verified against a real Windows host" caveat — same as everything else Windows-Event-Log-specific here; re-confirm a DEBUG-level message reaches Event Viewer, both at startup and after a live-pushed change, before trusting that half further.
- Protocol bumped to `1.3.0` (additive `AliveRequest`/`AliveResult` fields).

## [1.0.12] - 2026-09-17

### Added

- **The "a reboot is required" signal is now checked on every heartbeat (~5 min by default) instead of only as a side effect of the full update-search cycle (~4h by default) — at the user's explicit request ("Der Check, ob ein Neustart nötig ist, sollte öfter stattfinden."), after first confirming the check itself is cheap on every platform (Windows: one cached COM property read; apt: one `File.Exists`; dnf/yum: one local subprocess spawn — none touch the network) and that piggybacking it on the existing heartbeat wouldn't noticeably load the server.** New `IUpdateChecker.CheckRebootRequiredAsync`/`IWindowsUpdateSession.CheckRebootRequired`/`ILinuxUpdateSession.IsRebootRequiredAsync`, decoupling the existing reboot-detection logic (`WuaUpdateSession.IsRebootRequired`, `AptUpdateSession`'s `/var/run/reboot-required` marker check, `DnfUpdateSession`'s `needs-restarting -r`) from the full search it used to only ever run behind — Windows in particular no longer needs a full, comparatively expensive WUApiLib catalog scan just to read this one cheap property. `HeartbeatWorker` runs the new check on every tick (own try/catch, non-fatal), sending its result as a new, additive `rebootRequired` field on the `alive` heartbeat request (protocol bumped to `1.2.0`). A failed check reports `null`, never a confirmed `false` — the same discipline already established for the full update check (`UpdateCheckResult.Success`/`Failed()`) — so a transient failure can never overwrite the server's last-known-good state with a false negative. `UpdateCheckWorker`'s own existing full-search-derived reboot reporting is unchanged and continues to run alongside this — both read the same real OS state, so they only ever report different snapshots in time, not conflicting sources of truth. No dedicated new config interval — piggybacks on `AliveIntervalMinutes` directly, matching how every other "should be more responsive than the update-check cycle" signal (pre-download policy, self-update offers, cert-rotation-pending) already rides the heartbeat. Confirmed with the user: `needs-restarting -r`'s subprocess spawn also runs on every heartbeat uniformly with Windows/apt (a real ~48x frequency increase over the old cadence) — accepted as negligible rather than special-cased with a lighter interval, since each spawn is still sub-second and local-only. **Not live-verified against a real target host** — same standing caveat as every other WUApiLib/dnf-specific finding in this codebase.

## [1.0.11] - 2026-09-17

### Fixed

- **Windows Event Viewer showed two different sources for this agent's log entries — "Service stopped/started successfully." under `"UpdateWatch2 Agent"` (space), everything else under `"UpdateWatch2.Agent"` (dot) — reported by the user directly, asking for the dot form to become the single standard everywhere.** Decompiling the actual `Microsoft.Extensions.Hosting.WindowsServices.dll` shipped with this project (no Windows host was available to observe this live) found a genuine dead-code bug plus a completely separate mechanism behind the two sources: `AddWindowsService()` (`Program.cs`) auto-registers its own `EventLogLoggerProvider`, whose source defaults to `IHostEnvironment.ApplicationName` — the entry assembly name, `"UpdateWatch2.Agent"` (dot), since this project sets no `<AssemblyName>` override. This agent *also* used to call `builder.Logging.AddEventLog(new EventLogSettings { SourceName = "UpdateWatch2 Agent" })` explicitly — but that call registers its own `EventLogLoggerProvider` instance under the exact same `TryAddEnumerable` key the auto-registered one already claimed, so it was silently dropped as a duplicate and **never took effect at all**; every `ILogger<T>`-routed message was already going to the dot source, just not for any reason this code actually controlled. Removed that dead call. Separately, the "Service stopped/started successfully." messages aren't from this codebase's own logging at all — they're `System.ServiceProcess.ServiceBase`'s built-in `AutoLog` mechanism (`WindowsServiceLifetime : ServiceBase`, `AutoLog` defaults to `true`, never disabled here), whose own lazily-constructed `EventLog` object independently defaults its `Source` to `ServiceBase.ServiceName` — `"UpdateWatch2 Agent"` (space), which must stay unchanged since it has to keep matching the SCM-registered service name from `installer/nsis/setup.nsi`. Fixed, at the user's explicit choice between redirecting vs. disabling these messages, by redirecting: `serviceBase.EventLog.Source = "UpdateWatch2.Agent"` is set explicitly right before `host.Run()`, changing only which Event Log source that one internal `EventLog` object writes to, without touching `ServiceName`/the actual registered service identity at all. The two remaining raw `EventLog.WriteEntry("UpdateWatch2 Agent", ...)` calls (the shutdown-timeout-warning and unhandled-exception crash handlers) were also switched to the dot source for full consistency. **Not live-verified against a real Windows host/Event Viewer** — same standing caveat as every other Windows-Event-Log finding in this codebase; re-confirm both messages actually appear under the dot source, and that service start/stop still works at all, before trusting this further.

## [1.0.10] - 2026-09-16

### Added

- **Windows agents can now proactively download pending Windows Updates ahead of an actual install trigger, gated by a new fleet-wide admin toggle — at the user's explicit request ("Gibt es die Möglichkeit die Windows-Updates im Vorfeld schon herunterladen zu lassen? Am besten über eine Option in den Einstellungen ein- und ausschaltbar machen.").** New `IUpdateChecker.PreDownloadAsync`/`IWindowsUpdateSession.DownloadOnly` (`WuaUpdateSession`) — a separate method from the existing fused `DownloadAndInstall`, deliberately not refactored to share logic with it, to avoid touching already-shipped, only-structurally-verified COM code for a testability benefit that doesn't apply here anyway (this class is excluded from Linux CI regardless). `DownloadOnly` re-searches with the same criteria `SearchForUpdates`/`DownloadAndInstall` already use, skips anything `IUpdate.IsDownloaded` already reports as downloaded (the first real use of that WUApiLib property in this codebase — WUA persists this as OS state, independent of this agent's own process lifetime, which is what keeps re-running this on every periodic check cheap once caught up), and checks the aggregate `IDownloadResult.ResultCode` rather than `DownloadAndInstall`'s own per-update result checking — that finer precision matters there because it decides exactly what gets installed; here a failure just means "retry on the next periodic check." Never touches `CreateUpdateInstaller()` at all. `LinuxUpdateChecker`/`NoOpUpdateChecker` implement the new interface member as a harmless no-op (Windows-only for this feature's first version, per explicit user decision) — no warning logged, since this is a best-effort optimization a fleet-wide server toggle opts an agent into, not something every platform is expected to actually do yet.
- Triggered from `UpdateCheckWorker`, right after its own periodic search+report succeeds, not from `HeartbeatWorker` — avoids an extra WUA COM search on `HeartbeatWorker`'s much shorter ~5-minute cadence just to check whether there's anything new to pre-download. The server's toggle value itself arrives on that shorter heartbeat cadence though (new additive `preDownloadWindowsUpdatesEnabled` field on the `alive` response, protocol bumped to `1.1.0`), so a new shared `UpdateCheck.IPreDownloadPolicyState`/`PreDownloadPolicyState` singleton bridges the two: `HeartbeatWorker` writes the latest known value on every successful heartbeat (unconditionally, so turning the setting off also propagates within one interval, not just turning it on), `UpdateCheckWorker` reads it right after a successful report to decide whether to also call `PreDownloadAsync`. A pre-download failure is logged as a warning and never affects the search/report that already succeeded on the same tick (own try/catch, same convention every other `HeartbeatWorker` maintenance check already uses).
- **Not live-verified against a real Windows Update Agent** — same standing caveat this codebase already carries for `WuaUpdateSession.SearchForUpdates`/`DownloadAndInstall`; re-verify the `IsDownloaded`-based skip logic and the aggregate `ResultCode` check on an actual Windows host before trusting this further.

## [1.0.9] - 2026-09-16

### Added

- **Transient transport-level failures on outbound server calls (Alive, ReportUpdates, and every other `ServerClient` call) are now retried up to 3 attempts, with a 2s delay in between, before being given up on — at the user's explicit request ("Anscheinend laufen manche Anfragen (Alive, Updates, etc.) netzwerkbedingt in einen 15sekündigen Timeout. Können diese einfach maximal 3 mal wiederholt werden, bevor sie verworfen werden?"), who also flagged this as a plausible contributor to `updatewatch2-agent#20`'s TLS-abort reports.** Previously, any transport-level failure (a dropped connection, a TLS handshake that never completed, a request that hung until `HttpClient`'s own timeout fired) failed the whole call outright, and the calling worker (`HeartbeatWorker`/`UpdateCheckWorker`) simply waited out its full interval — minutes for a heartbeat, potentially hours for an update check — before trying again; a single transient network blip could look, from the admin UI, like an agent had gone silent for a long stretch. `ServerClient`'s new `WithRetryAsync` helper wraps every outbound HTTP call and retries on a response-less `HttpRequestException` (`StatusCode: null` — thrown before any response was ever received), `IOException`, `SocketException`, and `TaskCanceledException` (also covers `HttpClient`'s own internal request timeout firing) — deliberately never on a real HTTP error response (`EnsureSuccessStatusCode()`'s `HttpRequestException` always carries a non-null `StatusCode` once a response was actually received), so this doesn't touch the documented transient-409 registration-handoff case or a genuine 401/403 certificate rejection, both of which still need their own caller-side handling rather than a blind retry. `DownloadFileAsync` wraps its entire request-and-copy, not just the initial connect, since a self-update package download can just as easily fail mid-stream — `File.Create` truncates on every attempt, so a retry always starts the destination file over cleanly. Real unit test coverage (`ServerClientTests`) confirms a transient failure that resolves within 3 attempts succeeds, a persistent one gives up after exactly 3 attempts, and a real HTTP error response is never retried at all. **Whether this actually resolves the specific ~15s timeouts the user is seeing, or `updatewatch2-agent#20`'s TLS aborts, is not confirmed** — those still have an unknown root cause (see issue #20's own investigation notes); this makes any transient occurrence of either self-heal within the same tick instead of costing a full interval, which is worth having regardless of the underlying cause, but isn't itself a diagnosis or a guaranteed fix.

## [1.0.8] - 2026-09-16

### Fixed

- **DEBUG-level log messages (including all of v1.0.4's new COM/HTTP/shell/registry logging) never reached Event Viewer, even with `LogLevel` set to `DEBUG` — reported by the user directly ("der Windows-Agent den LogLevel ignoriert... auch auf DEBUG liefert er keinerlei Einträge für die neuen COM-Logmeldungen").** Root cause: `Microsoft.Extensions.Hosting.WindowsServices`' `AddWindowsService()` auto-registers a hardcoded `Warning`-level floor filter scoped specifically to `EventLogLoggerProvider` whenever the process is actually running as a Windows Service — a real, if obscure, built-in .NET hosting behavior meant to keep routine chatter out of the shared Windows Event Log by default. Being provider-specific, that filter is *more specific than* — and so silently overrides — this agent's own generic `Logging:LogLevel:Default` write, regardless of registration order; `SetMinimumLevel()` never touches a provider-specific rule at all. The practical effect: only Information-and-above could ever reach Event Viewer from this agent, no matter what `LogLevel` was actually configured to. This also corrects this exact file's own prior claim ("writing the value directly into configuration is what the console/EventLog providers' filter actually respects") — true for the console provider, not for EventLog when running as a service, which is genuinely different from the server-side finding it was modeled on. Fixed two ways, deliberately redundant given the uncertainty of not being able to test this live: a configuration-bound `Logging:EventLog:LogLevel:Default` write (reactive the same way the generic default already is) plus a code-level `builder.Logging.AddFilter<EventLogLoggerProvider>(...)` call registered after `AddWindowsService()`'s own, so "last rule of equal specificity wins" resolves in our favor either way. **Not live-verified against a real Windows Event Viewer in this session** — same standing caveat this project already carries for everything else Windows-Event-Log-specific; re-confirm that a DEBUG-level message from this agent's own COM/HTTP logging actually appears in Event Viewer with `LogLevel=DEBUG` before trusting this further.

## [1.0.7] - 2026-09-16

### Fixed

- **A failed update check used to be reported to the server identically to "genuinely found zero updates" — reported by the user directly (a `WindowsUpdateChecker` COMException, HRESULT `0x8024401C` — WUApiLib's own `WU_E_PT_HTTP_STATUS_REQUEST_TIMEOUT`, right after installing updates and rebooting — after which "the agent's status stopped updating").** `WindowsUpdateChecker.CheckAsync`/`LinuxUpdateChecker.CheckAsync`'s catch blocks, and `DnfUpdateSession.SearchForUpdatesAsync`'s own internal `check-update`-failure branch, all returned an empty `UpdateCheckResult` on failure — indistinguishable from a real search that genuinely found nothing. `UpdateCheckWorker.CheckAndReportNowAsync` had no way to tell the difference and dutifully reported it to the server every time, which — since the server merges/replaces an agent's pending-updates list against whatever was just reported — silently **wiped out any real pending updates the server already knew about and falsely cleared `RebootRequired`**, even though nothing had actually changed; the agent simply didn't know. Fixed with a new `UpdateCheckResult.Success`/`ErrorDetail` (default `Success = true`, so every existing genuine-result call site is unaffected) — a failed check now uses `UpdateCheckResult.Failed(...)`, and `CheckAndReportNowAsync` skips the report entirely when `Success` is false, logging a warning instead and leaving the server's last-known-good state — including `LastUpdateCheckAt` (v1.3.7), which now only advances on an actual successful report — untouched until a check genuinely succeeds again. A `LastUpdateCheckAt` that visibly stops moving is now itself the honest diagnostic signal an admin needs, rather than a silently-wrong "0 pending updates."
- The specific WUA error reported (`0x8024401C`) is itself a transient network condition (Windows Update's own search request timing out — plausible right after a reboot, before the network is fully back up), not a bug in this agent's own code; this fix doesn't prevent that from happening, it prevents it from corrupting the server's picture of this agent's actual state when it does.

## [1.0.6] - 2026-09-16

### Fixed

- **The Linux agent crashed on startup on a fresh host with no ICU library installed, printing "Couldn't find a valid ICU package installed on the system" and `Environment.FailFast`ing out of `System.Globalization.CultureInfo`'s static initializer — reported by the user directly, having worked around it by manually installing `libicu76`.** A self-contained publish still dynamically loads the *system's* ICU library at startup unless told otherwise; the exact package name needed (`libicu76`, `libicu72`, `libicu70`, `libicu67`, `icu-libs`, ...) varies by distro and release, so there's no single `Depends:` this project's `.deb`/`.rpm` packaging could declare that would be correct on every target. Fixed instead by enabling `<InvariantGlobalization>true</InvariantGlobalization>` in `UpdateWatch2.Agent.csproj` — .NET's own standard, documented answer for exactly this class of headless-service deployment — which removes the ICU dependency entirely rather than trying to satisfy it. Confirmed safe for this codebase specifically, not just in general: grepped for every non-`Ordinal`/non-`Invariant` string comparison, culture-sensitive `ToUpper`/`ToLower`, and culture-formatted `ToString` call — none exist anywhere in this codebase, so Invariant mode changes nothing observable here. Live-verified in this session (not just reasoned about): published self-contained for `linux-x64`, confirmed `runtimeconfig.json` actually carries `"System.Globalization.Invariant": true`, and ran the resulting binary for real, confirming it starts and stays up cleanly with no error output. The specific "ICU genuinely absent" crash itself was not re-reproduced in this session's sandbox (it already has several ICU versions installed) — Invariant Globalization Mode's effect of skipping the ICU P/Invoke calls entirely at CLR startup is a structural, well-established .NET runtime guarantee rather than something that needs an ICU-less environment to re-confirm, but flagging the gap honestly rather than claiming more than was actually observed.

## [1.0.5] - 2026-09-16

### Fixed

- **A self-update that couldn't stop the old service in time left it deleted but never recreated, requiring manual intervention — reported by the user directly ("Der Agent ist beim Self-Update hängen geblieben und hat dabei den Dienst gelöscht, aber nicht neu angelegt.").** `installer/nsis/setup.nsi`'s upgrade sequence used to `sc.exe delete` the old service unconditionally after only a fixed `Sleep 1000`, then overwrite the binary — if the old process hadn't actually released its file handle by then (it crashed instead of exiting cleanly, or legitimately needed up to `HostOptions.ShutdownTimeout`'s own 30s), the `File` instruction failed hard and silently in `/S` mode, aborting the script between the already-executed delete and the never-reached recreate. Fixed with a real bounded poll for the service reaching `STOPPED` (replacing the fixed sleep) that aborts — restarting the still-present old service first — *before* the file copy if the old process never stops in time, instead of failing silently after; `sc.exe delete` moved to immediately before `sc.exe create` (i.e. after the file copy has already succeeded, not before it), so a copy failure now leaves the *old* service registration intact (self-heals via its own `start= auto` on the next reboot) instead of gone entirely; every `sc.exe` step's exit code is now checked and written to a new, persistent `$INSTDIR\install.log` (append mode, across upgrades) — a silent install otherwise leaves no trace of what happened, which is exactly what made this incident hard to diagnose; a poll for the new service reaching `RUNNING` after `sc.exe start`, logged if it never does; and a new `.onInstFailed` callback as a belt-and-suspenders extra restart attempt on any install failure. The `Uninstall` section's own identical fixed-sleep weakness got the same poll-loop fix, lower priority (no file-copy step there to protect, so this is purely about giving the old process a real chance to exit before the subsequent `Delete` calls).
- **`Program.cs`'s `host.Run()` catch was narrowly scoped to `OperationCanceledException` only** — investigating the same incident found that any *other* exception type escaping the same shutdown path was, and without this fix still would be, completely unprotected, crashing the process the same way the already-fixed (v0.15.1) OCE case used to. Broadened to a second, separate `catch (Exception ex)` branch — deliberately not folded into the OCE branch, since the right response differs: an unanticipated exception must still exit with a **nonzero** code (`Environment.ExitCode = 1`), not the OCE branch's implicit clean exit 0, since both `installer/linux/updatewatch2-agent.service`'s `Restart=on-failure` and `setup.nsi`'s `sc.exe failure ... actions=restart/...` only recover a process that exits nonzero or terminates abnormally, never one that exits cleanly — uniformly reusing the OCE branch's "log and exit 0" for every exception type would have silently disabled that recovery mechanism for a genuinely unexpected crash. Logs the actual exception type/message/stack trace to the Event Log at `Error` (not `Warning`) level, since unlike the OCE case there's no fixed, already-understood explanation to fall back on.
- Investigated whether `WindowsInstallerApplier.cs` (the agent-side self-update trigger) or `AgentSelfUpdateService.cs` needed a corresponding change — concluded no: the installer's fire-and-forget trigger is correct as-is, since the launched installer's own job is to stop and replace this very process, so waiting on it from inside the soon-to-be-killed launcher would risk a self-inflicted deadlock (the same class of problem the Linux `LinuxPackageApplier`/`systemd-run --scope` fix already exists to avoid, via a different mechanism appropriate to that platform). A genuine self-update outcome acknowledgement back to the server (there currently is none, silent or not) is a real but separate, larger protocol-level feature, deliberately left out of this fix's scope.
- **Not live-verified against a real Windows host** — same standing caveat this project already carries for the rest of the NSIS install/uninstall path and the WUApiLib integration. The new `setup.nsi` control flow *was* compile-checked for real with `makensis` (installed in this sandbox, the same version `.github/workflows/release.yml`'s `windows-installer` job uses), catching any real structural error, but its actual runtime behavior against a real Service Control Manager — the `sc query | find | find` exit-code pattern, whether the reordered delete-then-create genuinely leaves the old service in the described surviving state on a real `File` failure, whether a nonzero `Environment.ExitCode` actually triggers `sc.exe failure`'s configured restart action, and `.onInstFailed`'s exact firing behavior after a hard mid-`Section` abort — remains unconfirmed. Re-verify on a real Windows host before trusting this further: (1) a fresh install; (2) a normal upgrade over a healthy, cleanly-stoppable agent; (3) an upgrade where the old process's file handle is artificially held open past the stop-poll's timeout, confirming the installer aborts before the file copy and the old service survives/restarts; (4) a forced `sc.exe create` failure; (5) a genuinely unrelated (non-OCE) exception, confirming it now exits nonzero and is actually restarted by `sc.exe failure` (Windows) / `Restart=on-failure` (Linux — cheap to confirm independently of the Windows half, since this is cross-platform code).

## [1.0.4] - 2026-09-16

### Added

- **Much more DEBUG-level logging around every external interface this agent talks to, at the user's explicit request ("Mehr Debug-Infos für die COM-Aufrufe und sonstige Schnittstellen-Aufrufe (Level DEBUG)").** Scoped to the agent (the server's own external interfaces — LDAP, SMTP, the GitHub API — were explicitly out of scope for this request). Covers:
  - **COM (WUApiLib)** — `WuaUpdateSession` now logs each search/download/install boundary at DEBUG: the search criteria and result count, the selected-vs-pending counts for an install, each EULA acceptance, each per-update download result code, the install `ResultCode`, and every `Microsoft.Update.SystemInfo.RebootRequired` check.
  - **HTTP calls to the server** — `ServerClient` now logs a DEBUG line before and after every call (register, alive, report-updates, install-ack, reboot-ack, renew, version check, CA-certificate fetch, file download), including the route and the resulting status code.
  - **Linux shell-outs** — `ShellCommand.RunAsync` gained an optional `logger` parameter, now passed by every `AptUpdateSession`/`DnfUpdateSession` call site, logging the full command line before running and the exit code plus elapsed time after.
  - **Windows registry/certificate store access** — `WindowsClientCertificateStore` (X509Store open/find/add/remove) and `WindowsRegistryUpdatePolicyStore` (the `NoAutoUpdate` policy read/write added in v1.0.3) now log each operation at DEBUG.
  - Purely additive logging — no behavior change; all 133 existing tests pass unchanged. Not live-verified against a real Windows host for the COM/registry/certificate-store pieces — same standing caveat this project already carries for that code.

### Fixed

- **Found and fixed a pre-existing, unrelated CI-only test flake while pushing this change: `PinnedServerCertificateValidatorTests.CreateCaAndLeaf` could throw `The requested notAfter value ... is later than issuerCertificate.NotAfter`.** The root and leaf certificates' `NotAfter` were each computed from a separate `DateTimeOffset.UtcNow` call; straddling a second boundary between the two (rare locally, common enough on a CI runner to actually trip this release) meant the leaf's requested `notAfter` could round up past the root's already-stored, second-precision `NotAfter`. Fixed by capturing `now` once and reusing it for both, with the root additionally given a full extra day of margin over the leaf so the two can never collide regardless of rounding on either side. Confirmed with 5 back-to-back local runs after the fix, not just once.

### Fixed

- **Windows machines sometimes automatically rebooted after updates were installed, despite CLAUDE.md's explicit "update installation never triggers a reboot itself" rule — reported by the user directly ("Nach den Windows-Updates wird teilweise ein automatischer und unerwünschter Neustart durchgeführt").** Confirmed this agent's own code was not the cause: `WuaUpdateSession.DownloadAndInstall` never acts on `RebootRequired`, and neither the server nor this agent ever sets a pending reboot request except through an explicit admin action (`AgentsController.Reboot`). The actual cause is Windows' own separate, native "Automatic Updates" client — distinct from the Windows Update Agent (WUApiLib) COM API this agent uses directly to search/download/install — which runs independently by default and, per its own default consumer settings, can download, install, and reboot updates entirely on its own schedule regardless of what this agent does. Fixed by disabling that native client at the OS policy level (`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoUpdate=1`, the standard, long-documented Group Policy equivalent of "Configure Automatic Updates: Disabled" — the same mechanism a WSUS- or Intune-managed fleet already relies on), applied once at agent startup via a new `WindowsUpdatePolicyEnforcer`. Does not affect the separate WUApiLib COM API this agent's own search/download/install already uses. If a domain Group Policy also manages this same key, that policy wins on its next refresh regardless of this local write — a no-op safety net in that case, not a conflict. Follows the same testable-orchestrator/untestable-OS-action split as `WindowsUpdateChecker`/`WuaUpdateSession` (`WindowsUpdatePolicyEnforcer` behind a new `IWindowsUpdatePolicyStore` seam, with real test coverage against a hand-written fake store on this project's Linux CI; the real registry-touching `WindowsRegistryUpdatePolicyStore` cannot be exercised there). **Not live-verified against a real Windows host** — same standing honesty caveat this file already carries for the rest of the WUApiLib integration; re-confirm on a real machine (native Automatic Updates genuinely disabled, this agent's own controlled install/reboot flow unaffected) before relying on this further.

## [1.0.2] - 2026-09-16

### Security

- **Closed a TOCTOU window in `LinuxFileConfigStore.Save`/`LinuxClientCertificateStore.Save`, found by an automated security review.** Both used to write the file's full contents first (`File.WriteAllText`/`WriteAllBytes`) and only restrict its permissions to owner-only afterward (`File.SetUnixFileMode`) — between those two calls, the file briefly exists on disk with whatever mode its own creation leaves it at (the process's default reduced by umask, commonly world-readable, confirmed by hand: umask `0022` produces `644`), exposing the config file's `RegistrationToken` bearer secret or the client certificate's private key to any local user racing that window (e.g. an inotify watch on the directory). Fixed by creating the file with the restrictive mode already applied via `FileStreamOptions.UnixCreateMode`, closing the window entirely rather than narrowing it after the fact — the explicit `SetUnixFileMode` call is kept too, now a no-op on a freshly-created file, purely to migrate a file an older binary already created with the wrong mode. Existing test coverage (`LinuxFileConfigStoreTests`/`LinuxClientCertificateStoreTests`, asserting the final permission mode) still passes; a TOCTOU window itself isn't something a fast unit test can directly observe, so this rests on the fix's own reasoning plus that unchanged final-state coverage, not a new regression test.

### Changed

- **README/README.de "Project status" badge and callout still said "v1.0" — at the user's explicit request, both now say "Stable"/"Stabil"** (mirrored in the server repo's own README pair too, which carries the identical badge).

## [1.0.1] - 2026-09-13

### Fixed

- **NSIS installer's "Server Connection" page: the server-address label's text wrapped onto a second line that the address text field then half-covered — reported by the user directly ("Der Text ... wird in eine 2. Zeile umgebrochen. Dadurch verdeckt das Textfeld die 2. Zeile halb.").** `installer/nsis/setup.nsi`'s `ServerConfigPageCreate` gave that label only 12u of height (one line), matching every other single-line label on the page, but its own text ("Server address (hostname or IP) — must match the server's UPDATEWATCH2_SERVER_HOSTNAME:") is long enough to wrap at the dialog's default width — the page's own "Leave blank..." label already accounted for wrapping onto two lines with 24u, confirming 12u/24u as this page's established one-line/two-line convention. Fixed by giving the address label 24u too and shifting every control below it (the port label/field, the "leave blank" hint) down by the resulting 12u, keeping each label/field pair's own spacing unchanged. Not live-verified against a real Windows install (no Windows host available in this session) — same standing caveat this project already carries for the rest of the NSIS install/uninstall path; re-confirm the page renders correctly on a real target host before relying on this further.

## [1.0.0] - 2026-09-13

### Changed

- **Ends the beta phase, at the user's explicit request ("Ich würde die Beta-Phase gern beenden. Kannst du alle Versionen auf v1.0.0 setzen?").** This repo's agent version (`VERSION`, `AgentVersion.cs`) and transfer-protocol version (`Protocol/ProtocolVersion.cs`) both move to `1.0.0`, matched by the server repo's own server, protocol, and DB-schema versions moving to `1.0.0` too — all four of CLAUDE.md's independently-tracked version numbers reaching this milestone together, deliberately overriding the normal "these evolve independently" rule for this one occasion.
- README.md/README.de.md no longer describe the project as "Beta" — the status badge and callout now read `v1.0`/`✅`. The substantive caveats those callouts already carried (the real Windows Update API integration, the Linux `dnf`/`yum` update path, and the Windows installer's install/uninstall behavior not yet verified against a real target host) are unchanged and still called out explicitly — reaching `1.0.0` is a versioning/maturity milestone, not a claim that those specific, honestly-flagged gaps have been closed.

## [0.16.3] - 2026-09-13

### Fixed

- **v0.16.2's fix for the self-update path-traversal finding was itself incompletely closed against a foreign-host redirect — found by an automated follow-up review of that same commit, not by a fresh manual pass.** That release added a blacklist-style guard rejecting `DownloadUrl` when `Uri.TryCreate(..., UriKind.Absolute, out var parsed)` succeeded with a non-empty `Host`. Confirmed by hand: a protocol-relative reference (`//attacker.example/payload.exe`, no scheme) resolves against a real base URI by replacing just the authority and keeping the base's scheme — `new Uri(new Uri("https://real-server:8796/"), "//attacker.example/payload.exe")` yields `https://attacker.example/payload.exe`, a genuine foreign-host redirect — while `Uri.TryCreate` on that same bare string *also* happens to report a non-empty `Host` (parsed as `file://attacker.example/...`), meaning this specific case was actually still caught; the real problem is that this was only ever confirmed by testing individual cases one at a time, an inherently open-ended exercise against a string parser with this many edge cases (backslash variants, encoded authority delimiters, ...), not a fix that makes the bypass structurally impossible.
- Replaced the blacklist check with a structural fix: `AgentSelfUpdateService.ApplyAsync` no longer fetches the server-supplied `DownloadUrl` string at all. It extracts and sanitizes the bare filename exactly as before (`Path.GetFileName` after decoding, confined to the staging directory), then rebuilds the actual fetch target from *only* that already-sanitized filename via a new `Protocol.AgentApiRoutes.UpdateDownload(fileName)` — the identical route template the server itself builds `DownloadUrl` from. The fetch target is now same-origin-relative by construction; there is no longer a check to bypass, because the original untrusted string is never used for the fetch regardless of what it contains.
- Real test coverage added asserting the actual fetched URL for a foreign-host `http://` URL and a protocol-relative `//` reference — both now resolve to the correct, safe, reconstructed same-server path, confirmed against the fake `IServerClient`'s recorded `LastDownloadUrl`, rather than only asserting an outright rejection.

## [0.16.2] - 2026-09-13

### Fixed

- **Path traversal in the self-update download path (High) — found by a full, non-diff-scoped security review.** `SelfUpdate/AgentSelfUpdateService.ApplyAsync` derived the downloaded file's on-disk name from a server-supplied `AgentUpdateAssetOffer.DownloadUrl` via `Uri.UnescapeDataString(asset.DownloadUrl.Split('/').Last())` — the split ran *before* decoding, so a percent-encoded `/`/`..` inside the offered filename survived the split untouched and only became a real path separator afterward, and `Path.Combine` discards the staging directory entirely once the result turns out to be rooted. Combined with a matching server-side gap (unsanitized GitHub release asset names — see the server repo's own CHANGELOG entry for this same finding), a malicious/compromised release on the pinned upstream repo could have made every connected agent write an attacker-controlled file to an arbitrary path — on Windows, `WindowsInstallerApplier` then executing it directly. Fixed by sanitizing the decoded filename with `Path.GetFileName` and verifying the resolved path stays inside the staging directory before ever downloading to it, plus (defense in depth) refusing a `DownloadUrl` that names a foreign host at all — a legitimate offer is always a same-server-relative path. Real test coverage added for both the traversal case (confirms the download is safely confined and renamed, not just rejected outright) and the foreign-host case.

## [0.16.1] - 2026-09-12

### Fixed

- **Both reboot delays shortened to a matching, second-precise 10s, at the user's report that Windows' 60s wait was needlessly long.** Comparing `WindowsAgentRebooter`'s `/t 60` against `LinuxAgentRebooter`'s `+1` made the two look wildly different (one "60", one "1") — they were actually already the same delay, just in different units: `shutdown`'s `+m` syntax is whole *minutes*, so `+1` meant 1 minute (60s), not 1 second. Neither platform could go shorter than that misreading suggested was already happening on Linux, since `shutdown -r +m` has no sub-minute granularity at all (only whole minutes, or `now` — immediate, which would race the agent's own `AcknowledgeRebootAsync` call). `LinuxAgentRebooter` now schedules the reboot via `systemd-run --on-active=<seconds> -- systemctl reboot` instead of `shutdown -r +<minutes>` — a one-shot systemd timer with plain-seconds granularity — so both platforms now share the exact same 10-second constant (comfortably more than the ack call ever takes, nowhere near the minute-plus either platform was actually stuck at before). The one real trade-off: `systemd-run` has no wall-broadcast-message equivalent, so the "reboot requested by an administrator" message users on the Linux machine used to see is gone — acceptable for a headless server agent, and never an essential part of the feature to begin with.
- Test coverage updated: `WindowsAgentRebooterTests`/`LinuxAgentRebooterTests`' `BuildRebootArgs` assertions now cover the new 10s constant and (Linux) the new `systemd-run` argument shape.

## [0.16.0] - 2026-09-12

### Added

- **An admin can now remotely reboot the agent's machine from the admin UI, at the user's explicit request ("Es fehlt noch die Funktion um den Client über die Oberfläche neu starten zu können") — protocol `0.11.0`.** Reboots the whole machine, not just this agent's own service process (an earlier same-day version of this feature restarted only the service — corrected before ever being tagged, once the user clarified they meant a full machine reboot). Distinct from an OS-update install, which per CLAUDE.md's rule never triggers a reboot itself — this is exactly that admin decision being carried out. Delivered exactly like a remote install trigger: the server surfaces a pending reboot request as an additive `rebootRequested` field on the existing `alive` heartbeat response, `HeartbeatWorker` reacts to it inline on that same tick (last, deliberately, after everything else the tick would otherwise do — install, self-update, cert renewal — since a successful reboot ends this process shortly after), and a new `POST .../reboot-ack` call acknowledges the outcome. New `Reboot/IAgentRebooter` interface, `Reboot/Windows/WindowsAgentRebooter` and `Reboot/Linux/LinuxAgentRebooter` implementations, chosen in `Program.cs` the same way every other platform seam already is.
- **`IAgentRebooter.RequestReboot()` only ever schedules the platform's reboot command and returns almost immediately — it never waits for the machine to actually go down.** This is what makes it safe for `HeartbeatWorker` to still reach the server with `reboot-ack` in the window between scheduling the reboot and the machine actually going down; if that ack itself fails to land, the server simply reports the same reboot as still pending, and this agent's first heartbeat after coming back up could see it again — the same accepted harmless-but-wasteful edge case this codebase already tolerates for a failed install acknowledgement.
- **Windows**: `WindowsAgentRebooter` runs the built-in `shutdown.exe /r /t 60 /c "<message>"` — genuinely simpler than the service-restart mechanism it replaced, which needed a detached helper script since stopping this agent's own service via SCM would have terminated this process before it could run a follow-up "start" command itself. `shutdown.exe` needs none of that: it only schedules the reboot with the OS and exits immediately, regardless of what happens to the calling process afterward. Argument-building pulled into a testable `BuildRebootArgs` static method. **Not live-verified** — same standing honesty caveat as every other Windows-only class in this codebase (no Windows host available).
- **Linux**: `LinuxAgentRebooter` runs systemd's own `shutdown -r +1 "<message>"` — schedules the reboot and returns, the same shape as the Windows path. Deliberately NOT the detached `systemd-run --scope` dance `SelfUpdate/Linux/LinuxPackageApplier` needs for its own dpkg/rpm install step: that class has to keep `dpkg`/`rpm` itself running through this very unit being stopped and restarted, mid-transaction; scheduling a reboot has no such concern, since nothing in this agent's own process or cgroup needs to survive for it to happen — systemd's PID 1 carries it out independently. **Not live-verified either, deliberately** — unlike this same feature's other Linux mechanisms in this codebase, actually rebooting the shared dev sandbox this project is otherwise verified against was out of scope for a routine pass; only `BuildRebootArgs` has real test coverage.
- **The agent now also self-reports its machine's boot time on every heartbeat**, so an admin can actually confirm a triggered reboot took effect (the timestamp jumping forward) rather than only trusting the reboot-ack outcome, which just means the reboot command was scheduled successfully. Computed from `Environment.TickCount64` — a .NET API implemented portably on both Windows and Linux, so this needed no platform-specific code at all, unlike every other self-reported metadata field. Sent as an additive, nullable `bootTimeUtc` field on the existing `alive` request body.
- Real unit test coverage: `WindowsAgentRebooterTests`/`LinuxAgentRebooterTests` (the pure argument-building halves) and `HeartbeatWorker` tests (`WorkerTests`) covering the trigger-and-acknowledge-success path, the no-request no-op path, and the trigger-throws-and-acknowledges-failure path — mirroring the existing install-trigger test coverage exactly.

## [0.15.5] - 2026-09-11

### Added

- **A failed install now carries a human-readable reason back to the admin UI, at the user's explicit request after a real production incident took a raised log level and live journalctl tailing just to see one line — protocol `0.10.0`.** `IUpdateChecker.InstallAsync` now returns a new `InstallResult(InstallOutcome, string? ErrorDetail)` instead of the bare enum — `AptUpdateSession`/`DnfUpdateSession` fill it from the package manager's own stderr/exit code, `WuaUpdateSession` from its result code, and a caught exception anywhere in the chain from its own message. `HeartbeatWorker.HandleInstallRequestAsync` forwards it (capped at 2000 chars agent-side, so one bad install attempt can't send an unbounded blob) as a new optional `ErrorDetail` field on `POST .../install-ack`'s body — additive and nullable, an older server build simply ignores it. New test coverage in `WindowsUpdateCheckerTests`/`LinuxUpdateCheckerTests` (the exception-message path) and `WorkerTests` (both the exception path and a checker returning `Failed` cleanly with its own detail, forwarded verbatim to the acknowledgement).

## [0.15.4] - 2026-09-11

### Changed

- **Checked, at the user's request, whether `LinuxPackageApplier` (the self-update apply step) was similarly affected by the argument-injection class fixed in 0.15.3 — it wasn't, but hardened it anyway for consistency.** The downloaded artifact path `dpkg -i`/`rpm -U` receives is always `Path.Combine` of a fixed, agent-owned absolute staging directory and a filename — `Path.Combine` only omits its first argument when the second is itself rooted, and either way the result is guaranteed to start with `/`, never `-`, so this class's argument parser could never mistake it for a flag the way a bare, unconstrained package-name string could in the sibling classes. Added the same `--` end-of-options marker anyway (pure defense-in-depth/consistency, not a fix for an actually-reachable issue), and pulled the command-building logic out into a new testable `BuildInstallCommand` static method, mirroring `Apt`/`DnfUpdateSession.BuildInstallArgs`.

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
