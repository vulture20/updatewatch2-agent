namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// The most recently known value of the server's admin-configurable
/// pre-download-Windows-updates toggle, shared between <see cref="HeartbeatWorker"/>
/// (which writes it, from the field on every <c>alive</c> response) and
/// <see cref="UpdateCheckWorker"/> (which reads it, right after its own
/// periodic search/report succeeds, to decide whether to also call
/// <see cref="IUpdateChecker.PreDownloadAsync"/>). Deliberately not folded
/// into the <c>alive</c> response itself as UpdateCheckWorker's own trigger
/// — the flag arrives on HeartbeatWorker's much shorter cadence
/// (AliveIntervalMinutes), while the actual WUA search this piggybacks on
/// only happens on UpdateCheckWorker's own coarser cadence
/// (UpdateCheckIntervalMinutes) — polling the server on the short cadence
/// just for this one boolean would mean an extra WUA COM search every few
/// minutes for no benefit.
/// </summary>
public interface IPreDownloadPolicyState
{
    bool Enabled { get; }

    void Update(bool enabled);
}
