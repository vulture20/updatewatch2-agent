using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent;

/// <summary>
/// Sends a periodic alive message to the server (CLAUDE.md section 2.4)
/// and, piggybacked on the same cadence, checks for a protocol-version
/// mismatch (updatewatch2-server#3/updatewatch2-agent#2) — reusing this
/// existing periodic cycle rather than a one-time startup check (e.g.
/// against RegisterResult.ProtocolVersion, in RegistrationWorker) means a
/// server upgrade that happens while this agent keeps running gets
/// detected too, not just a mismatch already present at this agent's own
/// last startup.
/// </summary>
public class HeartbeatWorker(
    AgentOptions options, IServerClient serverClient, IAgentCertificateState certificateState, ILogger<HeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing here works before RegistrationWorker has attached a
        // client certificate (updatewatch2-agent#1) — wait rather than
        // hitting the cert-gated alive endpoint and logging the same
        // expected failure on every tick.
        await certificateState.WaitUntilReadyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await serverClient.SendAliveAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to send alive heartbeat");
            }

            // Deliberately its own try/catch, independent of the alive
            // call above — a transient failure to fetch the version must
            // not affect the heartbeat itself.
            try
            {
                await CheckProtocolVersionAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to check the server's protocol version");
            }

            await Task.Delay(TimeSpan.FromMinutes(options.AliveIntervalMinutes), stoppingToken);
        }
    }

    private async Task CheckProtocolVersionAsync(CancellationToken ct)
    {
        var version = await serverClient.FetchVersionAsync(ct);
        if (version.Protocol != Protocol.ProtocolVersion.Current)
        {
            // Logged every tick it's still mismatched, not just once — an
            // admin watching this agent's log should see it stays
            // relevant until actually fixed (matching how the server's
            // mail-unreachable admin warning stays live rather than
            // one-shot), not just a message that scrolled by once.
            logger.LogWarning(
                "Protocol version mismatch: this agent is on {AgentProtocolVersion}, the server is on {ServerProtocolVersion}. " +
                "Update the agent and/or server so they match.",
                Protocol.ProtocolVersion.Current, version.Protocol);
        }
    }
}
