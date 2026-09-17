namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Used on non-Windows platforms until a real Linux checker exists (see
/// CLAUDE.md section 4.1). Always reports no updates found.
/// </summary>
public class NoOpUpdateChecker(ILogger<NoOpUpdateChecker> logger) : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        logger.LogWarning("No update checker is implemented for this platform yet ({Os}).", Environment.OSVersion.Platform);
        return Task.FromResult(new UpdateCheckResult(Updates: [], RebootRequired: false));
    }

    public Task<InstallResult> InstallAsync(IReadOnlyList<string>? packageIds, CancellationToken ct = default)
    {
        logger.LogWarning("No update installer is implemented for this platform yet ({Os}).", Environment.OSVersion.Platform);
        return Task.FromResult(new InstallResult(InstallOutcome.Succeeded));
    }

    // No-op, no warning logged — unlike CheckAsync/InstallAsync above,
    // pre-download is a best-effort optimization a caller opts into per
    // the server's own fleet-wide toggle, not something an admin expects
    // to actually work on every platform yet, so nothing here is worth
    // warning about every periodic check.
    public Task<PreDownloadResult> PreDownloadAsync(CancellationToken ct = default) =>
        Task.FromResult(new PreDownloadResult(true));

    // No-op, no warning logged — matching PreDownloadAsync's convention
    // above: nothing meaningful to check on a platform with no real
    // update checker, and this would otherwise warn on every heartbeat
    // (far more often than CheckAsync's own periodic warning).
    public Task<RebootCheckResult> CheckRebootRequiredAsync(CancellationToken ct = default) =>
        Task.FromResult(new RebootCheckResult(false));
}
