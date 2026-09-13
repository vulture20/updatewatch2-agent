# Deployment runbook (agent)

Step-by-step for installing an UpdateWatch2 agent and getting it approved
and reporting against a real server. This is a procedural companion to
the README — the README's "Installation & configuration" section is the
quick-reference version of the same steps; this document walks through
the reasoning, the order things happen in, and what to check at each
step.

Standing up the server side of this is
`../../server/docs/deployment.md` in the server repo — read that first if
you don't have a running server yet.

## Before you install: two things to have ready

1. **The server's address and agent port** — the same
   `UPDATEWATCH2_SERVER_HOSTNAME` the server was configured with (a plain
   hostname/IP, not a URL), and its agent-facing port (`8796` unless the
   server overrode `Kestrel:AgentPort`). This agent validates the
   server's certificate SAN against exactly this value — a mismatch
   here, not a firewall problem, is the most common reason a freshly
   installed agent never gets past "pending" in the server's overview
   list.
2. **Optionally, the server's CA root certificate**, downloaded ahead of
   time from the server's admin UI (Settings → Certificates → "Download
   CA root certificate"). Without it, this agent trusts whatever CA the
   server hands it on its own first, unauthenticated contact
   (trust-on-first-use) — fine for a lab/test server, worth closing for
   a production rollout. See step 2 below for how to actually use this
   file once you have it.

## 1. Install

### Windows

```powershell
# Interactive — prompts for the server address/port
UpdateWatch2Agent-Setup-<version>-x64.exe

# Unattended (e.g. via a deployment tool)
UpdateWatch2Agent-Setup-<version>-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796
```

Installs and starts the `UpdateWatch2 Agent` Windows service, writing the
server address/port into `HKLM\SOFTWARE\UpdateWatch2\Agent`
(ACL-restricted to Administrators/SYSTEM — this key later also holds a
bearer secret, the registration token). Re-running the installer on top
of an existing install upgrades it in place, service and all.

### Linux (`.deb` / `.rpm`, x86_64)

```bash
# Debian/Ubuntu
sudo dpkg -i updatewatch2-agent_<version>_amd64.deb

# RHEL/Fedora/openSUSE
sudo rpm -U updatewatch2-agent-<version>-1.x86_64.rpm
```

Installs to `/opt/updatewatch2-agent/`, seeds a starter
`/etc/updatewatch2/agent.conf` if one doesn't already exist, and installs
`updatewatch2-agent.service` — **enabled but not started**, since it has
no server address yet.

## 2. (Optional) place the CA certificate before first start

Only relevant if you downloaded the CA root in the "before you install"
step above — skip this if you're fine with trust-on-first-use for now.

- **Windows**: pass it to the installer directly —
  `/CACERT=C:\temp\updatewatch2-ca.crt` alongside the other unattended
  flags. There's no way to add it after the fact through the installer;
  re-run the installer with this flag to add it retroactively.
- **Linux**: place the file at `/etc/updatewatch2/ca.pem` yourself
  *before* the first `systemctl start` below — `.deb`/`.rpm` have no
  install-time parameter mechanism, so this is a manual copy, not an
  installer flag. `postinst.sh` normalizes its ownership/permissions
  (`root:root`, world-readable — it's a public certificate) if it finds
  one already there.

## 3. Configure the server address (Linux only — Windows already has it)

```bash
sudo nano /etc/updatewatch2/agent.conf   # set "ServerAddress" (and "ServerPort" if not 8796)
sudo systemctl start updatewatch2-agent
```

A Windows install already has this from the installer prompt/flag in
step 1 — nothing further to do there.

## 4. Confirm it registered

On the server's admin UI, the agent overview list should show a new row
for this hostname within a few seconds, in the "pending" state. If it
never shows up:

- **Check the server address/port match exactly** — this is by far the
  most common cause. On Linux, `journalctl -u updatewatch2-agent -f`
  while restarting the service; on Windows, the Windows Event Log
  (source: this agent's own event source).
- **Check `systemctl status updatewatch2-agent`** (Linux) or the
  Windows service's own status — confirm it's actually running, not
  just installed.
- If logs show nothing at all, confirm the systemd unit's
  `WorkingDirectory=` is set correctly — an agent installed to a
  non-default location with no `WorkingDirectory=` override can hang
  silently walking an unrelated large filesystem tree before it ever
  reaches its own startup code (see the root `CLAUDE.md` for the exact
  incident this describes, agent v0.12.2).

## 5. Approve it

Back on the server's admin UI: select the new agent (or several, via
bulk-approve) and click Approve. Within one registration-poll interval,
this agent receives and installs its certificate — the overview row
flips to "approved" and a certificate thumbprint appears on its detail
page.

## 6. Confirm it's actually reporting

Give it one full heartbeat interval (`AliveIntervalMinutes`, default 5)
and one full update-check interval (`UpdateCheckIntervalMinutes`, default
240, plus jitter) — the agent detail page should show a recent "last
alive" timestamp well within the former, and its pending-updates list
should populate within the latter (or immediately, if you don't want to
wait: nothing forces you to wait for the jittered interval other than
patience — there's no manual "check now" trigger on the agent side today).

## Configuration reference

Every key below is used verbatim as the registry *value name* under
`HKLM\SOFTWARE\UpdateWatch2\Agent` on Windows, and as the JSON *field
name* in `/etc/updatewatch2/agent.conf` on Linux. No service restart
needed after changing any of these — picked up on the agent's own next
maintenance/heartbeat tick.

| Setting | Key | Default | What it does |
|---|---|---|---|
| Server address | `ServerAddress` | — (required) | Hostname/IP of the UpdateWatch2 server. |
| Server port | `ServerPort` | `8796` | The server's agent-facing mutual-TLS port. |
| Update-check interval | `UpdateCheckIntervalMinutes` | `240` | Base interval between OS-update checks. |
| Update-check jitter | `UpdateCheckJitterSeconds` | `300` | Random jitter added on top. |
| Heartbeat interval | `AliveIntervalMinutes` | `5` | How often this agent sends an alive message. |
| Log level | `LogLevel` | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR`. |
| Certificate renewal lead time | `CertificateRenewalLeadTimeDays` | `60` | Days before expiry this agent proactively renews. |

`RegistrationToken`/`ClientCertificateThumbprint` are also stored here
but managed automatically — the only time to touch either by hand is
placing a fresh `RegistrationToken` an admin gave you for a
re-issuance (see step 7 below).

## 7. Recovering a lost or compromised certificate

If this agent's certificate is genuinely gone (wiped store, corrupted
config) or you suspect it's compromised and want to force a fresh one:
on the server's admin UI, open this agent's detail page and reissue its
certificate — this produces a one-time registration token, shown once.
Write **only** that token into `RegistrationToken` in this agent's config
(registry or `agent.conf`) — nothing else needs changing. This agent
detects the new token on its own within one maintenance-interval tick
(faster still if `HeartbeatWorker`'s own self-heal already noticed the
old certificate being rejected) and re-registers automatically, no
service restart needed.

## Upgrading

Re-run the installer (Windows) or `dpkg -i`/`rpm -U` the newer package
(Linux) — both upgrade in place and restart the service automatically if
it was already configured and running. Nothing needs re-approving; the
same hostname keeps its existing certificate and history.

## Uninstalling

Windows: run the uninstaller from "Add or Remove Programs" — removes the
service, install directory, registry key, and (best-effort) this agent's
own client certificate from the machine store. Linux: your package
manager's normal remove/purge — stops and disables the service and
removes the installed files; `/etc/updatewatch2/agent.conf`/`agent.pfx`
are left in place on a plain remove (not a purge), in case you're about
to reinstall the same configuration.

Either way, this only removes the agent locally — the server still shows
the hostname until you delete it from the admin UI, at which point that
hostname's certificate stops being accepted immediately and its update
history is removed. It would need to register again from scratch as a
brand-new, unapproved agent to come back.
