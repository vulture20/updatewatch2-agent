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
