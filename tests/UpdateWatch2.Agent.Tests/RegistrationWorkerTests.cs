using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Tests;

public class RegistrationWorkerTests : IDisposable
{
    private readonly string _caPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-regworker-ca-{Guid.NewGuid()}.pem");

    public void Dispose()
    {
        if (File.Exists(_caPath))
        {
            File.Delete(_caPath);
        }
    }

    [Fact]
    public async Task Skips_the_network_entirely_when_a_client_certificate_is_already_stored()
    {
        var certificateStore = new FakeClientCertificateStore(existing: CreateThrowawayCertificate("already-certified"));
        var serverClient = new FakeServerClient();
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var worker = new RegistrationWorker(
            new AgentOptions(), new FakeAgentConfigStore(), new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await RunUntilReadyAsync(worker, certificateState);

        Assert.Equal(0, serverClient.FetchCaCertificateCallCount);
        Assert.Equal(0, serverClient.RegisterCallCount);
        Assert.True(certificateState.IsReady);
        Assert.Single(handler.SslOptions.ClientCertificates!);
    }

    [Fact]
    public async Task Skips_fetching_the_CA_certificate_when_one_is_already_pinned_but_registration_still_proceeds()
    {
        // Simulates the NSIS /CACERT= switch (or a manually pre-staged
        // Linux ca.pem): the CA trust file is already present on disk
        // BEFORE this worker's very first tick, but no client certificate
        // has been issued yet. Distinct from
        // Skips_the_network_entirely_when_a_client_certificate_is_already_stored
        // above, which skips via a completely different branch (an
        // already-present *client* certificate) — this proves
        // EnsureCaPinnedAsync's own early-return specifically, while
        // registration itself still runs normally on top of it.
        new FileCaTrustStore(_caPath).Save(CreateThrowawayCertificate("Pre-seeded CA").Export(X509ContentType.Cert));

        var options = new AgentOptions { RegistrationRetryIntervalSeconds = 1 };
        var issuedCertificate = CreateThrowawayCertificate("freshly-approved-host");
        var issuedPfxBase64 = Convert.ToBase64String(issuedCertificate.Export(X509ContentType.Pfx));
        var serverClient = new FakeServerClient(
            onRegister: _ => new RegisterResult(Approved: true, RegistrationToken: null, Certificate: issuedPfxBase64, ProtocolVersion: "0.1.0"));
        var certificateStore = new FakeClientCertificateStore(existing: null);
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var worker = new RegistrationWorker(
            options, new FakeAgentConfigStore(), new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await RunUntilReadyAsync(worker, certificateState);

        Assert.Equal(0, serverClient.FetchCaCertificateCallCount);
        Assert.True(serverClient.RegisterCallCount >= 1);
        Assert.True(certificateState.IsReady);
        Assert.NotNull(certificateStore.Saved);
    }

    [Fact]
    public async Task Pins_the_CA_then_polls_until_approved_and_stores_the_certificate()
    {
        var configStore = new FakeAgentConfigStore();
        var options = new AgentOptions { RegistrationRetryIntervalSeconds = 1 };
        var issuedCertificate = CreateThrowawayCertificate("freshly-approved-host");
        var issuedPfxBase64 = Convert.ToBase64String(issuedCertificate.Export(X509ContentType.Pfx));
        var caBytes = CreateThrowawayCertificate("Test CA").Export(X509ContentType.Cert);

        var callCount = 0;
        var serverClient = new FakeServerClient(
            caBytes: caBytes,
            onRegister: token =>
            {
                callCount++;
                return callCount == 1
                    ? new RegisterResult(Approved: false, RegistrationToken: "server-issued-token", Certificate: null, ProtocolVersion: "0.1.0")
                    : new RegisterResult(Approved: true, RegistrationToken: null, Certificate: issuedPfxBase64, ProtocolVersion: "0.1.0");
            });
        var certificateStore = new FakeClientCertificateStore(existing: null);
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var worker = new RegistrationWorker(
            options, configStore, new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await RunUntilReadyAsync(worker, certificateState, timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(1, serverClient.FetchCaCertificateCallCount);
        Assert.NotNull(new FileCaTrustStore(_caPath).Load());
        Assert.Equal(2, callCount);
        Assert.NotNull(certificateStore.Saved);
        Assert.Null(options.RegistrationToken); // cleared once delivered
        Assert.True(certificateState.IsReady);
        Assert.Single(handler.SslOptions.ClientCertificates!);
    }

    [Fact]
    public async Task A_wake_signal_recovers_a_lost_certificate_promptly_instead_of_waiting_out_the_maintenance_interval()
    {
        // Regression test for a real user report: after an admin-mediated
        // reissuance while this agent kept running, reconnection sometimes
        // appeared to simply never happen. It does eventually — but only
        // once RegistrationWorker's own long CertificateMaintenanceIntervalSeconds
        // sleep happens to finish (worst case, several minutes on its own,
        // stacking on top of HeartbeatWorker's own self-heal delay), during
        // which nothing is logged to say recovery is even pending. This
        // proves IRegistrationWakeSignal actually collapses that wait: a
        // deliberately very long interval is used here specifically so a
        // passing test can only mean the signal — not the interval merely
        // elapsing — is what caused the prompt re-registration.
        var originalCertificate = CreateThrowawayCertificate("wake-signal-host");
        var certificateStore = new FakeClientCertificateStore(existing: originalCertificate);
        var configStore = new FakeAgentConfigStore();
        var options = new AgentOptions { CertificateMaintenanceIntervalSeconds = 3600, RegistrationRetryIntervalSeconds = 1 };
        var certificateState = new AgentCertificateState();
        var wakeSignal = new RegistrationWakeSignal();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var recoveredCertificate = CreateThrowawayCertificate("wake-signal-host-recovered");
        var recoveredPfxBase64 = Convert.ToBase64String(recoveredCertificate.Export(X509ContentType.Pfx));
        var serverClient = new FakeServerClient(onRegister: token =>
            token == "reissued-token"
                ? new RegisterResult(Approved: true, RegistrationToken: null, Certificate: recoveredPfxBase64, ProtocolVersion: "0.1.0")
                : new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: "0.1.0"));

        var worker = new RegistrationWorker(
            options, configStore, new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, wakeSignal, NullLogger<RegistrationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await certificateState.WaitUntilReadyAsync(TimeoutToken());

            // Same admin-recovery setup as the sibling test above, but the
            // wake signal — not a short interval — is what should make
            // this recover quickly.
            certificateStore.SimulateLoss();
            configStore.ToReturn = new AgentOptions { RegistrationToken = "reissued-token" };
            wakeSignal.RequestImmediateCheck();

            await WaitUntilAsync(
                () => certificateStore.Saved is not null && ReferenceEquals(certificateStore.Saved, certificateStore.Load()),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(
            recoveredCertificate.GetCertHashString(HashAlgorithmName.SHA256),
            certificateStore.Saved!.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task A_certificate_lost_after_successful_attachment_is_recovered_without_a_restart()
    {
        // This is the direct proof of "live, no restart" (updatewatch2-server#8):
        // the worker starts already certified, the local certificate then
        // disappears out from under it (simulating a wipe/corruption) while
        // the SAME worker instance keeps running, and a fresh token shows
        // up in the config store the way an admin's re-issuance would place
        // one — no new worker/process is ever started in this test.
        var originalCertificate = CreateThrowawayCertificate("lost-host");
        var certificateStore = new FakeClientCertificateStore(existing: originalCertificate);
        var configStore = new FakeAgentConfigStore();
        var options = new AgentOptions { CertificateMaintenanceIntervalSeconds = 1, RegistrationRetryIntervalSeconds = 1 };
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var recoveredCertificate = CreateThrowawayCertificate("lost-host-recovered");
        var recoveredPfxBase64 = Convert.ToBase64String(recoveredCertificate.Export(X509ContentType.Pfx));
        var serverClient = new FakeServerClient(onRegister: token =>
            token == "reissued-token"
                ? new RegisterResult(Approved: true, RegistrationToken: null, Certificate: recoveredPfxBase64, ProtocolVersion: "0.1.0")
                : new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: "0.1.0"));

        var worker = new RegistrationWorker(
            options, configStore, new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // First reach the idle, already-certified steady state.
            await certificateState.WaitUntilReadyAsync(TimeoutToken());

            // Simulate the certificate being lost, and an admin's
            // re-issuance token showing up in the config store — all while
            // this worker instance keeps running, never restarted.
            certificateStore.SimulateLoss();
            configStore.ToReturn = new AgentOptions { RegistrationToken = "reissued-token" };

            // Wait for the worker's own maintenance loop to notice, recover,
            // and re-attach — polling rather than a single wait, since
            // certificateState.IsReady is already true and won't transition
            // again.
            await WaitUntilAsync(() => certificateStore.Saved is not null && ReferenceEquals(certificateStore.Saved, certificateStore.Load()),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.NotNull(certificateStore.Saved);
        Assert.Equal(
            recoveredCertificate.GetCertHashString(HashAlgorithmName.SHA256),
            certificateStore.Saved!.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Single(handler.SslOptions.ClientCertificates!);
        var reattached = Assert.IsType<X509Certificate2>(handler.SslOptions.ClientCertificates![0]);
        Assert.Equal(recoveredCertificate.GetCertHashString(HashAlgorithmName.SHA256), reattached.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task A_fresh_registration_token_is_used_even_though_the_old_certificate_is_still_present_locally()
    {
        // Regression test: an admin re-issuing a certificate for an agent
        // that's still running fine (suspected compromise, not a lost/wiped
        // certificate) writes a fresh RegistrationToken into the config
        // store without also clearing ClientCertificateThumbprint — the
        // still-present old certificate used to mean this loop's
        // certificate-present branch never even looked at the new token,
        // so it just sat unused. The token itself must now be enough:
        // dropping the still-present old certificate and registering with
        // it, with no separate manual clear required.
        var oldCertificate = CreateThrowawayCertificate("still-healthy-host");
        var certificateStore = new FakeClientCertificateStore(existing: oldCertificate);
        var configStore = new FakeAgentConfigStore();
        var options = new AgentOptions { CertificateMaintenanceIntervalSeconds = 1, RegistrationRetryIntervalSeconds = 1 };
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var reissuedCertificate = CreateThrowawayCertificate("still-healthy-host-reissued");
        var reissuedPfxBase64 = Convert.ToBase64String(reissuedCertificate.Export(X509ContentType.Pfx));
        var serverClient = new FakeServerClient(onRegister: token =>
            token == "forced-reissue-token"
                ? new RegisterResult(Approved: true, RegistrationToken: null, Certificate: reissuedPfxBase64, ProtocolVersion: "0.1.0")
                : new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: "0.1.0"));

        var worker = new RegistrationWorker(
            options, configStore, new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // First reach the idle, already-certified steady state — the
            // old certificate is attached and nothing else has happened yet.
            await certificateState.WaitUntilReadyAsync(TimeoutToken());

            // The old certificate is deliberately left in place (no
            // SimulateLoss() call) — only a fresh token shows up, exactly
            // as an admin's forced re-issuance without a manual thumbprint
            // clear would look like.
            configStore.ToReturn = new AgentOptions { RegistrationToken = "forced-reissue-token" };

            await WaitUntilAsync(
                () => certificateStore.DeletedThumbprints.Contains(oldCertificate.GetCertHashString(HashAlgorithmName.SHA256)),
                TimeSpan.FromSeconds(5));
            await WaitUntilAsync(
                () => certificateStore.Saved is not null &&
                      certificateStore.Saved.GetCertHashString(HashAlgorithmName.SHA256) ==
                      reissuedCertificate.GetCertHashString(HashAlgorithmName.SHA256),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Contains(oldCertificate.GetCertHashString(HashAlgorithmName.SHA256), certificateStore.DeletedThumbprints);
        Assert.Single(handler.SslOptions.ClientCertificates!);
        var reattached = Assert.IsType<X509Certificate2>(handler.SslOptions.ClientCertificates![0]);
        Assert.Equal(reissuedCertificate.GetCertHashString(HashAlgorithmName.SHA256), reattached.GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task A_registration_attempt_that_times_out_is_logged_and_retried_instead_of_silently_ending_the_worker()
    {
        // Regression test for a real, live-diagnosed bug: HttpClient's own
        // request timeout surfaces as a TaskCanceledException — an
        // OperationCanceledException that has nothing to do with this
        // worker's own stoppingToken (that one is never cancelled here).
        // The old code caught it via a catch clause that excluded
        // OperationCanceledException unconditionally, letting it fall
        // through into an outer handler that silently ended the loop for
        // the rest of the process's life, with zero log output and no
        // further retries — exactly indistinguishable from "the agent is
        // just idling" from the outside.
        var configStore = new FakeAgentConfigStore();
        var options = new AgentOptions { RegistrationRetryIntervalSeconds = 1 };

        var callCount = 0;
        var serverClient = new FakeServerClient(
            caBytes: CreateThrowawayCertificate("Test CA").Export(X509ContentType.Cert),
            onRegister: _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // A CancellationTokenSource entirely unrelated to the
                    // worker's own stoppingToken — simulates HttpClient's
                    // internal request-timeout mechanism.
                    using var unrelatedTimeout = new CancellationTokenSource();
                    unrelatedTimeout.Cancel();
                    unrelatedTimeout.Token.ThrowIfCancellationRequested();
                }

                return new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: "0.1.0");
            });
        var certificateStore = new FakeClientCertificateStore(existing: null);
        var certificateState = new AgentCertificateState();
        using var handler = new SocketsHttpHandler { SslOptions = { ClientCertificates = [] } };

        var worker = new RegistrationWorker(
            options, configStore, new FileCaTrustStore(_caPath), certificateStore,
            handler, () => serverClient, certificateState, new RegistrationWakeSignal(), NullLogger<RegistrationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => callCount >= 2, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(callCount >= 2, "The worker should retry after a non-shutdown OperationCanceledException, not silently end the loop.");
    }

    private static CancellationToken TimeoutToken(TimeSpan? timeout = null) =>
        new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5)).Token;

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static async Task RunUntilReadyAsync(RegistrationWorker worker, AgentCertificateState certificateState, TimeSpan? timeout = null)
    {
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await certificateState.WaitUntilReadyAsync(TimeoutToken(timeout));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static X509Certificate2 CreateThrowawayCertificate(string subjectCn)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }

    private class FakeAgentConfigStore : IAgentConfigStore
    {
        /// <summary>
        /// What the NEXT <see cref="Load"/> call returns — settable
        /// mid-test to simulate an admin writing a fresh registration
        /// token into the real config file/registry while the worker's
        /// maintenance loop keeps polling it.
        /// </summary>
        public AgentOptions ToReturn { get; set; } = new();

        public AgentOptions Load() => ToReturn;

        public void Save(AgentOptions options)
        {
            // RegistrationWorker mutates the same AgentOptions instance it
            // was constructed with and re-saves it — nothing to persist
            // separately for these tests to observe beyond that instance.
        }
    }

    private class FakeClientCertificateStore(X509Certificate2? existing) : IClientCertificateStore
    {
        public X509Certificate2? Saved { get; private set; } = existing;

        public List<string> DeletedThumbprints { get; } = [];

        public X509Certificate2? Load() => Saved;

        public void Save(byte[] pfxBytes) => Saved = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);

        public void Delete(string thumbprintSha256) => DeletedThumbprints.Add(thumbprintSha256);

        /// <summary>Test-only: simulates the certificate disappearing out from under a running process (wipe, corruption).</summary>
        public void SimulateLoss() => Saved = null;
    }

    private class FakeServerClient(byte[]? caBytes = null, Func<string?, RegisterResult>? onRegister = null) : IServerClient
    {
        public int FetchCaCertificateCallCount { get; private set; }

        public int RegisterCallCount { get; private set; }

        public Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default)
        {
            FetchCaCertificateCallCount++;
            return Task.FromResult(caBytes ?? []);
        }

        public Task<byte[]> FetchCaCertificateBundleAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());

        public Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default)
        {
            RegisterCallCount++;
            return Task.FromResult(onRegister?.Invoke(registrationToken)
                ?? new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: null));
        }

        public Task<AliveResult> SendAliveAsync(CancellationToken ct = default) => Task.FromResult(AliveResult.From(AliveOutcome.Success));

        public Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default) => Task.CompletedTask;

        public Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default) =>
            Task.FromResult(new VersionResponse("0.0.0", "0.0.0", "0.0.0"));

        public Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default) =>
            Task.FromResult(new RenewCertificateResult(false, null));

        public Task AcknowledgeInstallAsync(InstallOutcome outcome, string? errorDetail, CancellationToken ct = default) => Task.CompletedTask;

        public Task AcknowledgeRestartAsync(RestartOutcome outcome, string? errorDetail, CancellationToken ct = default) => Task.CompletedTask;

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken ct = default) => Task.CompletedTask;
    }
}
