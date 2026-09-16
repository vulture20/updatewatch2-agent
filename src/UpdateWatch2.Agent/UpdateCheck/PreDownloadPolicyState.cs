namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// A plain <c>volatile bool</c> is enough here — a single writer
/// (<see cref="HeartbeatWorker"/>) and a single reader (<see cref="UpdateCheckWorker"/>)
/// on two independent periodic loops, with no ordering requirement between
/// them beyond "eventually consistent." Unlike
/// <see cref="Certificates.AgentCertificateState"/> (the closest existing
/// precedent for a small shared cross-worker state singleton), there's no
/// async wait to coordinate here — a stale read for up to one heartbeat
/// interval is a fully acceptable, self-correcting staleness window, not a
/// correctness bug.
/// </summary>
public class PreDownloadPolicyState : IPreDownloadPolicyState
{
    private volatile bool _enabled;

    public bool Enabled => _enabled;

    public void Update(bool enabled) => _enabled = enabled;
}
