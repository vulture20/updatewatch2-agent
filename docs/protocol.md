# UpdateWatch2 agent-server transfer protocol

This is a reference for the wire shapes exchanged between an agent and the
server — the protocol version tracked independently of the server, agent,
and DB schema versions (see the root `CLAUDE.md`, "Four independent
version numbers"). It documents what's actually implemented today; if this
drifts from the code, the code wins. The authoritative source lives across
both repos: server-side, `Api/Controllers/AgentProtocolController.cs`,
`Api/Controllers/UpdatesController.cs`, `Agents/AgentRegistrationDtos.cs`,
and `Updates/UpdateDtos.cs` in `updatewatch2-server`; agent-side, how this
agent actually calls each route and acts on each response —
`Communication/ServerClient.cs`, `RegistrationWorker.cs`,
`HeartbeatWorker.cs`, and `UpdateCheckWorker.cs` in this repo.

The identical copy of this file lives in the server repo at
`docs/protocol.md` — the protocol is shared, so both repos describe it the
same way rather than one linking to the other (each repo is meant to stand
on its own).

**Current protocol version:** see `Protocol/ProtocolVersion.cs` in either
repo (kept in sync between them by hand on every wire-shape change).

## Transport

- All agent-facing endpoints listen on the server's dedicated agent port
  (`Kestrel:AgentPort`, default `8796`) — Kestrel terminates TLS directly
  there, no reverse proxy in front, unlike the admin-facing port `8795`.
- Every route except `register` and the two `ca-certificate(s)` routes
  requires a client certificate issued by the server's own internal CA,
  validated by the `AgentCertificate` authorization policy (mutual TLS —
  see `architecture.md`'s "Certificate lifecycle" section for how an agent
  gets one in the first place).
- Every request/response body is JSON. Enum-like fields (`RebootOutcome`,
  `Updates.InstallOutcome`) are serialized as their name
  (`"Succeeded"`/`"Failed"`), never the default numeric encoding — this
  project has no global `JsonStringEnumConverter`, so a caller sending the
  bare number gets a 400.
- A hostname in the URL path is the single source of truth for agent
  identity (CLAUDE.md, "Agents are identified by hostname") — no separate
  numeric agent ID exists anywhere on the wire.

## Bootstrapping trust: the CA certificate routes

Both are anonymous — they only ever expose public certificates, nothing
secret — and are what an agent's trust-on-first-use (TOFU) flow bootstraps
from before it has a client certificate of its own to authenticate with.

### `GET /api/agent/ca-certificate`

Returns the CA's single current root certificate as a raw DER blob
(`application/x-x509-ca-cert`). This is what a genuinely fresh agent (one
whose local CA trust store is empty) falls back to fetching on its own —
see the "Certificate-based mutual auth" section of the root `CLAUDE.md` for
the TOFU risk window this implies and how an admin can close it ahead of
time (pre-seeding the CA file via the installer instead of relying on this
endpoint at all).

### `GET /api/agent/ca-certificates`

Additive, not a replacement — returns every root the CA currently knows
about (current, and a previous/pending one mid-rotation) as a PKCS#7
certs-only bundle (`application/pkcs7-mime`). A rotation-aware agent polls
this every heartbeat and merges in any root it doesn't already trust,
purely additively, so it can pick up a newly prepared root before an admin
ever activates it.

## `POST /api/agents/{hostname}/register`

Anonymous. This is how a brand-new agent (or one re-registering with a
fresh admin-issued token) gets approved and, once approved, receives its
client certificate.

**Request** (`AgentRegisterRequest`):

```json
{
  "dnsName": "host1.example.com",
  "operatingSystem": "Windows Server 2022",
  "ipAddress": "10.0.0.5",
  "agentVersion": "1.0.1",
  "protocolVersion": "1.1.0",
  "registrationToken": null
}
```

All fields are optional. `registrationToken` is omitted on an agent's very
first-ever contact for a hostname, and present on every poll after that —
see `Agents/AgentRegistrationService`'s own doc comment for the full
state machine (self-registration → admin approval → certificate issuance,
and how a hijack attempt on an already-claimed hostname is rejected).

**Response** — `200 OK`:

```json
{
  "approved": false,
  "registrationToken": "<opaque one-time token, present until approved>",
  "certificate": null,
  "protocolVersion": "1.1.0"
}
```

Once an admin approves the agent and it polls again with that same token,
`approved` becomes `true` and `certificate` carries the issued client
certificate as a base64-encoded PFX (PKCS#12) — handed out exactly once;
the agent is expected to install it into its platform's certificate store
(or `agent.pfx` on Linux) and never ask again.

**Response** — `409 Conflict` (rejected — no/mismatched token for an
already-known hostname):

```json
{ "message": "<human-readable rejection reason>" }
```

## `POST /api/agents/{hostname}/alive`

mTLS-gated. The heartbeat — an already-certified agent calls this
periodically (`HeartbeatWorker`'s own cadence) both to prove it's still
alive (`Agent.LastAliveAt`) and to pick up anything the server has queued
for it since the last tick.

**Request** (`AgentAliveRequest`, optional body — omitting it entirely is
valid and keeps working for an agent build that predates the fields it
would carry):

```json
{
  "dnsName": "host1.example.com",
  "operatingSystem": "Windows Server 2022",
  "ipAddress": "10.0.0.5",
  "agentVersion": "1.0.1",
  "bootTimeUtc": "2026-09-01T08:00:00Z"
}
```

Every field refreshes this agent's stored metadata — registration only
ever runs once for an already-certified agent, so this is the only
remaining channel for a hostname/IP/OS/version change to reach the server
afterward.

**Response** — `200 OK` (`AliveRecordResult`):

```json
{
  "installRequested": false,
  "installUpdateIds": null,
  "agentUpdateAvailable": null,
  "certificateRotationPending": false,
  "rebootRequested": false,
  "preDownloadWindowsUpdatesEnabled": true
}
```

Every field is a delivery mechanism for something an admin (or the server
itself) queued, piggybacked on this same round trip rather than a separate
poll endpoint:

| Field | True/non-null means | Cleared by |
|---|---|---|
| `installRequested` | An admin clicked "Trigger install" (`POST .../install`). | `POST .../install-ack` |
| `installUpdateIds` | `null` = install everything pending; otherwise the specific `PackageId`s to install, sparing the rest. | Implicitly, alongside `installRequested`. |
| `agentUpdateAvailable` | A newer agent *software* release is known and auto-update is enabled — see the `AgentUpdateOffer` shape below. | Self-correcting — the next heartbeat after the agent restarts on the new build reports the new version and this goes null on its own. |
| `certificateRotationPending` | This agent's certificate was issued under a CA root that's no longer the CA's current one (a rotation happened since). | Self-correcting — clears the moment the agent renews via `POST .../renew`. |
| `rebootRequested` | An admin clicked "Reboot machine" (`POST .../reboot`). | `POST .../reboot-ack` |
| `preDownloadWindowsUpdatesEnabled` | The admin-configured, fleet-wide `AdminSettings.PreDownloadWindowsUpdatesEnabled` toggle (Settings → General, default true) — not a queued action, just the live setting value re-sent every heartbeat. Windows-only: `UpdateCheckWorker` acts on it (via `IUpdateChecker.PreDownloadAsync`) only on a Windows agent; a Linux agent receives the same field but its own pre-download step is currently a no-op. | Not "cleared" — re-evaluated fresh every heartbeat from the live setting. |

`agentUpdateAvailable`'s shape (`AgentUpdateOffer`), when non-null:

```json
{
  "version": "1.1.0",
  "windowsInstaller": { "downloadUrl": "/api/agent/updates/UpdateWatch2Agent-Setup-1.1.0-x64.exe", "sha256": "...", "sizeBytes": 34500000 },
  "linuxDeb": { "downloadUrl": "/api/agent/updates/updatewatch2-agent_1.1.0_amd64.deb", "sha256": "...", "sizeBytes": 4200000 },
  "linuxRpm": null
}
```

Each asset slot is independently nullable (a release might not have built
every package). `downloadUrl` is always a same-server-relative path under
`/api/agent/updates/{fileName}` — an agent must never fetch release assets
from GitHub directly (see `AgentUpdates/` in the root `CLAUDE.md` for why),
and must verify `sha256` before applying anything it downloads.

## `POST /api/agents/{hostname}/renew`

mTLS-gated, authenticated by the agent's **current, still-valid**
certificate — not a registration token. Re-issues a fresh leaf before the
current one expires, or immediately after `certificateRotationPending`
turns true.

**Request:** no body.

**Response** — `200 OK`:

```json
{ "certificate": "<base64 PFX>" }
```

**Response** — `409 Conflict`:

```json
{ "message": "<human-readable failure reason>" }
```

## `GET /api/agents/{hostname}/updates`

mTLS-gated. Returns this agent's currently-known update list
(`UpdateItemDto[]`) — mainly useful for an agent to reconcile its own
locally-detected state; the admin UI is the primary consumer of the
equivalent admin-facing read.

## `POST /api/agents/{hostname}/updates`

mTLS-gated. An agent's own periodic update-check report
(`UpdateCheckWorker`'s cadence).

**Request** (`ReportUpdatesRequest`):

```json
{
  "updates": [
    { "title": "2026-09 Cumulative Update for Windows Server 2022", "packageId": "KB5041234", "description": null }
  ],
  "rebootRequired": false
}
```

The server merges this against what it already has for the agent (matched
by `packageId` when both sides have one, else by `title`) rather than
replacing the list wholesale — an update already known keeps its original
`DetectedAt`; one no longer reported is dropped as resolved. `304`/no
special response shape: `204 No Content` on success, `404` if the hostname
is unknown.

## `POST /api/agents/{hostname}/install-ack`

mTLS-gated. The agent's acknowledgement that it acted on a pending install
request delivered via `alive`'s `installRequested` field.

**Request** (`InstallAckRequest`):

```json
{ "outcome": "Succeeded", "errorDetail": null }
```

`outcome` is `"Succeeded"` or `"Failed"`. `errorDetail` is only ever
meaningful alongside `"Failed"` — the package manager's own stderr, a
Windows Update result code, or a caught exception's message (capped at
2000 chars agent-side). `204 No Content` on success.

## `POST /api/agents/{hostname}/reboot-ack`

mTLS-gated. The same acknowledgement pattern as `install-ack`, for a
pending reboot delivered via `alive`'s `rebootRequested` field.

**Request** (`RebootAckRequest`):

```json
{ "outcome": "Succeeded", "errorDetail": null }
```

`"Succeeded"` here only ever means the platform's reboot command was
scheduled successfully — not that the machine has actually come back up
yet (that's `bootTimeUtc` jumping forward on a later `alive` call, a
signal deliberately kept separate per CLAUDE.md's "update installation
never triggers a reboot itself" rule).

## `GET /api/agent/updates/{fileName}`

mTLS-gated (not anonymous, unlike the CA-certificate routes — the asset
isn't secret, but there's no reason to expose it more broadly than the
rest of the agent-facing API). Streams the raw bytes of a self-update
asset previously offered via `alive`'s `agentUpdateAvailable` field.
`fileName` is only ever resolved against the exact filenames the server
itself already recorded for the current known release — never
concatenated into a filesystem path from the request, which is what
actually prevents path traversal here, not any string sanitization.

## Admin-triggered actions (not agent-initiated, delivered via `alive`)

These are admin-facing endpoints (cookie-session-gated, not mTLS) that
queue something for delivery on the agent's *next* heartbeat rather than
pushing to it directly — this server has no push channel to an agent, only
the heartbeat's own pull cadence.

- `POST /api/agents/{hostname}/install` (optional `TriggerInstallRequest`
  body — `{ "updateItemIds": [1, 2, 3] }`, or no body/`null` to install
  everything pending) sets `Agent.PendingInstallRequestedAt` (and
  optionally `PendingInstallUpdateIds`), surfaced on the next `alive` as
  `installRequested`/`installUpdateIds`.
- `POST /api/agents/{hostname}/reboot` sets `Agent.PendingRebootRequestedAt`,
  surfaced as `rebootRequested`.

## Compatibility policy

Every change to a request/response shape is additive — a new nullable
field an older build simply never sends or never reads — and comes with a
protocol version bump (`Protocol/ProtocolVersion.cs` in both repos).
`HeartbeatWorker` polls the server's own `GET /api/version` on its normal
cadence and logs a warning (not a hard rejection) on a mismatch, so a
temporarily mixed fleet during a rollout keeps working, just with a
logged heads-up.
