using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

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
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await client.RegisterAsync(registrationToken: null);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var ip = doc.RootElement.GetProperty("ipAddress").GetString();
        Assert.False(string.IsNullOrEmpty(ip));
    }

    [Fact]
    public async Task RegisterAsync_uses_HostnameOverride_in_the_route_when_set()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { approved = false, registrationToken = "tok", certificate = (string?)null, protocolVersion = "0.6.0" }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions { HostnameOverride = "renamed-host" }, NullLogger<ServerClient>.Instance);

        await client.RegisterAsync(registrationToken: null);

        Assert.Contains("/api/agents/renamed-host/register", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task SendAliveAsync_uses_HostnameOverride_in_the_route_when_set()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions { HostnameOverride = "renamed-host" }, NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.Contains("/api/agents/renamed-host/alive", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task SendAliveAsync_falls_back_to_the_OS_hostname_when_HostnameOverride_is_blank()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions { HostnameOverride = "  " }, NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.Contains($"/api/agents/{Environment.MachineName}/alive", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task SendAliveAsync_never_lets_HostnameOverride_affect_the_separately_reported_DnsName()
    {
        // AgentOptions.HostnameOverride's own doc comment: overriding the
        // identity/routing hostname must not also change what this agent
        // self-reports as its DnsName metadata — a genuinely different,
        // purely informational field.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions { HostnameOverride = "renamed-host" }, NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var dnsName = doc.RootElement.GetProperty("dnsName").GetString();
        Assert.DoesNotContain("renamed-host", dnsName);
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
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("ipAddress").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("dnsName").GetString()));
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("operatingSystem").GetString()));
        Assert.Equal(UpdateWatch2.Agent.AgentVersion.Current, doc.RootElement.GetProperty("agentVersion").GetString());
    }

    [Fact]
    public async Task SendAliveAsync_sends_the_reboot_required_value_when_given_one()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync(rebootRequired: true);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.GetProperty("rebootRequired").GetBoolean());
    }

    [Fact]
    public async Task SendAliveAsync_omits_reboot_required_as_null_by_default()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync();

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("rebootRequired").ValueKind);
    }

    [Fact]
    public async Task SendAliveAsync_sends_this_agents_own_current_settings()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await client.SendAliveAsync(
            actualLogLevel: "DEBUG", actualUpdateCheckIntervalMinutes: 120, actualUpdateCheckJitterSeconds: 45,
            actualAliveIntervalMinutes: 10);

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("DEBUG", doc.RootElement.GetProperty("actualLogLevel").GetString());
        Assert.Equal(120, doc.RootElement.GetProperty("actualUpdateCheckIntervalMinutes").GetInt32());
        Assert.Equal(45, doc.RootElement.GetProperty("actualUpdateCheckJitterSeconds").GetInt32());
        Assert.Equal(10, doc.RootElement.GetProperty("actualAliveIntervalMinutes").GetInt32());
    }

    [Fact]
    public async Task SendAliveAsync_parses_a_desired_setting_override_when_the_server_reports_one()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                installRequested = false,
                desiredLogLevel = "DEBUG",
                desiredUpdateCheckIntervalMinutes = 15,
                desiredUpdateCheckJitterSeconds = 5,
                desiredAliveIntervalMinutes = 10,
            }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Equal("DEBUG", result.DesiredLogLevel);
        Assert.Equal(15, result.DesiredUpdateCheckIntervalMinutes);
        Assert.Equal(5, result.DesiredUpdateCheckJitterSeconds);
        Assert.Equal(10, result.DesiredAliveIntervalMinutes);
    }

    [Fact]
    public async Task SendAliveAsync_defaults_desired_settings_to_null_when_the_server_omits_them()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Null(result.DesiredLogLevel);
        Assert.Null(result.DesiredUpdateCheckIntervalMinutes);
        Assert.Null(result.DesiredUpdateCheckJitterSeconds);
        Assert.Null(result.DesiredAliveIntervalMinutes);
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
                    windowsInstallerX64 = new { downloadUrl = "/api/agent/updates/setup.exe", sha256 = "abc", sizeBytes = 123 },
                    windowsInstallerArm64 = (object?)null,
                    linuxDebX64 = (object?)null,
                    linuxDebArm64 = (object?)null,
                    linuxRpmX64 = (object?)null,
                    linuxRpmArm64 = (object?)null,
                },
            }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.NotNull(result.AgentUpdateAvailable);
        Assert.Equal("99.0.0", result.AgentUpdateAvailable!.Version);
        Assert.NotNull(result.AgentUpdateAvailable.WindowsInstallerX64);
        Assert.Equal("/api/agent/updates/setup.exe", result.AgentUpdateAvailable.WindowsInstallerX64!.DownloadUrl);
        Assert.Null(result.AgentUpdateAvailable.LinuxDebX64);
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
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Null(result.AgentUpdateAvailable);
    }

    [Fact]
    public async Task SendAliveAsync_parses_certificate_rotation_pending_when_the_server_reports_it()
    {
        // updatewatch2-server#6 follow-up: additive field, same shape as
        // installRequested/agentUpdateAvailable.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false, certificateRotationPending = true }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.True(result.CertificateRotationPending);
    }

    [Fact]
    public async Task SendAliveAsync_defaults_certificate_rotation_pending_to_false_when_the_server_omits_it()
    {
        // Backward compat with a pre-0.8.0 server that doesn't send this
        // field at all.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.False(result.CertificateRotationPending);
    }

    [Fact]
    public async Task SendAliveAsync_parses_preDownloadWindowsUpdatesEnabled_when_the_server_reports_it()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false, preDownloadWindowsUpdatesEnabled = true }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.True(result.PreDownloadWindowsUpdatesEnabled);
    }

    [Fact]
    public async Task SendAliveAsync_defaults_preDownloadWindowsUpdatesEnabled_to_false_when_the_server_omits_it()
    {
        // Backward compat with a pre-1.1.0-protocol server that doesn't
        // send this field at all.
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.False(result.PreDownloadWindowsUpdatesEnabled);
    }

    [Fact]
    public async Task SendAliveAsync_parses_preDownloadLinuxUpdatesEnabled_when_the_server_reports_it()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false, preDownloadLinuxUpdatesEnabled = true }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.True(result.PreDownloadLinuxUpdatesEnabled);
    }

    [Fact]
    public async Task SendAliveAsync_defaults_preDownloadLinuxUpdatesEnabled_to_false_when_the_server_omits_it()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { installRequested = false }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.False(result.PreDownloadLinuxUpdatesEnabled);
    }

    [Fact]
    public async Task DownloadFileAsync_writes_the_response_body_to_the_destination_path()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("fake-binary-content"),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);
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

    [Fact]
    public async Task SendAliveAsync_retries_a_transient_transport_failure_and_then_succeeds()
    {
        // Reported directly by the user: some Alive/Updates calls hit a
        // ~15s transport-level timeout in the field — this covers the fix,
        // retrying a connection-level failure (no response ever received,
        // HttpRequestException.StatusCode is null) rather than failing the
        // whole heartbeat and waiting out the full interval.
        var attempts = 0;
        var handler = new CapturingHttpMessageHandler(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException("Connection reset by peer", null, statusCode: null);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { installRequested = false }) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Equal(AliveOutcome.Success, result.Outcome);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task SendAliveAsync_gives_up_after_three_attempts_on_a_persistent_transport_failure()
    {
        var attempts = 0;
        var handler = new CapturingHttpMessageHandler(_ =>
        {
            attempts++;
            throw new HttpRequestException("Connection reset by peer", null, statusCode: null);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAliveAsync());

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task SendAliveAsync_does_not_retry_a_real_http_error_response()
    {
        // A response that was actually received (StatusCode is non-null on
        // the resulting HttpRequestException, e.g. via EnsureSuccessStatusCode
        // elsewhere) is a real server-side outcome, not a transport blip —
        // retrying it blindly would fight the documented transient-409
        // registration-handoff behavior instead of just returning it as-is.
        // SendAliveAsync itself never throws on a non-2xx status (it maps
        // 401/403 to CertificateRejected and everything else to
        // OtherFailure), so this is the most direct way to prove the HTTP
        // call itself is only attempted once for a real response.
        var attempts = 0;
        var handler = new CapturingHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1") };
        var client = new ServerClient(httpClient, new AgentOptions(), NullLogger<ServerClient>.Instance);

        var result = await client.SendAliveAsync();

        Assert.Equal(AliveOutcome.OtherFailure, result.Outcome);
        Assert.Equal(1, attempts);
    }

    private class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
