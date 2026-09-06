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
            return new UpdateCheckResult(Updates: [], RebootRequired: false);
        }
    }

    public async Task<InstallOutcome> InstallAsync(CancellationToken ct = default)
    {
        try
        {
            return await session.DownloadAndInstallAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Linux update install failed");
            return InstallOutcome.Failed;
        }
    }
}
