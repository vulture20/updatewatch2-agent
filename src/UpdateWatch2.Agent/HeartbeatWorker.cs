using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent;

/// <summary>Sends a periodic alive message to the server (CLAUDE.md section 2.4).</summary>
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

            await Task.Delay(TimeSpan.FromMinutes(options.AliveIntervalMinutes), stoppingToken);
        }
    }
}
