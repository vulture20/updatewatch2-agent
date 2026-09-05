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

    public Task<InstallOutcome> InstallAsync(CancellationToken ct = default)
    {
        logger.LogWarning("No update installer is implemented for this platform yet ({Os}).", Environment.OSVersion.Platform);
        return Task.FromResult(InstallOutcome.Succeeded);
    }
}
