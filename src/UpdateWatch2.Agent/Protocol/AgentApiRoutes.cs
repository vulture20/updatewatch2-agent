namespace UpdateWatch2.Agent.Protocol;

/// <summary>
/// Server routes the agent calls into (updatewatch2-server#1/#3). These
/// are route templates rather than flat constants because the server's
/// AgentProtocolController/UpdatesController key everything except the CA
/// certificate off <c>api/agents/{hostname}/...</c> — a path segment, not
/// a query string, and plural "agents" (this class previously didn't match
/// either, a latent mismatch caught only once something actually drove
/// registration end to end — see updatewatch2-agent#1's commit history).
/// </summary>
public static class AgentApiRoutes
{
    /// <summary>Not per-agent, and anonymous — an agent's trust anchor before it has anything else to authenticate with.</summary>
    public const string CaCertificate = "/api/agent/ca-certificate";

    /// <summary>Every root the CA currently knows about, as a PKCS7 bundle (updatewatch2-server#6) — see HeartbeatWorker's periodic re-fetch.</summary>
    public const string CaCertificateBundle = "/api/agent/ca-certificates";

    /// <summary>
    /// The server's four version numbers (CLAUDE.md "Four independent
    /// version numbers"). Not per-agent, and anonymous — reachable on the
    /// same mTLS port an agent already talks to, since Kestrel shares one
    /// route table across both listeners (see updatewatch2-server#3).
    /// </summary>
    public const string Version = "/api/version";

    public static string Register(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/register";

    public static string Alive(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/alive";

    public static string ReportUpdates(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/updates";

    /// <summary>Fresh-certificate-before-expiry (updatewatch2-server#7) — authenticated by the current client certificate, not a token.</summary>
    public static string Renew(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/renew";

    /// <summary>Acknowledges a remote-triggered install (updatewatch2-server#10/updatewatch2-agent#4) — see HeartbeatWorker.</summary>
    public static string InstallAck(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/install-ack";

    /// <summary>Acknowledges a remote-triggered machine reboot — see HeartbeatWorker.</summary>
    public static string RebootAck(string hostname) => $"/api/agents/{Uri.EscapeDataString(hostname)}/reboot-ack";

    /// <summary>
    /// A self-update asset's download route — mirrors the server's own
    /// route template exactly (<c>AgentProtocolController</c>'s download
    /// action, and the identical string the server itself builds an
    /// <c>AgentUpdateAssetOffer.DownloadUrl</c> from:
    /// <c>$"/api/agent/updates/{Uri.EscapeDataString(fileName)}"</c>).
    /// Security review finding: <see cref="SelfUpdate.AgentSelfUpdateService"/>
    /// used to fetch the server-supplied <c>DownloadUrl</c> string
    /// verbatim after only validating it looked safe — a blacklist-style
    /// check that's inherently fragile against URL-parsing edge cases
    /// (protocol-relative <c>//host/path</c> references, backslash
    /// variants, ...). Calling this helper with the already-sanitized bare
    /// filename instead means the fetch target is same-origin-relative BY
    /// CONSTRUCTION, not by validating-then-still-trusting the original
    /// untrusted string — there's no longer a check to bypass at all.
    /// </summary>
    public static string UpdateDownload(string fileName) => $"/api/agent/updates/{Uri.EscapeDataString(fileName)}";
}
