using System.Text.Json.Serialization;

namespace UpdateWatch2.Agent.Communication;

/// <summary>
/// Body of a registration call — hostname is deliberately not included
/// here: it's the URL route segment (see AgentApiRoutes.Register), the
/// single source of truth for identity on both sides, per CLAUDE.md
/// ("Agents are identified by hostname"). RegistrationToken is null on
/// first contact and set (from the previous response) on every poll after
/// that — see RegistrationWorker.
/// </summary>
public record RegisterRequest(string? DnsName, string OperatingSystem, string? IpAddress, string AgentVersion, string ProtocolVersion, string? RegistrationToken);

/// <summary>
/// Body of an alive heartbeat (updatewatch2-agent#6) — same self-reported
/// metadata as <see cref="RegisterRequest"/> minus the fields that only
/// make sense at onboarding (ProtocolVersion, RegistrationToken). Sent on
/// every heartbeat because <c>AgentRegistrationService.RegisterAsync</c>
/// (server-side) never runs again for an already-certified agent, so this
/// is the only remaining channel to keep IP/OS/DNS/version current after
/// approval — see this type's server-side counterpart, <c>AgentAliveRequest</c>.
/// <see cref="BootTimeUtc"/> is the same idea, added later, for the
/// remote-reboot feature — computed from <c>Environment.TickCount64</c>,
/// which .NET implements portably on both Windows and Linux, so no
/// platform-specific code is needed to report it. Lets an admin actually
/// confirm a triggered reboot took effect (this value jumping forward to
/// a recent timestamp on a later heartbeat). <see cref="RebootRequired"/>
/// is a different thing again — not identity metadata, but the OS-level
/// "a restart is needed to finish already-installed updates" signal
/// (<c>UpdateCheck.IUpdateChecker.CheckRebootRequiredAsync</c>), riding
/// this same heartbeat so it can be checked far more often than the full
/// update-search cycle is worth running, at the user's explicit request
/// ("Der Check, ob ein Neustart nötig ist, sollte öfter stattfinden.").
/// Null means "the check itself failed this tick, or hasn't run yet" —
/// never conflate with a confirmed <c>false</c>, the same discipline
/// <c>UpdateCheck.RebootCheckResult</c> already enforces one layer down.
/// <see cref="ActualLogLevel"/>/<see cref="ActualUpdateCheckIntervalMinutes"/>/
/// <see cref="ActualUpdateCheckJitterSeconds"/>/<see cref="ActualAliveIntervalMinutes"/>
/// are this agent's own current, actually-effective values for the settings
/// the server can push a per-agent override for — sent every heartbeat
/// regardless of whether an override is active, so the admin UI can show
/// what's really running even after a manual registry/config-file edit, at
/// the user's explicit request ("Änderungen an Registry bzw. Configfile
/// sollen wiederum am Server zu sehen sein."). <see cref="ActualAliveIntervalMinutes"/>
/// was added later than the other three (agent v1.0.15, at the user's
/// explicit request — "Mache bitte auch die Client-Einstellungen für den
/// Alive-Intervall in dem Agent-Einstellungsdialog verfügbar.").
/// </summary>
public record AliveRequest(
    string? DnsName, string OperatingSystem, string? IpAddress, string AgentVersion, DateTimeOffset? BootTimeUtc = null,
    bool? RebootRequired = null, string? ActualLogLevel = null, int? ActualUpdateCheckIntervalMinutes = null,
    int? ActualUpdateCheckJitterSeconds = null, int? ActualAliveIntervalMinutes = null);

/// <summary>
/// Property names match the server's camelCase JSON output field-for-field
/// (case-insensitively, via JsonSerializerDefaults.Web — see ServerClient)
/// except <c>Certificate</c>, which mirrors the server's "certificate"
/// field name directly rather than adding a base64/PFX suffix that JSON
/// binding doesn't need.
/// </summary>
public record RegisterResult(bool Approved, string? RegistrationToken, string? Certificate, string? ProtocolVersion);

public record ReportedUpdate(string Title, string? PackageId, string? Description);

public record ReportUpdatesRequest(IReadOnlyList<ReportedUpdate> Updates, bool RebootRequired);

/// <summary>The server's four independent version numbers (CLAUDE.md) — see <c>GET /api/version</c>.</summary>
public record VersionResponse(string Server, string Protocol, string Database);

/// <summary>
/// Result of <c>POST .../renew</c> (updatewatch2-agent#3/updatewatch2-server#7)
/// — requesting a fresh client certificate before the current one expires.
/// Unlike <see cref="RegisterResult"/>, this call is authenticated by the
/// CURRENT still-valid client certificate itself, not a registration token.
/// </summary>
public record RenewCertificateResult(bool Success, string? Certificate);

/// <summary>
/// Outcome of an alive heartbeat (updatewatch2-server#11/updatewatch2-agent#5).
/// <see cref="CertificateRejected"/> is deliberately its own case, distinct
/// from <see cref="OtherFailure"/> — it's the one outcome that means "the
/// certificate itself is no longer trusted" (a 401/403 response, which can
/// only happen after a real round-trip completed; a network-level problem
/// surfaces as a thrown exception instead, never this enum at all), as
/// opposed to some unrelated server-side problem a 500 or similar would
/// indicate, which self-healing by discarding a perfectly good certificate
/// would only make worse.
/// </summary>
public enum AliveOutcome
{
    Success,
    CertificateRejected,
    OtherFailure,
}

/// <summary>
/// One downloadable asset offered on an <see cref="AliveResult.AgentUpdateAvailable"/>
/// offer — mirrors the server's own <c>AgentUpdateAssetOffer</c> field-for-
/// field. <see cref="DownloadUrl"/> is always a path on THIS server
/// (<c>/api/agent/updates/{fileName}</c>), never GitHub directly — see
/// updatewatch2-server#14's pinned design decision.
/// </summary>
public record AgentUpdateAssetOffer(string DownloadUrl, string Sha256, long SizeBytes);

/// <summary>
/// Surfaced on the <c>alive</c> heartbeat response once a newer agent
/// version than this agent's own <see cref="UpdateWatch2.Agent.AgentVersion.Current"/>
/// is known and agent auto-update is enabled server-side
/// (updatewatch2-server#14/updatewatch2-agent#14) — mirrors the server's
/// own <c>AgentUpdateOffer</c>. Each asset slot is independently nullable —
/// a release might not (yet) carry every platform/architecture's package.
/// See <c>SelfUpdate.IAgentSelfUpdater</c> for how this agent reacts to it.
///
/// <para>
/// Six slots, not three — one per (kind, architecture) combination, since
/// updatewatch2-agent#22/#23 added a second architecture for every kind
/// and the original three-slot shape had no way to tell an x64 asset apart
/// from an arm64 one of the same kind (protocol bumped accordingly). Found
/// by a direct user question asking whether self-update had been
/// considered for the new multi-arch releases at all — it hadn't, at the
/// time. <see cref="SelfUpdate.AgentSelfUpdateService"/> now selects the
/// one slot matching both this agent's own platform (Windows/deb/rpm,
/// unchanged from before) and its own architecture
/// (<see cref="System.Runtime.InteropServices.RuntimeInformation.OSArchitecture"/>,
/// resolved once in <c>Program.cs</c>).
/// </para>
/// </summary>
public record AgentUpdateOffer(
    string Version,
    AgentUpdateAssetOffer? WindowsInstallerX64,
    AgentUpdateAssetOffer? WindowsInstallerArm64,
    AgentUpdateAssetOffer? LinuxDebX64,
    AgentUpdateAssetOffer? LinuxDebArm64,
    AgentUpdateAssetOffer? LinuxRpmX64,
    AgentUpdateAssetOffer? LinuxRpmArm64);

/// <summary>
/// Result of an alive heartbeat, now also carrying whether the server has a
/// remote install pending for this agent (updatewatch2-server#10/
/// updatewatch2-agent#4), whether a newer agent version is available
/// (updatewatch2-server#14/updatewatch2-agent#14), and whether this agent's
/// own client certificate was issued under a CA root a rotation has since
/// superseded (updatewatch2-server#6 follow-up), alongside the existing
/// certificate-rejection signal. <see cref="InstallRequested"/>,
/// <see cref="InstallUpdateIds"/>, <see cref="AgentUpdateAvailable"/>, and
/// <see cref="CertificateRotationPending"/> are only ever meaningful when
/// <see cref="Outcome"/> is <see cref="AliveOutcome.Success"/> — a rejected
/// or otherwise-failed call has no trustworthy body to read them from.
/// </summary>
public record AliveResult(
    AliveOutcome Outcome,
    bool InstallRequested,
    IReadOnlyList<string>? InstallUpdateIds = null,
    AgentUpdateOffer? AgentUpdateAvailable = null,
    bool CertificateRotationPending = false,
    bool RebootRequested = false,
    bool PreDownloadWindowsUpdatesEnabled = false,
    // Null means no server-set override for that setting — this agent's
    // own local registry/config file value stays authoritative. Non-null
    // is enforced unconditionally by HeartbeatWorker on every heartbeat
    // that reports a differing actual value — "the server always wins on
    // conflict" (CLAUDE.md, at the user's explicit request), no separate
    // timestamp-based conflict resolution.
    string? DesiredLogLevel = null,
    int? DesiredUpdateCheckIntervalMinutes = null,
    int? DesiredUpdateCheckJitterSeconds = null,
    int? DesiredAliveIntervalMinutes = null,
    // The Linux counterpart to PreDownloadWindowsUpdatesEnabled above
    // (agent v1.0.16, at the user's explicit request — "Setze den
    // Pre-Download auch für Linux um.") — a genuinely independent
    // fleet-wide toggle, not derived from the Windows one. A Windows agent
    // receives this field too but never reads it, the same "extra field,
    // simply unused on this platform" pattern the Windows flag already
    // established for a Linux agent.
    bool PreDownloadLinuxUpdatesEnabled = false)
{
    public static AliveResult From(AliveOutcome outcome) => new(outcome, InstallRequested: false);
}

/// <summary>
/// Wire-facing mirror of the server's own <c>Updates.InstallOutcome</c> —
/// kept a separate type from <see cref="UpdateCheck.InstallOutcome"/> (what
/// <c>IUpdateChecker.InstallAsync</c> itself returns) even though both
/// currently have identical shape, matching this codebase's existing
/// DetectedUpdate/ReportedUpdate layering: checker-facing and wire-facing
/// DTOs are mapped at the boundary (HeartbeatWorker here) rather than
/// shared directly, so the two can diverge later without coupling the
/// platform-specific checker layer to the wire protocol. Serialized as its
/// name, matching the server's own [JsonConverter(JsonStringEnumConverter)]
/// on Updates.InstallOutcome (see that type's doc comment for why —
/// confirmed live that the default numeric encoding, while technically
/// working end-to-end between this exact client and server, is opaque and
/// inconsistent with this codebase's other wire-facing enums).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstallOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Body of <c>POST .../install-ack</c> — this agent's acknowledgement that
/// it acted on a pending install request. <see cref="ErrorDetail"/> is new
/// (protocol 0.10.0) and only ever meaningful when <see cref="Outcome"/>
/// is <see cref="InstallOutcome.Failed"/> — a human-readable reason (the
/// OS-level tool's own stderr/exit code, or a caught exception's message)
/// so an admin can see WHY an install failed straight from the admin UI
/// instead of having to raise this agent's log level and tail journalctl
/// live, which is what a real production incident on a genuinely
/// unrelated apt-repository-trust problem took before this existed.
/// Additive and nullable, matching every prior wire-shape change in this
/// codebase — an older server build simply ignores the extra field.
/// </summary>
public record InstallAckRequest(InstallOutcome Outcome, string? ErrorDetail = null);

/// <summary>
/// Wire-facing mirror of the server's own <c>Agents.RebootOutcome</c> —
/// kept as its own separate type from <see cref="InstallOutcome"/> even
/// though the shape is identical, matching this codebase's existing
/// checker-facing/wire-facing DTO layering: a machine reboot is a
/// distinct action from an OS-update install, deliberately never
/// conflated (CLAUDE.md's "update installation never triggers a reboot
/// itself... the admin decides when to actually trigger a reboot" rule is
/// exactly the distinction this type exists to preserve on the wire).
/// Serialized as its name, matching the server's own
/// [JsonConverter(JsonStringEnumConverter)] on Agents.RebootOutcome.
/// "Succeeded" only ever means the platform's reboot command was
/// scheduled successfully, not that the machine has actually come back up
/// yet — that's instead visible via <see cref="AliveRequest.BootTimeUtc"/>
/// jumping forward on a later heartbeat.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RebootOutcome
{
    Succeeded,
    Failed,
}

/// <summary>Body of <c>POST .../reboot-ack</c> — this agent's acknowledgement that it acted on a pending reboot request.</summary>
public record RebootAckRequest(RebootOutcome Outcome, string? ErrorDetail = null);
