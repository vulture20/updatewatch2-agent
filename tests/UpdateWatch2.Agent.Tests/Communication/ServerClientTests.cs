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
            Content = JsonContent.Create(new { approved = false, registrationToken = "tok", certificate = (string?)null, protocolVersion = "0.6.0" }),
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

    [Fact]
    public async Task SendAliveAsync_parses_an_agent_update_offer_when_the_server_includes_one()
    {
        // updatewatch2-agent#14: the offer's shape (nested asset objects,
        // each independently nullable) must round-trip through
        // JsonSerializerDefaults.Web's case-insensitive camelCase matching
        // the same way installRequested/AliveRequest's other fields do.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                installRequested = false,
                agentUpdateAvailable = new
                {
                    version = "99.0.0",
                    windowsInstaller = new { downloadUrl = "/api/agent/updates/setup.exe", sha256 = "abc", sizeBytes = 123 },
                    linuxDeb = (object?)null,
                    linuxRpm = (object?)null,
                },
            }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.NotNull(result.AgentUpdateAvailable);
        Assert.Equal("99.0.0", result.AgentUpdateAvailable!.Version);
        Assert.NotNull(result.AgentUpdateAvailable.WindowsInstaller);
        Assert.Equal("/api/agent/updates/setup.exe", result.AgentUpdateAvailable.WindowsInstaller!.DownloadUrl);
        Assert.Null(result.AgentUpdateAvailable.LinuxDeb);
    }

    [Fact]
    public async Task SendAliveAsync_reports_no_agent_update_when_the_server_omits_the_field()
    {
        // An agent build this old still round-trips fine against a server
        // that doesn't send agentUpdateAvailable at all — the field is
        // additive, per the same reasoning installRequested's own addition
        // already established.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Null(result.AgentUpdateAvailable);
    }

    [Fact]
    public async Task DownloadFileAsync_writes_the_response_body_to_the_destination_path()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("fake-binary-content"),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, NullLogger<ServerClient>.Instance);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"uw2-agent-download-test-{Guid.NewGuid()}");

        try
        {
            await client.DownloadFileAsync("/api/agent/updates/setup.exe", destinationPath);

            Assert.Equal("fake-binary-content", await File.ReadAllTextAsync(destinationPath));
        }
        finally
        {
            File.Delete(destinationPath);
        }
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
