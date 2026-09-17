namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// The most recently known value of the server's admin-configurable
/// pre-download-updates toggle FOR THIS AGENT'S OWN PLATFORM, shared
/// between <see cref="HeartbeatWorker"/> (which writes it, resolving
/// whichever of the <c>alive</c> response's two platform-specific fields —
/// <c>PreDownloadWindowsUpdatesEnabled</c>/<c>PreDownloadLinuxUpdatesEnabled</c>
/// — actually applies here, via a single <c>OperatingSystem.IsWindows()</c>
/// check) and <see cref="UpdateCheckWorker"/> (which reads it, right after
/// its own periodic search/report succeeds, to decide whether to also call
/// <see cref="IUpdateChecker.PreDownloadAsync"/>). Deliberately kept a
/// single platform-agnostic bool rather than exposing both wire fields
/// here — the platform selection is HeartbeatWorker's concern alone, so
/// UpdateCheckWorker and every <see cref="IUpdateChecker"/> implementation
/// stay unaware there are two toggles on the wire at all. Deliberately not
/// folded into the <c>alive</c> response itself as UpdateCheckWorker's own
/// trigger — the flag arrives on HeartbeatWorker's much shorter cadence
/// (AliveIntervalMinutes), while the actual search this piggybacks on only
/// happens on UpdateCheckWorker's own coarser cadence
/// (UpdateCheckIntervalMinutes) — polling the server on the short cadence
/// just for this one boolean would mean an extra search every few minutes
/// for no benefit.
/// </summary>
public interface IPreDownloadPolicyState
{
    bool Enabled { get; }

    void Update(bool enabled);
}
