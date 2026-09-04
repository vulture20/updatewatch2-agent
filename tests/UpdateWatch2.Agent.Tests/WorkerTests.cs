using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.UpdateCheck;

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

        var worker = new HeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 60 }, client, ReadyCertificateState(), NullLogger<HeartbeatWorker>.Instance);

        await RunUntilCancelledAsync(worker, cts.Token);

        Assert.Equal(1, aliveCount);
    }

    [Fact]
    public async Task HeartbeatWorker_makes_no_calls_until_the_certificate_state_is_ready()
    {
        var certificateState = new AgentCertificateState();
        var sentBeforeReady = false;
        var client = new FakeServerClient(onSendAlive: () => sentBeforeReady = true);

        var worker = new HeartbeatWorker(
            new AgentOptions { AliveIntervalMinutes = 60 }, client, certificateState, NullLogger<HeartbeatWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        Assert.False(sentBeforeReady);
    }

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

    private class FakeUpdateChecker(UpdateCheckResult result) : IUpdateChecker
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) => Task.FromResult(result);
    }

    private class FakeServerClient(
        Action<ReportUpdatesRequest>? onReportUpdates = null,
        Action? onSendAlive = null,
        Func<string?, RegisterResult>? onRegister = null) : IServerClient
    {
        public Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());

        public Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default) =>
            Task.FromResult(onRegister?.Invoke(registrationToken)
                ?? new RegisterResult(Approved: true, RegistrationToken: null, Certificate: null, ProtocolVersion: null));

        public Task SendAliveAsync(CancellationToken ct = default)
        {
            onSendAlive?.Invoke();
            return Task.CompletedTask;
        }

        public Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
        {
            onReportUpdates?.Invoke(report);
            return Task.CompletedTask;
        }
    }
}
