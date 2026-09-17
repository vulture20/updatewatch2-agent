using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using UpdateWatch2.Agent.Protocol;

namespace UpdateWatch2.Agent.Communication;

public class ServerClient(HttpClient httpClient, ILogger<ServerClient> logger) : IServerClient
{
    // The server's controllers use ASP.NET Core's default camelCase JSON
    // output (e.g. "registrationToken", "certificate") — Web defaults
    // match that against these PascalCase C# record properties
    // case-insensitively, without needing [JsonPropertyName] on each one.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Reported directly by the user: some Alive/ReportUpdates calls run
    // into a ~15s transport-level timeout in the field (plausibly the same
    // underlying network condition behind updatewatch2-agent#20's TLS
    // aborts). Without a retry, a single blip fails the whole call, and
    // the calling worker (HeartbeatWorker/UpdateCheckWorker) just waits
    // out its full interval — minutes to hours — before trying again.
    // Retried here, close to the actual I/O, rather than in each worker's
    // own loop, so every outbound call gets the same treatment without
    // duplicating retry logic in each one. Deliberately scoped to
    // TRANSPORT-level failures only — a connection that was never
    // established, reset mid-request, or that timed out outright — never
    // to an HTTP error status code: EnsureSuccessStatusCode()'s
    // HttpRequestException always carries a non-null StatusCode once a
    // response was actually received, and a real error response (e.g. the
    // documented transient 409 during a registration token handoff, or a
    // genuine 401/403 certificate rejection) needs its caller's own
    // handling, not a blind retry here.
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private async Task<T> WithRetryAsync<T>(string operationName, Func<Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransientTransportFailure(ex) && !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "{Operation} failed on attempt {Attempt}/{MaxAttempts} due to a transient network error — retrying.",
                    operationName, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private Task WithRetryAsync(string operationName, Func<Task> action, CancellationToken ct) =>
        WithRetryAsync(operationName, async () => { await action(); return true; }, ct);

    private static bool IsTransientTransportFailure(Exception ex) => ex switch
    {
        // Only a response-less failure — a real HTTP error response
        // (EnsureSuccessStatusCode, or the higher-level Get*Async helpers
        // that call it internally) always sets StatusCode.
        HttpRequestException { StatusCode: null } => true,
        IOException => true,
        SocketException => true,
        // TaskCanceledException also covers HttpClient's own internal
        // request timeout firing — genuine caller cancellation is already
        // excluded by the !ct.IsCancellationRequested guard above.
        TaskCanceledException => true,
        _ => false,
    };

    public async Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default)
    {
        logger.LogDebug("HTTP GET {Route}", AgentApiRoutes.CaCertificate);
        var bytes = await WithRetryAsync(nameof(FetchCaCertificateAsync), () => httpClient.GetByteArrayAsync(AgentApiRoutes.CaCertificate, ct), ct);
        logger.LogDebug("HTTP GET {Route} -> {ByteCount} byte(s)", AgentApiRoutes.CaCertificate, bytes.Length);
        return bytes;
    }

    public async Task<byte[]> FetchCaCertificateBundleAsync(CancellationToken ct = default)
    {
        logger.LogDebug("HTTP GET {Route}", AgentApiRoutes.CaCertificateBundle);
        var bytes = await WithRetryAsync(nameof(FetchCaCertificateBundleAsync), () => httpClient.GetByteArrayAsync(AgentApiRoutes.CaCertificateBundle, ct), ct);
        logger.LogDebug("HTTP GET {Route} -> {ByteCount} byte(s)", AgentApiRoutes.CaCertificateBundle, bytes.Length);
        return bytes;
    }

    public async Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default)
    {
        var request = new RegisterRequest(
            DnsName: ResolveDnsName(),
            OperatingSystem: OperatingSystemDescriber.Describe(),
            IpAddress: ResolveOutboundIpAddress(),
            AgentVersion: AgentVersion.Current,
            ProtocolVersion: ProtocolVersion.Current,
            RegistrationToken: registrationToken);

        var route = AgentApiRoutes.Register(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route} (hasRegistrationToken={HasToken})", route, registrationToken is not null);
        var response = await WithRetryAsync(nameof(RegisterAsync), () => httpClient.PostAsJsonAsync(route, request, JsonOptions, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RegisterResult>(JsonOptions, ct);
        return result ?? new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: null);
    }

    public async Task<AliveResult> SendAliveAsync(
        bool? rebootRequired = null, string? actualLogLevel = null, int? actualUpdateCheckIntervalMinutes = null,
        int? actualUpdateCheckJitterSeconds = null, CancellationToken ct = default)
    {
        // Re-resolved fresh on every heartbeat, not just at registration —
        // this is the only channel that can ever update these fields after
        // an agent is certified, since the server's RegisterAsync never
        // runs again for it (updatewatch2-agent#6).
        var request = new AliveRequest(
            DnsName: ResolveDnsName(),
            OperatingSystem: OperatingSystemDescriber.Describe(),
            IpAddress: ResolveOutboundIpAddress(),
            AgentVersion: AgentVersion.Current,
            BootTimeUtc: ResolveBootTimeUtc(),
            RebootRequired: rebootRequired,
            ActualLogLevel: actualLogLevel,
            ActualUpdateCheckIntervalMinutes: actualUpdateCheckIntervalMinutes,
            ActualUpdateCheckJitterSeconds: actualUpdateCheckJitterSeconds);

        var route = AgentApiRoutes.Alive(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route}", route);
        var response = await WithRetryAsync(nameof(SendAliveAsync), () => httpClient.PostAsJsonAsync(route, request, JsonOptions, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<AliveResponseBody>(JsonOptions, ct);
            return new AliveResult(
                AliveOutcome.Success, body?.InstallRequested ?? false, body?.InstallUpdateIds, body?.AgentUpdateAvailable,
                body?.CertificateRotationPending ?? false, body?.RebootRequested ?? false,
                body?.PreDownloadWindowsUpdatesEnabled ?? false, body?.DesiredLogLevel, body?.DesiredUpdateCheckIntervalMinutes,
                body?.DesiredUpdateCheckJitterSeconds);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "Alive heartbeat rejected with status {StatusCode} — this agent's certificate may no longer be trusted by the server.",
                response.StatusCode);
            return AliveResult.From(AliveOutcome.CertificateRejected);
        }

        logger.LogWarning("Alive heartbeat failed with status {StatusCode}", response.StatusCode);
        return AliveResult.From(AliveOutcome.OtherFailure);
    }

    private record AliveResponseBody(
        bool InstallRequested, IReadOnlyList<string>? InstallUpdateIds, AgentUpdateOffer? AgentUpdateAvailable,
        bool CertificateRotationPending, bool RebootRequested, bool PreDownloadWindowsUpdatesEnabled,
        string? DesiredLogLevel, int? DesiredUpdateCheckIntervalMinutes, int? DesiredUpdateCheckJitterSeconds);

    /// <summary>
    /// Computes when this machine last booted from <see cref="Environment.TickCount64"/>
    /// — milliseconds since system startup, a .NET API implemented
    /// portably on both Windows and Linux, so no platform-specific code is
    /// needed here at all (unlike every other self-reported-metadata
    /// helper in this class, this one needs no OS-specific branch).
    /// </summary>
    private static DateTimeOffset ResolveBootTimeUtc() => DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);

    public async Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var route = AgentApiRoutes.ReportUpdates(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route} ({Count} update(s))", route, report.Updates.Count);
        var response = await WithRetryAsync(nameof(ReportUpdatesAsync), () => httpClient.PostAsJsonAsync(route, report, JsonOptions, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    public async Task AcknowledgeInstallAsync(InstallOutcome outcome, string? errorDetail, CancellationToken ct = default)
    {
        var route = AgentApiRoutes.InstallAck(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route} (outcome={Outcome})", route, outcome);
        var response = await WithRetryAsync(nameof(AcknowledgeInstallAsync), () => httpClient.PostAsJsonAsync(route, new InstallAckRequest(outcome, errorDetail), JsonOptions, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    public async Task AcknowledgeRebootAsync(RebootOutcome outcome, string? errorDetail, CancellationToken ct = default)
    {
        var route = AgentApiRoutes.RebootAck(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route} (outcome={Outcome})", route, outcome);
        var response = await WithRetryAsync(nameof(AcknowledgeRebootAsync), () => httpClient.PostAsJsonAsync(route, new RebootAckRequest(outcome, errorDetail), JsonOptions, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    public async Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default)
    {
        logger.LogDebug("HTTP GET {Route}", AgentApiRoutes.Version);
        var response = await WithRetryAsync(nameof(FetchVersionAsync), () => httpClient.GetAsync(AgentApiRoutes.Version, ct), ct);
        logger.LogDebug("HTTP GET {Route} -> {StatusCode}", AgentApiRoutes.Version, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VersionResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Server returned an empty /api/version response.");
    }

    public async Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default)
    {
        var route = AgentApiRoutes.Renew(Environment.MachineName);
        logger.LogDebug("HTTP POST {Route}", route);
        var response = await WithRetryAsync(nameof(RenewCertificateAsync), () => httpClient.PostAsync(route, content: null, ct), ct);
        logger.LogDebug("HTTP POST {Route} -> {StatusCode}", route, (int)response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            return new RenewCertificateResult(false, null);
        }

        // The server's success body is just { certificate }, with no
        // "success" field of its own — the status code already carries
        // that. Deserializing straight into RenewCertificateResult would
        // leave its Success property at its JSON-absent default (false)
        // even on a 200, so parse the body shape separately instead.
        var body = await response.Content.ReadFromJsonAsync<RenewCertificateBody>(JsonOptions, ct);
        return new RenewCertificateResult(true, body?.Certificate);
    }

    private record RenewCertificateBody(string? Certificate);

    public async Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken ct = default)
    {
        logger.LogDebug("HTTP GET {Route} -> {DestinationPath}", downloadUrl, destinationPath);
        // The whole request-and-copy, not just the initial GetAsync call, is
        // wrapped here — a transient failure can just as easily happen mid-
        // stream (CopyToAsync) as at connect time, and File.Create truncates
        // on every attempt, so a retry always starts the destination file
        // over cleanly rather than appending to a partial one.
        await WithRetryAsync(nameof(DownloadFileAsync), async () =>
        {
            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            logger.LogDebug("HTTP GET {Route} -> {StatusCode}", downloadUrl, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, ct);
        }, ct);
    }

    /// <summary>Shared by RegisterAsync and SendAliveAsync so both report the same DNS name resolution.</summary>
    private static string ResolveDnsName() => System.Net.Dns.GetHostEntry(Environment.MachineName).HostName;

    /// <summary>
    /// This machine's outbound-facing IP address toward the configured
    /// server — purely informational metadata shown in the admin overview,
    /// not used for anything security-relevant (the certificate SAN/pinned
    /// CA are what actually establish identity). Deliberately resolved
    /// against the server's own address/port rather than an arbitrary
    /// public address: on a multi-homed machine (a Docker bridge, a VPN
    /// interface, a real LAN NIC, ...) picking "some" local IP without
    /// context can easily surface the wrong one — the interface actually
    /// used to reach the management server is the one an admin looking at
    /// this field cares about. Connecting a UDP socket triggers the OS's
    /// routing-table lookup for that destination without ever sending a
    /// packet, so this works even before the server is reachable/up.
    /// </summary>
    private string? ResolveOutboundIpAddress()
    {
        if (httpClient.BaseAddress is not { } baseAddress)
        {
            // No ServerAddress configured yet — nothing to resolve a route
            // toward. Registration will retry later once one is set; this
            // call just runs again on the next attempt.
            return null;
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(baseAddress.Host, baseAddress.Port);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Could not resolve this agent's outbound IP address toward the server — reporting none.");
            return null;
        }
    }
}
