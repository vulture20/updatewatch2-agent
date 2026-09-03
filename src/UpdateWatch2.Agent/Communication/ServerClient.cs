using System.Net.Http.Json;
using System.Runtime.InteropServices;
using UpdateWatch2.Agent.Protocol;

namespace UpdateWatch2.Agent.Communication;

public class ServerClient(HttpClient httpClient, ILogger<ServerClient> logger) : IServerClient
{
    public async Task<RegisterResult> RegisterAsync(CancellationToken ct = default)
    {
        var request = new RegisterRequest(
            Hostname: Environment.MachineName,
            DnsName: System.Net.Dns.GetHostEntry(Environment.MachineName).HostName,
            OperatingSystem: RuntimeInformation.OSDescription,
            IpAddress: null, // TODO: resolve a real outbound-facing IP
            AgentVersion: AgentVersion.Current,
            ProtocolVersion: ProtocolVersion.Current);

        var response = await httpClient.PostAsJsonAsync(AgentApiRoutes.Register, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RegisterResult>(ct);
        return result ?? new RegisterResult(Approved: false);
    }

    public async Task SendAliveAsync(CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync($"{AgentApiRoutes.Alive}?hostname={Uri.EscapeDataString(Environment.MachineName)}", content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Alive heartbeat failed with status {StatusCode}", response.StatusCode);
        }
    }

    public async Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"{AgentApiRoutes.ReportUpdates}?hostname={Uri.EscapeDataString(Environment.MachineName)}", report, ct);
        response.EnsureSuccessStatusCode();
    }
}
