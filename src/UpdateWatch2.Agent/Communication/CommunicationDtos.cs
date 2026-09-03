namespace UpdateWatch2.Agent.Communication;

public record RegisterRequest(string Hostname, string? DnsName, string OperatingSystem, string? IpAddress, string AgentVersion, string ProtocolVersion);

public record RegisterResult(bool Approved);

public record ReportedUpdate(string Title, string? PackageId, string? Description);

public record ReportUpdatesRequest(IReadOnlyList<ReportedUpdate> Updates, bool RebootRequired);
