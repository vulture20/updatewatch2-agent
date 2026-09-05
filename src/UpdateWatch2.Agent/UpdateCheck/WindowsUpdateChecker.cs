using UpdateWatch2.Agent.UpdateCheck.Windows;

namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Orchestrates a Windows update check/install through
/// <see cref="IWindowsUpdateSession"/> — deliberately NOT itself marked
/// <c>[SupportedOSPlatform("windows")]</c> even though it's only ever
/// registered on Windows (see Program.cs): this class has no direct COM/
/// Windows API calls of its own any more, only the injected session does
/// (<see cref="WuaUpdateSession"/>, which does carry that attribute). That
/// split is what lets this orchestration logic — mapping a search result,
/// turning an exception into a reported failure — run under `dotnet test`
/// on this project's Linux CI (ubuntu-latest) against a hand-written fake
/// session, the same fakes-not-mocks convention the rest of this test
/// suite already uses, rather than having zero coverage the way a
/// Windows-only class normally would on Linux CI.
/// </summary>
public class WindowsUpdateChecker(IWindowsUpdateSession session, ILogger<WindowsUpdateChecker> logger) : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                return session.SearchForUpdates(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Windows Update search failed");
                return new UpdateCheckResult(Updates: [], RebootRequired: false);
            }
        }, ct);

    public Task<InstallOutcome> InstallAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                return session.DownloadAndInstall(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Windows Update install failed");
                return InstallOutcome.Failed;
            }
        }, ct);
}
