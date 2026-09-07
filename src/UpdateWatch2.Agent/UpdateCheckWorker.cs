using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.UpdateCheck;

namespace UpdateWatch2.Agent;

/// <summary>
/// Periodically searches for updates and reports the result to the server.
/// The interval includes a random jitter component (CLAUDE.md section
/// 2.2) so that many agents don't hit the server at the same moment. Also
/// implements <see cref="IUpdateCheckTrigger"/> so <see cref="HeartbeatWorker"/>
/// can force an immediate check/report right after a successful
/// remote-triggered install, rather than leaving the admin UI's
/// pending-updates list stale until this worker's own next jittered tick.
/// </summary>
public class UpdateCheckWorker(
    AgentOptions options,
    IUpdateChecker updateChecker,
    IServerClient serverClient,
    IAgentCertificateState certificateState,
    ILogger<UpdateCheckWorker> logger) : BackgroundService, IUpdateCheckTrigger
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
                await CheckAndReportNowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Update check failed");
            }

            await Task.Delay(NextDelay(), stoppingToken);
        }
    }

    /// <summary>
    /// The actual check-and-report, shared between this worker's own
    /// periodic loop and an on-demand call via <see cref="IUpdateCheckTrigger"/>
    /// (<see cref="HeartbeatWorker"/>, after a successful remote-triggered
    /// install). Deliberately no certificate-readiness wait here — a
    /// caller reaching this method already implies a certificate exists
    /// (this worker's own loop already waited above; <c>HeartbeatWorker</c>
    /// only calls it after a heartbeat round-trip already succeeded).
    /// </summary>
    public async Task CheckAndReportNowAsync(CancellationToken ct = default)
    {
        var result = await updateChecker.CheckAsync(ct);
        await serverClient.ReportUpdatesAsync(
            new ReportUpdatesRequest(
                result.Updates.Select(u => new ReportedUpdate(u.Title, u.PackageId, u.Description)).ToList(),
                result.RebootRequired),
            ct);

        logger.LogInformation(
            "Update check reported {Count} update(s), reboot required: {RebootRequired}",
            result.Updates.Count, result.RebootRequired);
    }

    private TimeSpan NextDelay()
    {
        var jitter = Random.Shared.Next(0, Math.Max(1, options.UpdateCheckJitterSeconds));
        return TimeSpan.FromMinutes(options.UpdateCheckIntervalMinutes) + TimeSpan.FromSeconds(jitter);
    }
}
