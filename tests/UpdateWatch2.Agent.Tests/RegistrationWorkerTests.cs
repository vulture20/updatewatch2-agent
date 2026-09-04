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
            handler, () => serverClient, certificateState, NullLogger<RegistrationWorker>.Instance);

        await RunToCompletionAsync(worker);

        Assert.Equal(0, serverClient.FetchCaCertificateCallCount);
        Assert.Equal(0, serverClient.RegisterCallCount);
        Assert.True(certificateState.IsReady);
        Assert.Single(handler.SslOptions.ClientCertificates!);
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
            handler, () => serverClient, certificateState, NullLogger<RegistrationWorker>.Instance);

        await RunToCompletionAsync(worker, timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(1, serverClient.FetchCaCertificateCallCount);
        Assert.NotNull(new FileCaTrustStore(_caPath).Load());
        Assert.Equal(2, callCount);
        Assert.NotNull(certificateStore.Saved);
        Assert.Null(options.RegistrationToken); // cleared once delivered
        Assert.True(certificateState.IsReady);
        Assert.Single(handler.SslOptions.ClientCertificates!);
    }

    private static async Task RunToCompletionAsync(RegistrationWorker worker, TimeSpan? timeout = null)
    {
        await worker.StartAsync(CancellationToken.None);
        var executeTask = GetExecuteTask(worker);
        await executeTask!.WaitAsync(timeout ?? TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);
    }

    // BackgroundService doesn't expose ExecuteTask's completion in a way
    // StartAsync's returned Task reflects (StartAsync returns once
    // ExecuteAsync starts, not once it finishes) — reach the protected
    // ExecuteTask property via reflection rather than polling
    // IAgentCertificateState, so a failed/faulted run still surfaces here
    // instead of hanging until the timeout.
    private static Task? GetExecuteTask(Microsoft.Extensions.Hosting.BackgroundService worker) =>
        (Task?)typeof(Microsoft.Extensions.Hosting.BackgroundService)
            .GetProperty("ExecuteTask")!
            .GetValue(worker);

    private static X509Certificate2 CreateThrowawayCertificate(string subjectCn)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }

    private class FakeAgentConfigStore : IAgentConfigStore
    {
        public AgentOptions Load() => new();

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

        public X509Certificate2? Load() => Saved;

        public void Save(byte[] pfxBytes) => Saved = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);
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

        public Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default)
        {
            RegisterCallCount++;
            return Task.FromResult(onRegister?.Invoke(registrationToken)
                ?? new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: null));
        }

        public Task SendAliveAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default) => Task.CompletedTask;
    }
}
