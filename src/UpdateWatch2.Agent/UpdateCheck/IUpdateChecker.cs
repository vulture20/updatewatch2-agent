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
}
