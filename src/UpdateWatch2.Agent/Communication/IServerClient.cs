namespace UpdateWatch2.Agent.Communication;

/// <summary>
/// Talks to the server's agent-facing API. Mutual-TLS (presenting this
/// agent's client certificate, validating the server's) isn't implemented
/// yet — see updatewatch2-agent issue #1. The server-side endpoints this
/// calls into don't exist yet either — see updatewatch2-server issue #3.
/// </summary>
public interface IServerClient
{
    /// <summary>
    /// Registers this agent with the server. Per CLAUDE.md's onboarding
    /// flow, a newly registered agent stays unapproved until an admin
    /// confirms it — callers should keep retrying (with backoff) while
    /// <see cref="RegisterResult.Approved"/> is false rather than treating
    /// that as an error.
    /// </summary>
    Task<RegisterResult> RegisterAsync(CancellationToken ct = default);

    Task SendAliveAsync(CancellationToken ct = default);

    Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default);
}
