namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// The untestable, OS-specific half of self-update
/// (<see cref="AgentSelfUpdateService"/> is the testable orchestrator that
/// downloads and integrity-checks an update before ever calling this) —
/// see <c>Windows.WindowsInstallerApplier</c> and
/// <c>Linux.LinuxPackageApplier</c>.
/// </summary>
public interface IPlatformUpdateApplier
{
    /// <summary>
    /// Applies an already-downloaded, SHA-256-verified update artifact at
    /// <paramref name="downloadedFilePath"/>. True means the platform-
    /// specific apply step was successfully launched/executed — this
    /// agent's own process may not survive long after that (a service
    /// restart replacing its running binary is the whole point), so a
    /// caller must treat "true" as "handed off", not "confirmed complete".
    /// </summary>
    Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct);
}
