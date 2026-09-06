using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.UpdateCheck;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck;

/// <summary>
/// Covers <see cref="LinuxUpdateChecker"/>'s own orchestration logic
/// against a hand-written fake <see cref="ILinuxUpdateSession"/> — the
/// real apt/dnf-shelling-out sessions need an actual Linux package
/// manager and are untested here (see their own doc comments), but the
/// checker itself has no package-manager-specific code left, so it runs
/// on this project's CI like everything else.
/// </summary>
public class LinuxUpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_returns_the_sessions_search_result()
    {
        var result = new UpdateCheckResult([new DetectedUpdate("bash 5.2.15", "bash", "5.2.0 → 5.2.15")], RebootRequired: true);
        var checker = new LinuxUpdateChecker(new FakeSession(searchResult: result), NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.CheckAsync();

        Assert.Same(result, actual);
    }

    [Fact]
    public async Task CheckAsync_reports_no_updates_when_the_session_throws()
    {
        var checker = new LinuxUpdateChecker(
            new FakeSession(onSearch: () => throw new InvalidOperationException("simulated apt failure")),
            NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.CheckAsync();

        Assert.Empty(actual.Updates);
        Assert.False(actual.RebootRequired);
    }

    [Fact]
    public async Task InstallAsync_returns_the_sessions_outcome()
    {
        var checker = new LinuxUpdateChecker(new FakeSession(installOutcome: InstallOutcome.Succeeded), NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.InstallAsync();

        Assert.Equal(InstallOutcome.Succeeded, actual);
    }

    [Fact]
    public async Task InstallAsync_reports_failure_when_the_session_throws()
    {
        var checker = new LinuxUpdateChecker(
            new FakeSession(onInstall: () => throw new InvalidOperationException("simulated apt failure")),
            NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.InstallAsync();

        Assert.Equal(InstallOutcome.Failed, actual);
    }

    private class FakeSession(
        UpdateCheckResult? searchResult = null,
        InstallOutcome installOutcome = InstallOutcome.Succeeded,
        Func<UpdateCheckResult>? onSearch = null,
        Func<InstallOutcome>? onInstall = null) : ILinuxUpdateSession
    {
        public Task<UpdateCheckResult> SearchForUpdatesAsync(CancellationToken ct) =>
            Task.FromResult(onSearch is not null ? onSearch() : searchResult ?? new UpdateCheckResult([], RebootRequired: false));

        public Task<InstallOutcome> DownloadAndInstallAsync(CancellationToken ct) =>
            Task.FromResult(onInstall is not null ? onInstall() : installOutcome);
    }
}
