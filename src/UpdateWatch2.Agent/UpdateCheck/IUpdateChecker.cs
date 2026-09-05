namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Searches for available updates and determines whether a reboot is
/// required — kept as a separate signal from installation, per CLAUDE.md
/// ("Der Agent ermittelt bzw. meldet zusätzlich, ob ein Neustart des
/// Systems erforderlich ist – getrennt von der eigentlichen
/// Installation.").
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Installs whatever this checker most recently found — remote-
    /// triggered by an admin, delivered to this agent via its alive
    /// heartbeat poll (updatewatch2-server#10/updatewatch2-agent#4). Must
    /// never trigger a reboot itself (CLAUDE.md's "update installation
    /// never triggers a reboot itself" rule) — <c>RebootRequired</c> stays
    /// a separate signal reported through the normal
    /// <see cref="CheckAsync"/>/report-updates cycle, unrelated to this
    /// call's own outcome.
    /// </summary>
    Task<InstallOutcome> InstallAsync(CancellationToken ct = default);
}
