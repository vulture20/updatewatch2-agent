using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.Protocol;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.UpdateCheck;
using CheckerInstallOutcome = UpdateWatch2.Agent.UpdateCheck.InstallOutcome;
using WireInstallOutcome = UpdateWatch2.Agent.Communication.InstallOutcome;

namespace UpdateWatch2.Agent.Tests;

public class WorkerTests
{
    [Fact]
    public async Task UpdateCheckWorker_reports_the_checkers_findings_to_the_server()
    {
        var cts = new CancellationTokenSource();
        ReportUpdatesRequest? reported = null;

        var checker = new FakeUpdateChecker(new UpdateCheckResult(
            [new DetectedUpdate("Security Update", "KB123", "desc")], RebootRequired: true));
        var client = new FakeServerClient(onReportUpdates: request =>
        {
            reported = request;
            cts.Cancel(); // stop the worker's loop after the first report
        });

        var worker = new UpdateCheckWorker(
            new AgentOptions { UpdateCheckIntervalMinutes = 60, UpdateCheckJitterSeconds = 10 },
            checker, client, ReadyCertificateState(), NullLogger<UpdateCheckWorker>.Instance);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.NotNull(reported);
        Assert.True(reported.RebootRequired);
        var update = Assert.Single(reported.Updates);
        Assert.Equal("Security Update", update.Title);
        Assert.Equal("KB123", update.PackageId);
    }

    [Fact]
    public async Task UpdateCheckWorker_makes_no_calls_until_the_certificate_state_is_ready()
    {
        var certificateState = new AgentCertificateState();
        var reportedBeforeReady = false;
        var client = new FakeServerClient(onReportUpdates: _ => reportedBeforeReady = true);
        var checker = new FakeUpdateChecker(new UpdateCheckResult([], RebootRequired: false));

        var worker = new UpdateCheckWorker(
            new AgentOptions { UpdateCheckIntervalMinutes = 60, UpdateCheckJitterSeconds = 1 },
            checker, client, certificateState, NullLogger<UpdateCheckWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        Assert.False(reportedBeforeReady);
    }

    [Fact]
    public async Task HeartbeatWorker_sends_an_alive_message()
    {
        var cts = new CancellationTokenSource();
        var aliveCount = 0;

        var client = new FakeServerClient(onSendAlive: () =>
        {
            aliveCount++;
            cts.Cancel();
        });

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState());

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(1, aliveCount);
    }

    [Fact]
    public async Task HeartbeatWorker_makes_no_calls_until_the_certificate_state_is_ready()
    {
        var certificateState = new AgentCertificateState();
        var sentBeforeReady = false;
        var client = new FakeServerClient(onSendAlive: () => sentBeforeReady = true);

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, certificateState);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        Assert.False(sentBeforeReady);
    }

    [Fact]
    public async Task HeartbeatWorker_warns_when_the_servers_protocol_version_differs()
    {
        var cts = new CancellationTokenSource();
        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(), // stop the worker's loop after the first tick
            onFetchVersion: () => new VersionResponse("1.0.0", "9.9.9", "1.0.0")); // deliberately not ProtocolVersion.Current
        var logger = new CapturingLogger<HeartbeatWorker>();

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), logger);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Contains(logger.Warnings, message => message.Contains("Protocol version mismatch"));
    }

    [Fact]
    public async Task HeartbeatWorker_does_not_warn_when_the_servers_protocol_version_matches()
    {
        var cts = new CancellationTokenSource();
        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onFetchVersion: () => new VersionResponse("1.0.0", ProtocolVersion.Current, "1.0.0"));
        var logger = new CapturingLogger<HeartbeatWorker>();

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), logger);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.DoesNotContain(logger.Warnings, message => message.Contains("Protocol version mismatch"));
    }

    [Fact]
    public async Task HeartbeatWorker_renews_a_certificate_within_the_lead_time_and_hot_swaps_the_handler()
    {
        var cts = new CancellationTokenSource();
        var oldCertificate = CreateThrowawayCertificate("expiring-soon", DateTimeOffset.UtcNow.AddDays(-700), DateTimeOffset.UtcNow.AddDays(5));
        var newCertificate = CreateThrowawayCertificate("renewed", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(730));
        var newPfxBase64 = Convert.ToBase64String(newCertificate.Export(X509ContentType.Pfx));
        var certificateStore = new FakeClientCertificateStore(existing: oldCertificate);
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [oldCertificate] } };

        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onRenewCertificate: () => new RenewCertificateResult(true, newPfxBase64));

        var worker = CreateHeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 60, CertificateRenewalLeadTimeDays = 60 },
            client, ReadyCertificateState(), certificateStore: certificateStore, sharedHttpHandler: handler);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(1, client.RenewCertificateCallCount);
        Assert.Contains(oldCertificate.GetCertHashString(HashAlgorithmName.SHA256), certificateStore.DeletedThumbprints);
        var remaining = Assert.Single(handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());
        Assert.Equal(newCertificate.GetCertHashString(HashAlgorithmName.SHA256), remaining.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task HeartbeatWorker_does_not_renew_a_certificate_outside_the_lead_time()
    {
        var cts = new CancellationTokenSource();
        var farFromExpiry = CreateThrowawayCertificate("plenty-of-time", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(700));
        var certificateStore = new FakeClientCertificateStore(existing: farFromExpiry);
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [farFromExpiry] } };

        var client = new FakeServerClient(onSendAlive: () => cts.Cancel());

        var worker = CreateHeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 60, CertificateRenewalLeadTimeDays = 60 },
            client, ReadyCertificateState(), certificateStore: certificateStore, sharedHttpHandler: handler);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(0, client.RenewCertificateCallCount);
    }

    [Fact]
    public async Task HeartbeatWorker_merges_a_newly_published_CA_root_into_this_agents_trust_store()
    {
        // updatewatch2-server#6: this agent must pre-trust a pending root
        // BEFORE an admin activates a rotation, since activation is the
        // exact moment the server's own leaf switches to it.
        var cts = new CancellationTokenSource();
        using var alreadyTrustedRoot = CreateThrowawayCertificate("already-trusted-root", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3650));
        using var pendingRoot = CreateThrowawayCertificate("pending-root", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3650));
        var trustStore = new FileCaTrustStore(Path.Combine(Path.GetTempPath(), $"uw2-agent-tests-catrust-{Guid.NewGuid()}.pem"));
        trustStore.Save(alreadyTrustedRoot.Export(X509ContentType.Cert));

        var bundle = new X509Certificate2Collection { alreadyTrustedRoot, pendingRoot };
        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onFetchCaCertificateBundle: () => bundle.Export(X509ContentType.Pkcs7)!);

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), caTrustStore: trustStore);

        await RunUntilCancelledAsync(worker, cts.Token);

        var trusted = trustStore.LoadAll().Cast<X509Certificate2>().Select(c => c.GetCertHashString(HashAlgorithmName.SHA256)).ToHashSet();
        Assert.Contains(alreadyTrustedRoot.GetCertHashString(HashAlgorithmName.SHA256), trusted);
        Assert.Contains(pendingRoot.GetCertHashString(HashAlgorithmName.SHA256), trusted);
    }

    [Fact]
    public async Task HeartbeatWorker_self_heals_after_two_consecutive_certificate_rejections()
    {
        // Direct proof of updatewatch2-server#11/updatewatch2-agent#5: an
        // admin reissuing a certificate this agent still has loaded (not
        // lost) surfaces as repeated 401/403s from SendAliveAsync — this
        // worker must notice and drop the now-untrusted certificate itself,
        // so RegistrationWorker's existing lost-certificate recovery path
        // picks it up (proven separately in RegistrationWorkerTests).
        var cts = new CancellationTokenSource();
        var certificate = CreateThrowawayCertificate("rejected-host", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(700));
        var certificateStore = new FakeClientCertificateStore(existing: certificate);
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [certificate] } };

        var client = new FakeServerClient(onSendAliveOutcome: callCount =>
        {
            if (callCount >= 2)
            {
                cts.Cancel();
            }
            return AliveOutcome.CertificateRejected;
        });

        var worker = CreateHeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 0 },
            client, ReadyCertificateState(), certificateStore: certificateStore, sharedHttpHandler: handler);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Contains(certificate.GetCertHashString(HashAlgorithmName.SHA256), certificateStore.DeletedThumbprints);
        Assert.Empty(handler.SslOptions.ClientCertificates!);
    }

    [Fact]
    public async Task HeartbeatWorker_does_not_self_heal_after_a_single_certificate_rejection()
    {
        var cts = new CancellationTokenSource();
        var certificate = CreateThrowawayCertificate("rejected-once-host", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(700));
        var certificateStore = new FakeClientCertificateStore(existing: certificate);
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [certificate] } };

        var client = new FakeServerClient(onSendAliveOutcome: _ =>
        {
            cts.Cancel(); // stop after the first tick
            return AliveOutcome.CertificateRejected;
        });

        var worker = CreateHeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 60 },
            client, ReadyCertificateState(), certificateStore: certificateStore, sharedHttpHandler: handler);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Empty(certificateStore.DeletedThumbprints);
        Assert.Single(handler.SslOptions.ClientCertificates!);
    }

    [Fact]
    public async Task HeartbeatWorker_resets_the_rejection_count_on_a_successful_heartbeat_in_between()
    {
        var cts = new CancellationTokenSource();
        var certificate = CreateThrowawayCertificate("intermittent-host", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(700));
        var certificateStore = new FakeClientCertificateStore(existing: certificate);
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [certificate] } };

        // Reject, succeed, reject — never two rejections in a row.
        //
        // cts.Cancel() below only unblocks RunUntilCancelledAsync's own
        // wait, not HeartbeatWorker's loop itself — a BackgroundService
        // runs on its own internally-managed cancellation token, created
        // inside StartAsync, which this test's `cts` was never linked to.
        // The loop only actually stops once RunUntilCancelledAsync's
        // `finally` calls worker.StopAsync(), and with
        // AliveIntervalMinutes = 0 (a zero-duration Task.Delay between
        // ticks) one or more extra SendAliveAsync calls can race in
        // before that happens. Call 4+ must therefore never be another
        // CertificateRejected — that would land right after call 3's
        // rejection and spuriously trip the two-in-a-row threshold this
        // test asserts never fires. This raced and failed exactly that
        // way once in CI (a real, if infrequent, flake) before this
        // comment/fix.
        var client = new FakeServerClient(onSendAliveOutcome: callCount =>
        {
            if (callCount == 3)
            {
                cts.Cancel();
            }
            return callCount switch
            {
                1 or 3 => AliveOutcome.CertificateRejected,
                _ => AliveOutcome.Success,
            };
        });

        var worker = CreateHeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 0 },
            client, ReadyCertificateState(), certificateStore: certificateStore, sharedHttpHandler: handler);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Empty(certificateStore.DeletedThumbprints);
    }

    [Fact]
    public async Task HeartbeatWorker_invokes_the_installer_and_acknowledges_success_when_an_install_is_requested()
    {
        var cts = new CancellationTokenSource();
        var checker = new FakeUpdateChecker(new UpdateCheckResult([], RebootRequired: false), onInstall: () => CheckerInstallOutcome.Succeeded);
        WireInstallOutcome? acknowledged = null;

        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onInstallRequested: _ => true,
            onAcknowledgeInstall: outcome => acknowledged = outcome);

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), updateChecker: checker);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(1, checker.InstallCallCount);
        Assert.Equal(1, client.AcknowledgeInstallCallCount);
        Assert.Equal(WireInstallOutcome.Succeeded, acknowledged);
    }

    [Fact]
    public async Task HeartbeatWorker_does_not_invoke_the_installer_when_no_install_is_requested()
    {
        var cts = new CancellationTokenSource();
        var checker = new FakeUpdateChecker(new UpdateCheckResult([], RebootRequired: false));
        var client = new FakeServerClient(onSendAlive: () => cts.Cancel(), onInstallRequested: _ => false);

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), updateChecker: checker);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(0, checker.InstallCallCount);
        Assert.Equal(0, client.AcknowledgeInstallCallCount);
    }

    [Fact]
    public async Task HeartbeatWorker_acknowledges_failure_when_the_installer_throws()
    {
        var cts = new CancellationTokenSource();
        var checker = new FakeUpdateChecker(
            new UpdateCheckResult([], RebootRequired: false),
            onInstall: () => throw new InvalidOperationException("simulated install failure"));
        WireInstallOutcome? acknowledged = null;

        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onInstallRequested: _ => true,
            onAcknowledgeInstall: outcome => acknowledged = outcome);

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), updateChecker: checker);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(WireInstallOutcome.Failed, acknowledged);
    }

    [Fact]
    public async Task HeartbeatWorker_applies_a_self_update_when_the_server_offers_a_newer_agent_version()
    {
        var cts = new CancellationTokenSource();
        var offer = new AgentUpdateOffer("99.0.0", new AgentUpdateAssetOffer("/api/agent/updates/setup.exe", "abc", 1), null, null);
        AgentUpdateOffer? appliedWith = null;

        var client = new FakeServerClient(
            onSendAlive: () => cts.Cancel(),
            onAgentUpdateAvailable: _ => offer);
        var selfUpdater = new FakeAgentSelfUpdater(onApply: o =>
        {
            appliedWith = o;
            return SelfUpdateOutcome.Applied;
        });

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), selfUpdater: selfUpdater);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(1, selfUpdater.ApplyCallCount);
        Assert.Same(offer, appliedWith);
    }

    [Fact]
    public async Task HeartbeatWorker_does_not_invoke_the_self_updater_when_no_agent_update_is_available()
    {
        var cts = new CancellationTokenSource();
        var client = new FakeServerClient(onSendAlive: () => cts.Cancel(), onAgentUpdateAvailable: _ => null);
        var selfUpdater = new FakeAgentSelfUpdater();

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), selfUpdater: selfUpdater);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(0, selfUpdater.ApplyCallCount);
    }

    [Fact]
    public async Task HeartbeatWorker_swallows_an_unexpected_self_update_failure_without_stopping_the_loop()
    {
        var cts = new CancellationTokenSource();
        var aliveCount = 0;
        var offer = new AgentUpdateOffer("99.0.0", new AgentUpdateAssetOffer("/api/agent/updates/setup.exe", "abc", 1), null, null);

        var client = new FakeServerClient(
            onSendAlive: () =>
            {
                aliveCount++;
                if (aliveCount >= 2)
                {
                    cts.Cancel();
                }
            },
            onAgentUpdateAvailable: _ => offer);
        var selfUpdater = new FakeAgentSelfUpdater(onApply: _ => throw new InvalidOperationException("simulated self-update failure"));

        var worker = CreateHeartbeatWorker(new AgentOptions { AliveIntervalMinutes = 0 }, client, ReadyCertificateState(), selfUpdater: selfUpdater);

        await RunUntilCancelledAsync(worker, cts.Token);

        // The next tick's alive call still happened — a self-update failure
        // must not take the whole heartbeat loop down with it.
        Assert.True(aliveCount >= 2);
    }

    private static HeartbeatWorker CreateHeartbeatWorker(
        AgentOptions options,
        IServerClient client,
        IAgentCertificateState certificateState,
        ILogger<HeartbeatWorker>? logger = null,
        IClientCertificateStore? certificateStore = null,
        SocketsHttpHandler? sharedHttpHandler = null,
        IUpdateChecker? updateChecker = null,
        FileCaTrustStore? caTrustStore = null,
        IAgentSelfUpdater? selfUpdater = null) =>
        new(options, client, certificateState,
            certificateStore ?? new FakeClientCertificateStore(existing: null),
            caTrustStore ?? new FileCaTrustStore(Path.Combine(Path.GetTempPath(), $"uw2-agent-tests-catrust-{Guid.NewGuid()}.pem")),
            sharedHttpHandler ?? new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } },
            updateChecker ?? new FakeUpdateChecker(new UpdateCheckResult([], RebootRequired: false)),
            selfUpdater ?? new FakeAgentSelfUpdater(),
            logger ?? NullLogger<HeartbeatWorker>.Instance);

    private static AgentCertificateState ReadyCertificateState()
    {
        var state = new AgentCertificateState();
        state.MarkReady();
        return state;
    }

    private static async Task RunUntilCancelledAsync(BackgroundService worker, CancellationToken ct)
    {
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // expected — the fake client cancels once it has been called.
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private class FakeUpdateChecker(UpdateCheckResult result, Func<CheckerInstallOutcome>? onInstall = null) : IUpdateChecker
    {
        public int InstallCallCount { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) => Task.FromResult(result);

        public Task<CheckerInstallOutcome> InstallAsync(CancellationToken ct = default)
        {
            InstallCallCount++;
            return onInstall is null ? Task.FromResult(CheckerInstallOutcome.Succeeded) : Task.FromResult(onInstall());
        }
    }

    private class FakeServerClient(
        Action<ReportUpdatesRequest>? onReportUpdates = null,
        Action? onSendAlive = null,
        Func<string?, RegisterResult>? onRegister = null,
        Func<VersionResponse>? onFetchVersion = null,
        Func<RenewCertificateResult>? onRenewCertificate = null,
        Func<int, AliveOutcome>? onSendAliveOutcome = null,
        Func<int, bool>? onInstallRequested = null,
        Action<WireInstallOutcome>? onAcknowledgeInstall = null,
        Func<byte[]>? onFetchCaCertificateBundle = null,
        Func<int, AgentUpdateOffer?>? onAgentUpdateAvailable = null,
        Func<string, string, Task>? onDownloadFile = null) : IServerClient
    {
        public int RenewCertificateCallCount { get; private set; }

        public int SendAliveCallCount { get; private set; }

        public int AcknowledgeInstallCallCount { get; private set; }

        public int DownloadFileCallCount { get; private set; }

        public Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());

        public Task<byte[]> FetchCaCertificateBundleAsync(CancellationToken ct = default) => Task.FromResult(onFetchCaCertificateBundle?.Invoke() ?? []);

        public Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default) =>
            Task.FromResult(onRegister?.Invoke(registrationToken)
                ?? new RegisterResult(Approved: true, RegistrationToken: null, Certificate: null, ProtocolVersion: null));

        public Task<AliveResult> SendAliveAsync(CancellationToken ct = default)
        {
            SendAliveCallCount++;
            onSendAlive?.Invoke();
            var outcome = onSendAliveOutcome?.Invoke(SendAliveCallCount) ?? AliveOutcome.Success;
            var installRequested = outcome == AliveOutcome.Success && (onInstallRequested?.Invoke(SendAliveCallCount) ?? false);
            var agentUpdateAvailable = outcome == AliveOutcome.Success ? onAgentUpdateAvailable?.Invoke(SendAliveCallCount) : null;
            return Task.FromResult(new AliveResult(outcome, installRequested, agentUpdateAvailable));
        }

        public Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
        {
            onReportUpdates?.Invoke(report);
            return Task.CompletedTask;
        }

        public Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default) =>
            Task.FromResult(onFetchVersion?.Invoke() ?? new VersionResponse("0.0.0", ProtocolVersion.Current, "0.0.0"));

        public Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default)
        {
            RenewCertificateCallCount++;
            return Task.FromResult(onRenewCertificate?.Invoke() ?? new RenewCertificateResult(false, null));
        }

        public Task AcknowledgeInstallAsync(WireInstallOutcome outcome, CancellationToken ct = default)
        {
            AcknowledgeInstallCallCount++;
            onAcknowledgeInstall?.Invoke(outcome);
            return Task.CompletedTask;
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken ct = default)
        {
            DownloadFileCallCount++;
            return onDownloadFile?.Invoke(downloadUrl, destinationPath) ?? Task.CompletedTask;
        }
    }

    private class FakeAgentSelfUpdater(Func<AgentUpdateOffer, SelfUpdateOutcome>? onApply = null) : IAgentSelfUpdater
    {
        public int ApplyCallCount { get; private set; }

        public Task<SelfUpdateOutcome> ApplyAsync(AgentUpdateOffer? offer, CancellationToken ct = default)
        {
            if (offer is null)
            {
                return Task.FromResult(SelfUpdateOutcome.NotApplicable);
            }

            ApplyCallCount++;
            return Task.FromResult(onApply?.Invoke(offer) ?? SelfUpdateOutcome.Applied);
        }
    }

    private class FakeClientCertificateStore(X509Certificate2? existing) : IClientCertificateStore
    {
        public X509Certificate2? Saved { get; private set; } = existing;

        public List<string> DeletedThumbprints { get; } = [];

        public X509Certificate2? Load() => Saved;

        public void Save(byte[] pfxBytes) => Saved = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);

        public void Delete(string thumbprintSha256) => DeletedThumbprints.Add(thumbprintSha256);
    }

    private static X509Certificate2 CreateThrowawayCertificate(string subjectCn, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }

    /// <summary>Captures formatted log messages by level — the built-in NullLogger discards them, so tests that need to assert on log content (not just behavior) need this instead.</summary>
    private class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
