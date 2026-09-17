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
        // Must be distinguishable from "searched and found nothing", or
        // UpdateCheckWorker would report it to the server as if it were
        // real data — see WindowsUpdateCheckerTests' identical assertion
        // for the real incident this guards against.
        Assert.False(actual.Success);
        Assert.Equal("simulated apt failure", actual.ErrorDetail);
    }

    [Fact]
    public async Task InstallAsync_returns_the_sessions_outcome()
    {
        var checker = new LinuxUpdateChecker(new FakeSession(installResult: new InstallResult(InstallOutcome.Succeeded)), NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.InstallAsync(packageIds: null);

        Assert.Equal(InstallOutcome.Succeeded, actual.Outcome);
    }

    [Fact]
    public async Task InstallAsync_reports_failure_and_the_exception_message_when_the_session_throws()
    {
        var checker = new LinuxUpdateChecker(
            new FakeSession(onInstall: _ => throw new InvalidOperationException("simulated apt failure")),
            NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.InstallAsync(packageIds: null);

        Assert.Equal(InstallOutcome.Failed, actual.Outcome);
        Assert.Equal("simulated apt failure", actual.ErrorDetail);
    }

    [Fact]
    public async Task InstallAsync_passes_the_requested_package_names_through_to_the_session()
    {
        IReadOnlyList<string>? received = null;
        var session = new FakeSession(onInstall: names =>
        {
            received = names;
            return new InstallResult(InstallOutcome.Succeeded);
        });
        var checker = new LinuxUpdateChecker(session, NullLogger<LinuxUpdateChecker>.Instance);

        await checker.InstallAsync(["bash"]);

        Assert.Equal(["bash"], received);
    }

    [Fact]
    public async Task PreDownloadAsync_returns_the_sessions_result()
    {
        var result = new PreDownloadResult(true);
        var checker = new LinuxUpdateChecker(new FakeSession(preDownloadResult: result), NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.PreDownloadAsync();

        Assert.Same(result, actual);
    }

    [Fact]
    public async Task PreDownloadAsync_reports_failure_and_the_exception_message_when_the_session_throws()
    {
        var checker = new LinuxUpdateChecker(
            new FakeSession(onPreDownload: () => throw new InvalidOperationException("simulated apt failure")),
            NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.PreDownloadAsync();

        Assert.False(actual.Success);
        Assert.Equal("simulated apt failure", actual.ErrorDetail);
    }

    [Fact]
    public async Task CheckRebootRequiredAsync_returns_the_sessions_result()
    {
        var result = new RebootCheckResult(true);
        var checker = new LinuxUpdateChecker(new FakeSession(rebootCheckResult: result), NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.CheckRebootRequiredAsync();

        Assert.Same(result, actual);
    }

    [Fact]
    public async Task CheckRebootRequiredAsync_reports_failure_and_the_exception_message_when_the_session_throws()
    {
        var checker = new LinuxUpdateChecker(
            new FakeSession(onRebootCheck: () => throw new InvalidOperationException("simulated apt failure")),
            NullLogger<LinuxUpdateChecker>.Instance);

        var actual = await checker.CheckRebootRequiredAsync();

        Assert.False(actual.Success);
        Assert.Equal("simulated apt failure", actual.ErrorDetail);
    }

    private class FakeSession(
        UpdateCheckResult? searchResult = null,
        InstallResult? installResult = null,
        RebootCheckResult? rebootCheckResult = null,
        PreDownloadResult? preDownloadResult = null,
        Func<UpdateCheckResult>? onSearch = null,
        Func<IReadOnlyList<string>?, InstallResult>? onInstall = null,
        Func<RebootCheckResult>? onRebootCheck = null,
        Func<PreDownloadResult>? onPreDownload = null) : ILinuxUpdateSession
    {
        public Task<UpdateCheckResult> SearchForUpdatesAsync(CancellationToken ct) =>
            Task.FromResult(onSearch is not null ? onSearch() : searchResult ?? new UpdateCheckResult([], RebootRequired: false));

        public Task<InstallResult> DownloadAndInstallAsync(IReadOnlyList<string>? packageNames, CancellationToken ct) =>
            Task.FromResult(onInstall is not null ? onInstall(packageNames) : installResult ?? new InstallResult(InstallOutcome.Succeeded));

        public Task<RebootCheckResult> IsRebootRequiredAsync(CancellationToken ct) =>
            Task.FromResult(onRebootCheck is not null ? onRebootCheck() : rebootCheckResult ?? new RebootCheckResult(false));

        public Task<PreDownloadResult> DownloadOnlyAsync(CancellationToken ct) =>
            Task.FromResult(onPreDownload is not null ? onPreDownload() : preDownloadResult ?? new PreDownloadResult(true));
    }
}
