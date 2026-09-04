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
}
