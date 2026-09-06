using UpdateWatch2.Agent.Communication;

namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// Reacts to a server-offered newer agent version (updatewatch2-agent#14,
/// matching updatewatch2-server#14) by downloading it from the server —
/// never GitHub directly, since <see cref="AgentUpdateAssetOffer.DownloadUrl"/>
/// already points at this agent's own trusted server, per the design
/// decision pinned on updatewatch2-server#14 — and applying it.
///
/// <para>
/// See <see cref="AgentSelfUpdateService"/> for the testable decision logic
/// (is the offer actually newer than <c>AgentVersion.Current</c>? does it
/// carry an asset for this platform? does the download pass its SHA-256
/// check?) and <see cref="IPlatformUpdateApplier"/> for the untestable,
/// OS-specific apply step that gets delegated to — the same testable-
/// orchestrator/untestable-OS-action split <c>WindowsUpdateChecker</c>/
/// <c>WuaUpdateSession</c> and <c>LinuxUpdateChecker</c>/
/// <c>{Apt,Dnf}UpdateSession</c> already use for OS-update checking.
/// </para>
/// </summary>
public interface IAgentSelfUpdater
{
    /// <summary>
    /// Applies <paramref name="offer"/> if it's actually newer than this
    /// agent's own version and carries an asset for this platform; a no-op
    /// (<see cref="SelfUpdateOutcome.NotApplicable"/>) otherwise, including
    /// when <paramref name="offer"/> itself is null. No acknowledgement
    /// call back to the server is made — see this class's own design note
    /// pinned on updatewatch2-agent#14: the server naturally stops
    /// offering an update the moment this agent's next successful
    /// heartbeat, after applying it, self-reports the new
    /// <c>AgentVersion</c> (updatewatch2-agent#6).
    /// </summary>
    Task<SelfUpdateOutcome> ApplyAsync(AgentUpdateOffer? offer, CancellationToken ct = default);
}
