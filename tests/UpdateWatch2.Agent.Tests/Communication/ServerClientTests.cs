using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Communication;

namespace UpdateWatch2.Agent.Tests.Communication;

/// <summary>
/// Covers ServerClient's own wire behavior directly (not via a hand-written
/// IServerClient fake, unlike WorkerTests/RegistrationWorkerTests) — this is
/// the class that actually resolves this agent's IpAddress before it goes
/// out on the wire, which is worth verifying for real rather than assuming.
/// Uses a fake HttpMessageHandler to intercept the outgoing request instead
/// of a real server, matching this project's general preference for
/// hand-written fakes over a mocking library.
/// </summary>
public class ServerClientTests
{
    [Fact]
    public async Task RegisterAsync_reports_a_real_outbound_ip_address()
    {
        // The registration bug this covers (updatewatch2-agent, no issue
        // filed — reported directly as "IP address never shows up in the
        // admin overview"): RegisterAsync used to hardcode IpAddress: null
        // with a "TODO: resolve a real outbound-facing IP" comment, so the
        // field was never populated at all.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { approved = false, registrationToken = "tok", certificate = (string?)null, protocolVersion = "0.5.0" }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, NullLogger<ServerClient>.Instance);

        await client.RegisterAsync(registrationToken: null);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var ip = doc.RootElement.GetProperty("ipAddress").GetString();
        Assert.False(string.IsNullOrEmpty(ip));
    }

    [Fact]
    public async Task SendAliveAsync_reports_current_self_reported_metadata()
    {
        // updatewatch2-agent#6: the heartbeat, not just registration, is
        // what keeps IP/OS/DNS/version current for an already-certified
        // agent — RegisterAsync never runs again for one.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("ipAddress").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("dnsName").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("operatingSystem").GetString()));
        Assert.Equal(UpdateWatch2.Agent.AgentVersion.Current, doc.RootElement.GetProperty("agentVersion").GetString());
    }

    private class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
