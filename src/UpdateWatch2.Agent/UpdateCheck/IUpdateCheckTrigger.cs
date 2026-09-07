namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Lets another worker force an update check/report right now, instead of
/// waiting for <c>UpdateCheckWorker</c>'s own jittered interval —
/// <see cref="UpdateWatch2.Agent.HeartbeatWorker"/> uses this right after a successful
/// remote-triggered install so the admin UI's pending-updates list
/// reflects the new state immediately, not after up to
/// <c>AgentOptions.UpdateCheckIntervalMinutes</c> (plus jitter) of staleness.
/// </summary>
public interface IUpdateCheckTrigger
{
    Task CheckAndReportNowAsync(CancellationToken ct = default);
}
