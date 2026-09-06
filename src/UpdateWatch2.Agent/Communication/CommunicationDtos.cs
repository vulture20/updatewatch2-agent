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
/// </summary>
public record AliveRequest(string? DnsName, string OperatingSystem, string? IpAddress, string AgentVersion);

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
/// a release might not (yet) carry every platform's package. See
/// <c>SelfUpdate.IAgentSelfUpdater</c> for how this agent reacts to it.
/// </summary>
public record AgentUpdateOffer(
    string Version,
    AgentUpdateAssetOffer? WindowsInstaller,
    AgentUpdateAssetOffer? LinuxDeb,
    AgentUpdateAssetOffer? LinuxRpm);

/// <summary>
/// Result of an alive heartbeat, now also carrying whether the server has a
/// remote install pending for this agent (updatewatch2-server#10/
/// updatewatch2-agent#4) and whether a newer agent version is available
/// (updatewatch2-server#14/updatewatch2-agent#14), alongside the existing
/// certificate-rejection signal. <see cref="InstallRequested"/> and
/// <see cref="AgentUpdateAvailable"/> are only ever meaningful when
/// <see cref="Outcome"/> is <see cref="AliveOutcome.Success"/> — a rejected
/// or otherwise-failed call has no trustworthy body to read them from.
/// </summary>
public record AliveResult(AliveOutcome Outcome, bool InstallRequested, AgentUpdateOffer? AgentUpdateAvailable = null)
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

/// <summary>Body of <c>POST .../install-ack</c> — this agent's acknowledgement that it acted on a pending install request.</summary>
public record InstallAckRequest(InstallOutcome Outcome);
