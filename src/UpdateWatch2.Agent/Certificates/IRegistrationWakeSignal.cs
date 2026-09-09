namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Lets <see cref="HeartbeatWorker"/> wake <see cref="RegistrationWorker"/>
/// up immediately instead of it waiting out its own
/// <see cref="Configuration.AgentOptions.CertificateMaintenanceIntervalSeconds"/>
/// sleep (default 15 minutes) — found by a real user report: after an
/// admin-mediated certificate re-issuance
/// (<c>updatewatch2-server#8</c>) while this agent kept running, recovery
/// sometimes appeared to simply never happen. It does eventually, but the
/// two workers run on completely independent timers with no signaling
/// between them: <see cref="HeartbeatWorker"/>'s own self-heal
/// (<c>updatewatch2-server#11</c>/<c>updatewatch2-agent#5</c>) needs up to
/// two heartbeat intervals to even decide the certificate is rejected, and
/// <see cref="RegistrationWorker"/> then doesn't notice the certificate is
/// gone until its own next scheduled poll — worst case, several minutes
/// each, stacking to a wait long enough that an admin watching the log for
/// a few minutes reasonably concludes it isn't working, with nothing
/// logged in the meantime to say otherwise. This collapses the second half
/// of that wait to effectively zero.
/// </summary>
public interface IRegistrationWakeSignal
{
    /// <summary>Requests that a pending or future wait return immediately. Safe to call with no one currently waiting — the request isn't lost.</summary>
    void RequestImmediateCheck();

    /// <summary>Returns as soon as <see cref="RequestImmediateCheck"/> is called, or after <paramref name="timeout"/> elapses — whichever is first.</summary>
    Task WaitForWakeOrTimeoutAsync(TimeSpan timeout, CancellationToken ct = default);
}
