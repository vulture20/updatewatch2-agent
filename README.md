<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

**Author:** Thorsten Schröpel · [🇩🇪 Deutsche Version](README.de.md)

[![Latest Release](https://img.shields.io/github/v/release/vulture20/updatewatch2-agent?logo=github&color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/vulture20/updatewatch2-agent/total?color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/vulture20/updatewatch2-agent/ci.yml?branch=main&label=build)](https://github.com/vulture20/updatewatch2-agent/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/status-beta-orange)](#-project-status)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Agent is the managed-endpoint half of **UpdateWatch2**: a .NET Worker Service, targeting both Windows and Linux from one codebase, that checks for OS updates, reports them (and whether a reboot is required) to the server, and installs them only on remote trigger — never rebooting on its own.

> ⚠️ **Beta.** The certificate-based onboarding, heartbeat, and self-update mechanics are implemented and tested end to end against a real running server. The real Windows Update API (WUApiLib) integration, the Linux `dnf`/`yum` update path, and the Windows installer's install/uninstall behavior have **not** been verified against a real target host yet. See [Project status](#-project-status) below.

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
- **Windows:** the real Windows Update API (WUApiLib) via late-bound COM — search, download, and install, deliberately excluding driver updates by default, the same conservative default Windows Update's own UI uses.
- **Linux:** `apt`/`dpkg` on Debian-derived distros, `dnf`/`yum` on RPM-based ones, auto-detected at startup; falls back to a no-op checker if neither is present.
- Installation never triggers a reboot itself — "reboot required" is always a separate, independently reported signal.

### 🔄 Agent self-update
- Reacts to the server offering a newer agent release over the existing heartbeat channel — no separate poll loop.
- Downloads the update from **the server itself**, never GitHub directly, so an agent never needs its own internet access.
- Verifies the download's SHA-256 before ever applying it — a mismatch aborts and deletes the download without touching anything platform-specific.
- Windows: re-runs the NSIS installer silently. Linux: `dpkg -i`/`rpm -U` the package, then restarts its own systemd service.

## 🚧 Project status

UpdateWatch2 was built with **vibe coding**: implemented and iterated on with [Claude Code](https://claude.com/claude-code) (Anthropic) in conversation, rather than hand-written line by line, driven by a human-authored architecture brief. The certificate lifecycle, registration/heartbeat/self-update protocol, and the Linux `apt` update-detection path have been run live against a real server and a real package cache, and are covered by an automated (xUnit) test suite. Some pieces are explicitly **not yet live-verified against a real target host**, called out as such in code comments: the Windows Update API (WUApiLib COM) integration, the Linux `dnf`/`yum` path (this project's own dev environment is Debian-based), and the NSIS Windows installer's install/uninstall actually run through `sc.exe`/a package manager. Treat this as a well-researched, actively-tested implementation to build on — not yet battle-tested production software.

## 🚀 Installation & configuration

Every tagged release ([`release.yml`](.github/workflows/release.yml), triggered on a `vX.Y.Z` push) builds and publishes installable packages as [GitHub Release](https://github.com/vulture20/updatewatch2-agent/releases) assets — no manual build needed.

### Windows

Download `UpdateWatch2Agent-Setup-<version>-x64.exe` and run it:

```powershell
# Interactive install — prompts for the server address/port
UpdateWatch2Agent-Setup-0.12.0-x64.exe

# Unattended install (e.g. via a deployment tool)
UpdateWatch2Agent-Setup-0.12.0-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796
```

This installs and starts the `UpdateWatch2 Agent` Windows service, and writes the server address/port to `HKLM\SOFTWARE\UpdateWatch2\Agent` (ACL-restricted to Administrators/SYSTEM). Re-running the installer on top of an existing install performs an upgrade in place. The uninstaller removes the service, install directory, registry key, and (best-effort) this agent's own client certificate from the machine store.

### Linux (`.deb` / `.rpm`, x86_64)

```bash
# Debian/Ubuntu
sudo dpkg -i updatewatch2-agent_<version>_amd64.deb

# RHEL/Fedora/openSUSE
sudo rpm -U updatewatch2-agent-<version>-1.x86_64.rpm
```

This installs to `/opt/updatewatch2-agent/`, seeds a starter `/etc/updatewatch2/agent.conf` if one doesn't already exist, and ships a systemd unit (`updatewatch2-agent.service`) — **enabled but not started** until you set a server address:

```bash
sudo nano /etc/updatewatch2/agent.conf   # set "ServerAddress" (and "ServerPort" if not 8796)
sudo systemctl start updatewatch2-agent
```

An upgrade over an already-configured, already-running agent restarts the service automatically to pick up the new binary — no manual step needed.

### Configuration reference

| Setting | Windows (registry, `HKLM\SOFTWARE\UpdateWatch2\Agent`) | Linux (`/etc/updatewatch2/agent.conf`, JSON) | Default | What it does |
|---|---|---|---|---|
| Server address | `ServerAddress` | `ServerAddress` | — (required) | Hostname/IP of the UpdateWatch2 server — **must match** its own `UPDATEWATCH2_SERVER_HOSTNAME` exactly; this agent validates the server's certificate SAN against it. |
| Server port | `ServerPort` | `ServerPort` | `8796` | The server's agent-facing mutual-TLS port. |
| Update-check interval | `UpdateCheckIntervalMinutes` | `UpdateCheckIntervalMinutes` | `240` | Base interval between OS-update checks, in minutes. |
| Update-check jitter | `UpdateCheckJitterSeconds` | `UpdateCheckJitterSeconds` | `300` | Random jitter (0..N seconds) added on top, so many agents don't hit the server at once. |
| Heartbeat interval | `AliveIntervalMinutes` | `AliveIntervalMinutes` | `5` | How often this agent sends an alive message. |
| Log level | `LogLevel` | `LogLevel` | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR`. |
| Certificate renewal lead time | `CertificateRenewalLeadTimeDays` | `CertificateRenewalLeadTimeDays` | `60` | Days before its certificate's expiry that this agent proactively requests a fresh one. |

`RegistrationToken` and `ClientCertificateThumbprint` are also stored here but are managed automatically by the agent itself — never set these by hand except when placing a fresh token an admin gave you for re-issuance (see the server's admin UI). No service restart is required after changing any of these; the agent picks config changes up on its own maintenance/heartbeat cadence.

> **Placing a re-issuance token:** just write the fresh `RegistrationToken` — that's the only field you need to touch. As of agent v0.14.3, `RegistrationWorker` detects on its own that the config-store token differs from the one it already consumed, and automatically drops the still-locally-present old certificate (and clears `ClientCertificateThumbprint` for you) before registering with the new one — this covers the "re-issue for an agent that's still running fine" case (suspected compromise, not loss), not just a genuinely lost/wiped certificate. On an older agent build, you must clear `ClientCertificateThumbprint` (Windows registry) yourself, or delete `/etc/updatewatch2/agent.pfx` (Linux) — leaving the old value in place there means the fresh token silently sits unused: `RegistrationWorker` keeps finding the old certificate still present and never re-registers, and recovery only happens once the server rejects that old certificate enough times for `HeartbeatWorker`'s self-heal to notice and drop it.

## 🧱 Tech stack

- .NET 10 Generic Host Worker Service, targeting Windows and Linux from one codebase — platform-specific pieces (registry vs. config file, WUApiLib vs. `apt`/`dnf`, the two client-certificate stores) are selected at startup, not via separate build configurations.
- Packaging: NSIS (Windows installer), [`fpm`](https://fpm.readthedocs.io/) (`.deb`/`.rpm`) from a systemd unit and pre/postinst scripts.

## 📁 Repository layout

```
src/UpdateWatch2.Agent/    Certificates/, Communication/, Configuration/, SelfUpdate/, UpdateCheck/ (Windows/, Linux/), RegistrationWorker.cs, HeartbeatWorker.cs, UpdateCheckWorker.cs
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
