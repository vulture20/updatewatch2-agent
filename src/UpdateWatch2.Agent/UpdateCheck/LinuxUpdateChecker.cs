using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Orchestrates a Linux update check/install through
/// <see cref="ILinuxUpdateSession"/> — the same
/// testable-orchestrator/untestable-OS-session split
/// <see cref="WindowsUpdateChecker"/> already established, and for the
/// same reason: this class has no package-manager-specific code of its
/// own, only the injected session does (<see cref="AptUpdateSession"/>/
/// <see cref="DnfUpdateSession"/>, which carry the
/// <c>[SupportedOSPlatform("linux")]</c> attribute), so it gets real
/// <c>dotnet test</c> coverage against a hand-written fake session
/// instead of the zero coverage a platform-specific class normally has.
/// </summary>
public class LinuxUpdateChecker(ILinuxUpdateSession session, ILogger<LinuxUpdateChecker> logger) : IUpdateChecker
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            return await session.SearchForUpdatesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Linux update search failed");
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    public async Task<InstallResult> InstallAsync(IReadOnlyList<string>? packageIds, CancellationToken ct = default)
    {
        try
        {
            return await session.DownloadAndInstallAsync(packageIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Linux update install failed");
            return new InstallResult(InstallOutcome.Failed, ex.Message);
        }
    }

    // No-op for now — pre-downloading via apt/dnf isn't implemented yet
    // (the feature this supports, updatewatch2-server's Pre-download
    // Windows updates toggle, is scoped Windows-only for its first
    // version). Always reports success rather than an error, matching
    // NoOpUpdateChecker's own "nothing to do here" convention, so a
    // Linux agent doesn't log a spurious warning every periodic check
    // just because the server enabled a fleet-wide toggle this platform
    // doesn't act on yet.
    public Task<PreDownloadResult> PreDownloadAsync(CancellationToken ct = default) =>
        Task.FromResult(new PreDownloadResult(true));

    public async Task<RebootCheckResult> CheckRebootRequiredAsync(CancellationToken ct = default)
    {
        try
        {
            return await session.IsRebootRequiredAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Checking whether a reboot is required failed");
            return RebootCheckResult.Failed(ex.Message);
        }
    }
}
