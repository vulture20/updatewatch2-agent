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
    /// Installs whatever this checker currently finds pending — remote-
    /// triggered by an admin, delivered to this agent via its alive
    /// heartbeat poll (updatewatch2-server#10/updatewatch2-agent#4).
    /// <paramref name="packageIds"/> is an admin's way to install only
    /// some pending updates while sparing others: null (the original,
    /// still-supported behavior) installs everything currently pending; a
    /// non-null list restricts installation to updates whose own
    /// <c>PackageId</c> is in it — see the concrete implementations for
    /// how each platform matches that back against what it independently
    /// re-finds pending, and how an update with no derivable identifier at
    /// all is handled. Must never trigger a reboot itself (CLAUDE.md's
    /// "update installation never triggers a reboot itself" rule) —
    /// <c>RebootRequired</c> stays a separate signal reported through the
    /// normal <see cref="CheckAsync"/>/report-updates cycle, unrelated to
    /// this call's own outcome.
    /// </summary>
    Task<InstallResult> InstallAsync(IReadOnlyList<string>? packageIds, CancellationToken ct = default);

    /// <summary>
    /// Proactively downloads whatever this checker currently finds pending,
    /// without installing it — driven by <c>UpdateCheckWorker</c> right
    /// after its own periodic <see cref="CheckAsync"/>/report succeeds, only
    /// when the server's admin-configurable pre-download setting is
    /// currently enabled for this agent's own platform (see
    /// <c>UpdateCheck.IPreDownloadPolicyState</c>). Implemented for both
    /// Windows (<c>WindowsUpdateChecker</c>/COM's <c>DownloadOnly</c>) and
    /// Linux (<c>LinuxUpdateChecker</c>/apt's <c>--download-only</c>, dnf's
    /// <c>--downloadonly</c> — agent v1.0.16); <c>NoOpUpdateChecker</c> (a
    /// Linux host with neither known package manager) implements this as a
    /// no-op that always reports success, since there's nothing to do at
    /// all on that host. Never installs anything (see <see cref="InstallAsync"/>'s
    /// own reboot-related rule — this method doesn't even touch that rule,
    /// since it never runs an installer at all).
    /// </summary>
    Task<PreDownloadResult> PreDownloadAsync(CancellationToken ct = default);

    /// <summary>
    /// A lightweight, standalone check of the same "a restart is needed"
    /// signal <see cref="CheckAsync"/> already reports as part of a full
    /// search — decoupled so <c>HeartbeatWorker</c> can poll it on its own,
    /// far shorter cadence instead of waiting for <see cref="CheckAsync"/>'s
    /// coarser one, at the user's explicit request ("Der Check, ob ein
    /// Neustart nötig ist, sollte öfter stattfinden."). Cheap on every
    /// platform (a cached COM property read on Windows, a file-existence
    /// check on Debian-derived Linux, a local subprocess spawn on RPM-based
    /// Linux) — what was actually expensive was piggybacking it on the full,
    /// much slower update search, not the check itself. Not to be confused
    /// with <c>Reboot.IAgentRebooter</c> — that's an admin *triggering* a
    /// reboot; this is the agent *detecting* that one is already needed.
    /// </summary>
    Task<RebootCheckResult> CheckRebootRequiredAsync(CancellationToken ct = default);
}
