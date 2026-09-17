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

    // Real apt/dnf pre-downloading (agent v1.0.16, at the user's explicit
    // request — "Setze den Pre-Download auch für Linux um."), mirroring
    // WindowsUpdateChecker.PreDownloadAsync's own try/catch shape exactly.
    public async Task<PreDownloadResult> PreDownloadAsync(CancellationToken ct = default)
    {
        try
        {
            return await session.DownloadOnlyAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Pre-downloading Linux updates failed");
            return new PreDownloadResult(false, ex.Message);
        }
    }

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
