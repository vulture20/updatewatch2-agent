using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
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

    public async Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default) =>
        await httpClient.GetByteArrayAsync(AgentApiRoutes.CaCertificate, ct);

    public async Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default)
    {
        var request = new RegisterRequest(
            DnsName: System.Net.Dns.GetHostEntry(Environment.MachineName).HostName,
            OperatingSystem: RuntimeInformation.OSDescription,
            IpAddress: null, // TODO: resolve a real outbound-facing IP
            AgentVersion: AgentVersion.Current,
            ProtocolVersion: ProtocolVersion.Current,
            RegistrationToken: registrationToken);

        var response = await httpClient.PostAsJsonAsync(AgentApiRoutes.Register(Environment.MachineName), request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RegisterResult>(JsonOptions, ct);
        return result ?? new RegisterResult(Approved: false, RegistrationToken: null, Certificate: null, ProtocolVersion: null);
    }

    public async Task<AliveOutcome> SendAliveAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync(AgentApiRoutes.Alive(Environment.MachineName), content: null, ct);
        if (response.IsSuccessStatusCode)
        {
            return AliveOutcome.Success;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                "Alive heartbeat rejected with status {StatusCode} — this agent's certificate may no longer be trusted by the server.",
                response.StatusCode);
            return AliveOutcome.CertificateRejected;
        }

        logger.LogWarning("Alive heartbeat failed with status {StatusCode}", response.StatusCode);
        return AliveOutcome.OtherFailure;
    }

    public async Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(AgentApiRoutes.ReportUpdates(Environment.MachineName), report, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(AgentApiRoutes.Version, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VersionResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Server returned an empty /api/version response.");
    }

    public async Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync(AgentApiRoutes.Renew(Environment.MachineName), content: null, ct);
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
}
