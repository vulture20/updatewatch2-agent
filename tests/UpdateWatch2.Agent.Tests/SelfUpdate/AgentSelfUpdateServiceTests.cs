using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.SelfUpdate;

namespace UpdateWatch2.Agent.Tests.SelfUpdate;

/// <summary>
/// Covers the testable half of self-update (updatewatch2-agent#14) — the
/// version/asset decision logic and the download-then-verify-checksum
/// steps — via hand-written <see cref="IServerClient"/>/<see cref="IPlatformUpdateApplier"/>
/// fakes, the same "no mocking library" convention every other test class
/// in this project follows. The untestable, OS-specific apply step itself
/// (<c>WindowsInstallerApplier</c>/<c>LinuxPackageApplier</c>) has no test
/// coverage here by design — same as <c>WuaUpdateSession</c>/
/// <c>AptUpdateSession</c>/<c>DnfUpdateSession</c>'s own install halves.
/// </summary>
public class AgentSelfUpdateServiceTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), $"uw2-agent-selfupdate-tests-{Guid.NewGuid()}");

    private const string FakeContent = "fake-installer-bytes";
    private static readonly string Sha256OfFakeContent = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(FakeContent)));
    private static readonly AgentUpdateAssetOffer SampleAsset = new("/api/agent/updates/setup.exe", Sha256OfFakeContent, SizeBytes: FakeContent.Length);

    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_returns_NotApplicable_and_downloads_nothing_when_the_offer_is_null()
    {
        var (serverClient, applier, service) = CreateService();

        var outcome = await service.ApplyAsync(null);

        Assert.Equal(SelfUpdateOutcome.NotApplicable, outcome);
        Assert.Equal(0, serverClient.DownloadCallCount);
        Assert.Equal(0, applier.ApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_returns_NotApplicable_when_the_offered_version_is_not_newer_than_the_current_one()
    {
        var (serverClient, applier, service) = CreateService();
        var offer = new AgentUpdateOffer("0.1.0", SampleAsset, null, null); // well below AgentVersion.Current

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.NotApplicable, outcome);
        Assert.Equal(0, serverClient.DownloadCallCount);
        Assert.Equal(0, applier.ApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_returns_NotApplicable_when_the_offer_has_no_asset_for_this_platform()
    {
        var (serverClient, applier, service) = CreateService();
        var offer = new AgentUpdateOffer("99.0.0", WindowsInstaller: null, LinuxDeb: SampleAsset, LinuxRpm: null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.NotApplicable, outcome);
        Assert.Equal(0, serverClient.DownloadCallCount);
        Assert.Equal(0, applier.ApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_downloads_verifies_and_applies_a_newer_offer_with_a_matching_checksum()
    {
        var (serverClient, applier, service) = CreateService();
        var offer = new AgentUpdateOffer("99.0.0", SampleAsset, null, null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.Applied, outcome);
        Assert.Equal(1, serverClient.DownloadCallCount);
        Assert.Equal(1, applier.ApplyCallCount);
        Assert.Equal("/api/agent/updates/setup.exe", serverClient.LastDownloadUrl);
        Assert.True(File.Exists(applier.LastAppliedPath));
        Assert.Equal(FakeContent, await File.ReadAllTextAsync(applier.LastAppliedPath!));
    }

    [Fact]
    public async Task ApplyAsync_returns_IntegrityCheckFailed_and_never_applies_when_the_checksum_does_not_match()
    {
        var (serverClient, applier, service) = CreateService();
        var tamperedAsset = SampleAsset with { Sha256 = "0000000000000000000000000000000000000000000000000000000000000000" };
        var offer = new AgentUpdateOffer("99.0.0", tamperedAsset, null, null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.IntegrityCheckFailed, outcome);
        Assert.Equal(0, applier.ApplyCallCount);
        Assert.Empty(Directory.EnumerateFiles(_stagingDirectory)); // the mismatched download was cleaned up
    }

    [Fact]
    public async Task ApplyAsync_returns_DownloadFailed_when_the_download_throws()
    {
        var (_, applier, service) = CreateService(onDownload: (_, _) => throw new HttpRequestException("simulated network failure"));
        var offer = new AgentUpdateOffer("99.0.0", SampleAsset, null, null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.DownloadFailed, outcome);
        Assert.Equal(0, applier.ApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_returns_ApplyFailed_when_the_platform_applier_returns_false()
    {
        var (_, applier, service) = CreateService(applierResult: false);
        var offer = new AgentUpdateOffer("99.0.0", SampleAsset, null, null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.ApplyFailed, outcome);
    }

    [Fact]
    public async Task ApplyAsync_returns_ApplyFailed_when_the_platform_applier_throws()
    {
        var (_, _, service) = CreateService(applierThrows: true);
        var offer = new AgentUpdateOffer("99.0.0", SampleAsset, null, null);

        var outcome = await service.ApplyAsync(offer);

        Assert.Equal(SelfUpdateOutcome.ApplyFailed, outcome);
    }

    private (FakeServerClient ServerClient, FakeApplier Applier, AgentSelfUpdateService Service) CreateService(
        Func<string, string, Task>? onDownload = null,
        bool applierResult = true,
        bool applierThrows = false)
    {
        var serverClient = new FakeServerClient(onDownload ?? WriteFakeContentAsync);
        var applier = new FakeApplier(applierResult, applierThrows);
        var service = new AgentSelfUpdateService(
            AgentUpdateAssetKind.WindowsInstaller, _stagingDirectory, serverClient, applier, NullLogger<AgentSelfUpdateService>.Instance);
        return (serverClient, applier, service);
    }

    private static Task WriteFakeContentAsync(string _, string destinationPath) => File.WriteAllTextAsync(destinationPath, FakeContent);

    private class FakeServerClient(Func<string, string, Task> onDownload) : IServerClient
    {
        public int DownloadCallCount { get; private set; }

        public string? LastDownloadUrl { get; private set; }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken ct = default)
        {
            DownloadCallCount++;
            LastDownloadUrl = downloadUrl;
            return onDownload(downloadUrl, destinationPath);
        }

        public Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<byte[]> FetchCaCertificateBundleAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AliveResult> SendAliveAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default) => throw new NotSupportedException();

        public Task AcknowledgeInstallAsync(InstallOutcome outcome, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private class FakeApplier(bool result, bool throws) : IPlatformUpdateApplier
    {
        public int ApplyCallCount { get; private set; }

        public string? LastAppliedPath { get; private set; }

        public Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct)
        {
            ApplyCallCount++;
            LastAppliedPath = downloadedFilePath;
            return throws
                ? throw new InvalidOperationException("simulated apply failure")
                : Task.FromResult(result);
        }
    }
}
