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

    public async Task SendAliveAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync(AgentApiRoutes.Alive(Environment.MachineName), content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Alive heartbeat failed with status {StatusCode}", response.StatusCode);
        }
    }

    public async Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(AgentApiRoutes.ReportUpdates(Environment.MachineName), report, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }
}
