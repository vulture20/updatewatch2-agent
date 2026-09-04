using System.Net.Http;
using System.Net.Security;
using Microsoft.Extensions.Logging.EventLog;
using UpdateWatch2.Agent;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Certificates.Linux;
using UpdateWatch2.Agent.Certificates.Windows;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.Configuration.Linux;
using UpdateWatch2.Agent.Configuration.Windows;
using UpdateWatch2.Agent.UpdateCheck;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "UpdateWatch2 Agent");
builder.Services.AddSystemd();

builder.Services.AddSingleton<IAgentConfigStore>(_ =>
{
    if (OperatingSystem.IsWindows())
    {
        return new WindowsRegistryConfigStore();
    }

    if (OperatingSystem.IsLinux())
    {
        return new LinuxFileConfigStore();
    }

    throw new PlatformNotSupportedException("UpdateWatch2 Agent only supports Windows and Linux.");
});

// Loaded once at startup. A server-pushed log-level change (CLAUDE.md
// section 6.2) would need this to become reloadable — not implemented yet.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IAgentConfigStore>().Load());

var logLevelFromConfig = builder.Configuration["UpdateWatch2:LogLevel"]; // overridable for local runs/tests
if (Enum.TryParse<LogLevel>(MapLogLevel(logLevelFromConfig ?? "INFO"), out var minLevel))
{
    builder.Logging.SetMinimumLevel(minLevel);
}

// Where this agent's own client certificate lives, once issued — genuinely
// platform-specific storage (machine cert store vs. a file), see
// Certificates/{Windows,Linux}/*ClientCertificateStore.
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IUpdateChecker, WindowsUpdateChecker>();
    builder.Services.AddSingleton<IClientCertificateStore, WindowsClientCertificateStore>();
    builder.Logging.AddEventLog(new EventLogSettings { SourceName = "UpdateWatch2 Agent" });
}
else if (OperatingSystem.IsLinux())
{
    // TODO: register a real Linux checker once one exists (CLAUDE.md section 4.1).
    builder.Services.AddSingleton<IUpdateChecker, NoOpUpdateChecker>();
    builder.Services.AddSingleton<IClientCertificateStore, LinuxClientCertificateStore>();
}
else
{
    throw new PlatformNotSupportedException("UpdateWatch2 Agent only supports Windows and Linux.");
}

// Where the pinned server CA certificate lives — deliberately a plain
// file, not the OS trust store (see FileCaTrustStore), at a fixed
// platform-appropriate path distinct from AgentOptions' scalar config
// (a cert blob doesn't fit the registry/JSON-config model those use).
var caTrustStorePath = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UpdateWatch2", "ca.pem")
    : "/etc/updatewatch2/ca.pem";
builder.Services.AddSingleton(new FileCaTrustStore(caTrustStorePath));

builder.Services.AddSingleton<IAgentCertificateState, AgentCertificateState>();
builder.Services.AddSingleton<PinnedServerCertificateValidator>();

// One shared handler for both the registration flow and the typed
// IServerClient below (via ConfigurePrimaryHttpMessageHandler) — so a
// client certificate RegistrationWorker attaches partway through the
// process's lifetime is visible to every subsequent connection, agent-wide.
// ClientCertificates starts as an empty (non-null) collection so
// RegistrationWorker can always just .Add() to it without a null check.
// The RemoteCertificateValidationCallback entirely replaces .NET's default
// TLS validation — see PinnedServerCertificateValidator for why it
// re-implements hostname (SAN) checking itself instead of just chain
// validation.
builder.Services.AddSingleton(sp =>
{
    var validator = sp.GetRequiredService<PinnedServerCertificateValidator>();
    return new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = [],
            RemoteCertificateValidationCallback = (_, certificate, _, _) => validator.Validate(certificate),
        },
    };
});

builder.Services.AddHttpClient<IServerClient, ServerClient>((sp, client) =>
{
    var options = sp.GetRequiredService<AgentOptions>();
    if (!string.IsNullOrWhiteSpace(options.ServerAddress))
    {
        client.BaseAddress = new Uri($"https://{options.ServerAddress}:{options.ServerPort}/");
    }
})
.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<SocketsHttpHandler>());

builder.Services.AddHostedService<RegistrationWorker>();
builder.Services.AddHostedService<UpdateCheckWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();

var host = builder.Build();
host.Run();

static string MapLogLevel(string value) => value.Trim().ToUpperInvariant() switch
{
    "DEBUG" => nameof(LogLevel.Debug),
    "INFO" => nameof(LogLevel.Information),
    "WARNING" => nameof(LogLevel.Warning),
    "ERROR" => nameof(LogLevel.Error),
    _ => value,
};
