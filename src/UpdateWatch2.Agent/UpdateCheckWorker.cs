using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.UpdateCheck;

namespace UpdateWatch2.Agent;

/// <summary>
/// Periodically searches for updates and reports the result to the server.
/// The interval includes a random jitter component (CLAUDE.md section
/// 2.2) so that many agents don't hit the server at the same moment.
/// </summary>
public class UpdateCheckWorker(
    AgentOptions options,
    IUpdateChecker updateChecker,
    IServerClient serverClient,
    IAgentCertificateState certificateState,
    ILogger<UpdateCheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing here works before RegistrationWorker has attached a
        // client certificate (updatewatch2-agent#1) — wait rather than
        // hitting the cert-gated report-updates endpoint and logging the
        // same expected failure on every tick.
        await certificateState.WaitUntilReadyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await updateChecker.CheckAsync(stoppingToken);
                await serverClient.ReportUpdatesAsync(
                    new ReportUpdatesRequest(
                        result.Updates.Select(u => new ReportedUpdate(u.Title, u.PackageId, u.Description)).ToList(),
                        result.RebootRequired),
                    stoppingToken);

                logger.LogInformation(
                    "Update check reported {Count} update(s), reboot required: {RebootRequired}",
                    result.Updates.Count, result.RebootRequired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Update check failed");
            }

            await Task.Delay(NextDelay(), stoppingToken);
        }
    }

    private TimeSpan NextDelay()
    {
        var jitter = Random.Shared.Next(0, Math.Max(1, options.UpdateCheckJitterSeconds));
        return TimeSpan.FromMinutes(options.UpdateCheckIntervalMinutes) + TimeSpan.FromSeconds(jitter);
    }
}
