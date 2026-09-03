using Microsoft.Extensions.Logging.EventLog;
using UpdateWatch2.Agent;
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

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IUpdateChecker, WindowsUpdateChecker>();
    builder.Logging.AddEventLog(new EventLogSettings { SourceName = "UpdateWatch2 Agent" });
}
else
{
    // TODO: register a real Linux checker once one exists (CLAUDE.md section 4.1).
    builder.Services.AddSingleton<IUpdateChecker, NoOpUpdateChecker>();
}

builder.Services.AddHttpClient<IServerClient, ServerClient>((sp, client) =>
{
    var options = sp.GetRequiredService<AgentOptions>();
    if (!string.IsNullOrWhiteSpace(options.ServerAddress))
    {
        client.BaseAddress = new Uri($"https://{options.ServerAddress}:{options.ServerPort}/");
    }
});

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
