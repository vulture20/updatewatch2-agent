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
