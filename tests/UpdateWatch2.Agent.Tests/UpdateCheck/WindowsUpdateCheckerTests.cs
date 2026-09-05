using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.UpdateCheck;
using UpdateWatch2.Agent.UpdateCheck.Windows;

namespace UpdateWatch2.Agent.Tests.UpdateCheck;

/// <summary>
/// Covers <see cref="WindowsUpdateChecker"/>'s own orchestration logic
/// against a hand-written fake <see cref="IWindowsUpdateSession"/> — the
/// real COM-backed <see cref="WuaUpdateSession"/> needs an actual Windows
/// Update Agent and is untested here (see its own doc comment), but the
/// checker itself has no Windows-specific code left, so it runs on this
/// project's Linux CI like everything else.
/// </summary>
public class WindowsUpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_returns_the_sessions_search_result()
    {
        var result = new UpdateCheckResult([new DetectedUpdate("Security Update", "KB123", "desc")], RebootRequired: true);
        var checker = new WindowsUpdateChecker(new FakeSession(searchResult: result), NullLogger<WindowsUpdateChecker>.Instance);

        var actual = await checker.CheckAsync();

        Assert.Same(result, actual);
    }

    [Fact]
    public async Task CheckAsync_reports_no_updates_when_the_session_throws()
    {
        var checker = new WindowsUpdateChecker(
            new FakeSession(onSearch: () => throw new InvalidOperationException("simulated WUApiLib failure")),
            NullLogger<WindowsUpdateChecker>.Instance);

        var actual = await checker.CheckAsync();

        Assert.Empty(actual.Updates);
        Assert.False(actual.RebootRequired);
    }

    [Fact]
    public async Task InstallAsync_returns_the_sessions_outcome()
    {
        var checker = new WindowsUpdateChecker(new FakeSession(installOutcome: InstallOutcome.Succeeded), NullLogger<WindowsUpdateChecker>.Instance);

        var actual = await checker.InstallAsync();

        Assert.Equal(InstallOutcome.Succeeded, actual);
    }

    [Fact]
    public async Task InstallAsync_reports_failure_when_the_session_throws()
    {
        var checker = new WindowsUpdateChecker(
            new FakeSession(onInstall: () => throw new InvalidOperationException("simulated WUApiLib failure")),
            NullLogger<WindowsUpdateChecker>.Instance);

        var actual = await checker.InstallAsync();

        Assert.Equal(InstallOutcome.Failed, actual);
    }

    private class FakeSession(
        UpdateCheckResult? searchResult = null,
        InstallOutcome installOutcome = InstallOutcome.Succeeded,
        Func<UpdateCheckResult>? onSearch = null,
        Func<InstallOutcome>? onInstall = null) : IWindowsUpdateSession
    {
        public UpdateCheckResult SearchForUpdates(CancellationToken ct) =>
            onSearch is not null ? onSearch() : searchResult ?? new UpdateCheckResult([], RebootRequired: false);

        public InstallOutcome DownloadAndInstall(CancellationToken ct) =>
            onInstall is not null ? onInstall() : installOutcome;
    }
}
