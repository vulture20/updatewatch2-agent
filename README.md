<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

**Author:** Thorsten Schröpel · [🇩🇪 Deutsche Version](README.de.md)

[![Latest Release](https://img.shields.io/github/v/release/vulture20/updatewatch2-agent?logo=github&color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/vulture20/updatewatch2-agent/total?color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/vulture20/updatewatch2-agent/ci.yml?branch=main&label=build)](https://github.com/vulture20/updatewatch2-agent/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/status-stable-brightgreen)](#-project-status)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Agent is the managed-endpoint half of **UpdateWatch2**: a .NET Worker Service, targeting both Windows and Linux from one codebase, that checks for OS updates, reports them (and whether a reboot is required) to the server, and installs them only on remote trigger — never rebooting on its own.

> ✅ **Stable.** The certificate-based onboarding and heartbeat are implemented and tested end to end against a real running server; self-update's negotiation half (offer, download, SHA-256 verification) is too. The real Windows Update API (WUApiLib) integration, the Windows installer's install/uninstall behavior, Windows self-update's apply step, Windows remote reboot scheduling, and Windows Event Log output have all since been confirmed against a real Windows host too, including a selective (per-update) install. The Linux `dnf`/`yum` update path, Linux self-update's apply step, Linux remote reboot, and the arm64 install path on both Windows and Linux have **not** been verified against a real target host yet. See [Project status](#-project-status) below.

Companion repository: [updatewatch2-server](https://github.com/vulture20/updatewatch2-server) — the management server this agent reports to.

## ✨ Features

### 🔐 Certificate-based onboarding
- Identified by hostname. On first contact the agent registers, pins the server's CA certificate (trust-on-first-use), and polls until an admin approves it and issues a client certificate.
- The certificate is stored securely (Windows: the machine certificate store, non-exportable; Linux: `/etc/updatewatch2/agent.pfx`, owner-only) and presented on every request afterward.
- Renews itself proactively before expiry, recovers automatically if an admin re-issues a fresh registration token — no service restart needed either way — and picks up a rotated CA root the moment the server reports it, rather than waiting out its normal schedule.

### 💓 Heartbeat & update checking
- A periodic alive message keeps the server's fleet overview current (last-seen, DNS name, OS, IP, agent version — all re-sent on every heartbeat, not just once at registration).
- Update checking runs on its own configurable interval with random jitter, so many agents don't hit the server at the same moment.
- Detects a protocol-version mismatch against the server and logs a warning, without hard-failing.

### 📦 Real update detection & installation
- **Windows:** the real Windows Update API (WUApiLib) via late-bound COM — search, download, and install, deliberately excluding driver updates by default, the same conservative default Windows Update's own UI uses. Can also proactively download pending updates ahead of an actual install trigger, gated by an admin-configurable, fleet-wide toggle (Settings → General on the server) — so an install applies from local cache instead of downloading at trigger time.
- **Linux:** `apt`/`dpkg` on Debian-derived distros, `dnf`/`yum` on RPM-based ones, auto-detected at startup; falls back to a no-op checker if neither is present. Same independent pre-download toggle as Windows, above (Settings → General on the server has separate checkboxes for each platform).
- Installation never triggers a reboot itself — "reboot required" is always a separate, independently reported signal. A full machine reboot (not just this agent's own service) can still be triggered remotely by an admin, delivered and acknowledged the same way as a triggered install.

### 🔄 Agent self-update
- Reacts to the server offering a newer agent release over the existing heartbeat channel — no separate poll loop.
- Downloads the update from **the server itself**, never GitHub directly, so an agent never needs its own internet access.
- Verifies the download's SHA-256 before ever applying it — a mismatch aborts and deletes the download without touching anything platform-specific.
- Windows: re-runs the NSIS installer silently. Linux: `dpkg -i`/`rpm -U` the package, then restarts its own systemd service.

## ✅ Project status

UpdateWatch2 was built with **vibe coding**: implemented and iterated on with [Claude Code](https://claude.com/claude-code) (Anthropic) in conversation, rather than hand-written line by line, driven by a human-authored architecture brief. The certificate lifecycle (registration, approval, renewal, re-issuance, CA root rotation) and the alive heartbeat have been run live against a real server, and the Linux `apt` update-*detection* path has been run live against a real package cache; all are covered by an automated (xUnit) test suite. The Windows Update API (WUApiLib COM) integration has since been run live end to end against a real Windows host — search, download, install (including a selective, per-update install), and reboot detection all confirmed working — and the NSIS installer's install/uninstall behavior actually run through `sc.exe` has likewise been confirmed on a real Windows host, closing [updatewatch2-agent#13](https://github.com/vulture20/updatewatch2-agent/issues/13). Agent self-update's *negotiation* half (the server offering a release, this agent downloading it and verifying its SHA-256) has been run live too, and — on Windows — so has actually *applying* one: `WindowsInstallerApplier` silently re-running the NSIS installer, including the service delete/recreate sequence, has been confirmed working end to end against a real Windows host. `shutdown.exe`-based remote reboot scheduling and Windows Event Log output have likewise been confirmed on a real Windows host. A number of pieces are explicitly **not yet live-verified against a real target host**, called out as such in code comments:

- **Windows**: Windows-on-ARM (`win-arm64`) entirely — the publish itself has been confirmed to produce a genuine native ARM64 executable, just never run on a real device.
- **Linux**: the `dnf`/`yum` update path (this project's own dev environment is Debian-based, only `apt` was verified); actually applying a self-update (`dpkg -i`/`rpm -U`) completing end to end on a real host — the cgroup-escape mechanism it depends on was confirmed live, but a full self-update cycle after that fix was not re-verified, and [updatewatch2-agent#24](https://github.com/vulture20/updatewatch2-agent/issues/24) tracks a real report suggesting it may still fail silently; `systemd`-based remote reboot scheduling (deliberately never run for real — would reboot the shared sandbox this project's own tooling depends on).
- **Packaging**: `.rpm` install/upgrade on x86_64 (structural inspection only, never run through a real package manager). arm64 `.deb`/`.rpm` are a partial exception: each release's CI pipeline actually starts the published `linux-arm64` binary on a native (not emulated) arm64 runner and confirms it runs its real startup/registration code before packaging, so that much is live-verified on real arm64 hardware — install/upgrade through `dpkg`/`rpm` itself on an arm64 host is not, same as the x86_64 gap. Windows-on-ARM (`win-arm64`) is likewise unconfirmed on a real device.

Treat the pieces called out above as well-researched but not yet confirmed on a real target host — everything else, including the Windows Update API integration, the installer's install/uninstall behavior, the Windows self-update apply step, Windows remote reboot, and Windows Event Log output above, has been live-verified end to end.

## 🚀 Installation & configuration

Every tagged release ([`release.yml`](.github/workflows/release.yml), triggered on a `vX.Y.Z` push) builds and publishes installable packages as [GitHub Release](https://github.com/vulture20/updatewatch2-agent/releases) assets — no manual build needed.

### Windows

Every release publishes two installers — `UpdateWatch2Agent-Setup-<version>-x64.exe` for regular (x64) Windows and `UpdateWatch2Agent-Setup-<version>-arm64.exe` for Windows-on-ARM devices (Snapdragon-based laptops, Surface Pro X, etc.), each bundling a native, self-contained publish for that architecture. Download the one matching your device and run it — everything below applies identically to both, just with the matching filename:

```powershell
# Interactive install — prompts for the server address/port
UpdateWatch2Agent-Setup-1.0.24-x64.exe

# Unattended install (e.g. via a deployment tool)
UpdateWatch2Agent-Setup-1.0.24-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796
```

This installs and starts the `UpdateWatch2 Agent` Windows service, and writes the server address/port to `HKLM\SOFTWARE\UpdateWatch2\Agent` (ACL-restricted to Administrators/SYSTEM). Re-running the installer on top of an existing install performs an upgrade in place. The uninstaller removes the service, install directory, registry key, and (best-effort) this agent's own client certificate from the machine store.

#### Pre-seeding the CA certificate (closing the trust-on-first-use window)

By default, a freshly installed agent trusts whatever CA certificate the server hands it on its very first contact ("trust-on-first-use", TOFU) — a network attacker present at *exactly* that moment could intercept it and hand the agent a malicious CA instead. To close that window, download the server's current CA root certificate ahead of time — from the admin UI's Certificates tab ("Download CA root certificate"), or `GET /api/admin/certificate-authority/download` directly, both session-authenticated — and pass it to the installer via `/CACERT=`:

```powershell
UpdateWatch2Agent-Setup-1.0.24-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796 /CACERT=C:\temp\updatewatch2-ca.crt
```

This is optional and fully backward compatible — omit `/CACERT=` and the original TOFU behavior is unchanged.

### Linux (`.deb` / `.rpm`, x86_64 or arm64)

Every release publishes both architectures — `amd64`/`x86_64` packages for regular Linux hosts and `arm64`/`aarch64` ones for arm64 hosts (AWS Graviton, Ampere Altra, Raspberry Pi, etc.), each bundling a native, self-contained publish for that architecture. Download the file matching your host's architecture (`uname -m`: `x86_64` → the `amd64`/`x86_64` package, `aarch64` → the `arm64`/`aarch64` one):

```bash
# Debian/Ubuntu, x86_64
sudo dpkg -i updatewatch2-agent_<version>_amd64.deb
# Debian/Ubuntu, arm64
sudo dpkg -i updatewatch2-agent_<version>_arm64.deb

# RHEL/Fedora/openSUSE, x86_64
sudo rpm -U updatewatch2-agent-<version>-1.x86_64.rpm
# RHEL/Fedora/openSUSE, arm64
sudo rpm -U updatewatch2-agent-<version>-1.aarch64.rpm
```

This installs to `/opt/updatewatch2-agent/`, seeds a starter `/etc/updatewatch2/agent.conf` if one doesn't already exist, and ships a systemd unit (`updatewatch2-agent.service`) — **enabled but not started** until you set a server address:

```bash
sudo nano /etc/updatewatch2/agent.conf   # set "ServerAddress" (and "ServerPort" if not 8796)
sudo systemctl start updatewatch2-agent
```

An upgrade over an already-configured, already-running agent restarts the service automatically to pick up the new binary — no manual step needed.

`.deb`/`.rpm` packages have no install-time parameter mechanism, so there's no Linux equivalent of `/CACERT=` above — instead, place the same downloaded CA certificate at `/etc/updatewatch2/ca.pem` yourself *before* the first `systemctl start updatewatch2-agent`, to close the trust-on-first-use window the same way. `postinst.sh` normalizes its ownership/permissions (`root:root`, world-readable) if it finds one already there when the package installs.

### Configuration reference

Every key below is used verbatim in both places: as the registry *value name* under `HKLM\SOFTWARE\UpdateWatch2\Agent` on Windows, and as the JSON *field name* in `/etc/updatewatch2/agent.conf` on Linux.

| Setting | Key | Default | What it does |
|---|---|---|---|
| Server address | `ServerAddress` | — (required) | Hostname/IP of the UpdateWatch2 server — **must match** its own `UPDATEWATCH2_SERVER_HOSTNAME` exactly; this agent validates the server's certificate SAN against it. |
| Server port | `ServerPort` | `8796` | The server's agent-facing mutual-TLS port. |
| Hostname override | `HostnameOverride` | — (unset) | Overrides the hostname this agent reports to — and is thereafter identified by on — the server, in place of the OS-reported machine name. Local-only, never pushed by the server. Does **not** affect the separate, purely informational DNS-name field shown in the admin UI. **Only safe to set before this agent's very first registration** — changing it on an agent that already holds a certificate is detected (a warning is logged every heartbeat, and no heartbeat/renewal is attempted while it persists) but not acted on automatically; to actually rename an already-onboarded agent, delete its old entry in the admin UI and remove this agent's local certificate (Windows: the machine certificate store; Linux: `/etc/updatewatch2/agent.pfx`) so it registers fresh under the new name. |
| Update-check interval | `UpdateCheckIntervalMinutes` | `240` | Base interval between OS-update checks, in minutes. |
| Update-check jitter | `UpdateCheckJitterSeconds` | `300` | Random jitter (0..N seconds) added on top, so many agents don't hit the server at once. |
| Heartbeat interval | `AliveIntervalMinutes` | `5` | How often this agent sends an alive message. |
| Registration poll interval | `RegistrationRetryIntervalSeconds` | `30` | How often this agent polls the server while waiting for admin approval/certificate issuance during onboarding — separate from the (much longer) heartbeat interval, since a human is typically watching during this phase. |
| Log level | `LogLevel` | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR`. |
| Certificate renewal lead time | `CertificateRenewalLeadTimeDays` | `60` | Days before its certificate's expiry that this agent proactively requests a fresh one. |
| Certificate maintenance interval | `CertificateMaintenanceIntervalSeconds` | `900` | Upper bound on how often this agent re-checks its local certificate once one is already attached (e.g. noticing a fresh re-issuance token). Only an upper bound — a rejected certificate wakes this check immediately rather than waiting it out. |
| Allow unauthenticated packages | `AllowUnauthenticatedPackages` | `false` | **Linux only.** Passes apt-get's `--allow-unauthenticated` / dnf's and yum's `--nogpgcheck` on install and pre-download, so a repository with an invalid or missing signature doesn't fail the whole transaction. Security-relevant — only enable this if you've deliberately decided to trust an unsigned/local repository; the usual fix for an "unauthenticated packages" error is importing that repository's GPG key, not this. Local-only, never pushed by the server. |
| Self-update staging retention | `SelfUpdateStagingRetentionDays` | `90` | How long a downloaded self-update package is kept in local staging before being cleaned up. The single most-recently-downloaded package is always kept regardless of this value. |

`RegistrationToken` and `ClientCertificateThumbprint` are also stored here but are managed automatically by the agent itself — never set these by hand except when placing a fresh token an admin gave you for re-issuance (see the server's admin UI). No service restart is required after changing any of these; the agent picks config changes up on its own maintenance/heartbeat cadence.

> **Placing a re-issuance token:** just write the fresh `RegistrationToken` — that's the only field you need to touch. As of agent v0.14.3, `RegistrationWorker` detects on its own that the config-store token differs from the one it already consumed, and automatically drops the still-locally-present old certificate (and clears `ClientCertificateThumbprint` for you) before registering with the new one — this covers the "re-issue for an agent that's still running fine" case (suspected compromise, not loss), not just a genuinely lost/wiped certificate. On an older agent build, you must clear `ClientCertificateThumbprint` (Windows registry) yourself, or delete `/etc/updatewatch2/agent.pfx` (Linux) — leaving the old value in place there means the fresh token silently sits unused: `RegistrationWorker` keeps finding the old certificate still present and never re-registers, and recovery only happens once the server rejects that old certificate enough times for `HeartbeatWorker`'s self-heal to notice and drop it.

## 🧱 Tech stack

- .NET 10 Generic Host Worker Service, targeting Windows and Linux from one codebase — platform-specific pieces (registry vs. config file, WUApiLib vs. `apt`/`dnf`, the two client-certificate stores) are selected at startup, not via separate build configurations.
- Packaging: NSIS (Windows installer), [`fpm`](https://fpm.readthedocs.io/) (`.deb`/`.rpm`) from a systemd unit and pre/postinst scripts.

## 📁 Repository layout

```
src/UpdateWatch2.Agent/    Certificates/, Communication/, Configuration/, Reboot/, SelfUpdate/, UpdateCheck/ (Windows/, Linux/), RegistrationWorker.cs, HeartbeatWorker.cs, UpdateCheckWorker.cs
tests/                     xUnit — hand-written fakes, no mocking library
installer/nsis/            setup.nsi — the Windows installer
installer/linux/           systemd unit + postinst/prerm/postrm scripts, packaged via fpm
```

## 🛠️ Local development

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/UpdateWatch2.Agent   # runs in the foreground, not installed as a service
```

## 📜 Changelog

See [`CHANGELOG.md`](CHANGELOG.md) for a version-by-version history of notable changes.

## ⚖️ License

Copyright (C) 2026 Thorsten Schröpel.

UpdateWatch2 Agent is free software: you can redistribute it and/or modify it under the terms of the [GNU Affero General Public License](LICENSE) as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE), or <https://www.gnu.org/licenses/agpl-3.0.html> for the full text.

In plain terms: you're free to run, modify, and self-host UpdateWatch2. The one obligation AGPL adds on top of a regular GPL license is that if you run a **modified** version and let other users interact with it over a network, you must also offer those users access to your modified source code — not just people you hand a copy of the software to directly. Running an unmodified copy for yourself carries no extra obligation beyond the standard copyleft terms.
