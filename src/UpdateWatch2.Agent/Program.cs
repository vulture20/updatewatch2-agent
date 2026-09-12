using System.Diagnostics;
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
using UpdateWatch2.Agent.Reboot;
using UpdateWatch2.Agent.Reboot.Linux;
using UpdateWatch2.Agent.Reboot.Windows;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.SelfUpdate.Linux;
using UpdateWatch2.Agent.SelfUpdate.Windows;
using UpdateWatch2.Agent.UpdateCheck;
using UpdateWatch2.Agent.UpdateCheck.Linux;
using UpdateWatch2.Agent.UpdateCheck.Windows;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "UpdateWatch2 Agent");
builder.Services.AddSystemd();

// Constructed directly (not just registered lazily via DI) because its
// Load() result is needed right here, before builder.Build(), to resolve
// the actual configured log level below.
IAgentConfigStore configStore = OperatingSystem.IsWindows()
    ? new WindowsRegistryConfigStore()
    : OperatingSystem.IsLinux()
        ? new LinuxFileConfigStore()
        : throw new PlatformNotSupportedException("UpdateWatch2 Agent only supports Windows and Linux.");
builder.Services.AddSingleton(configStore);

// Loaded once at startup. A server-pushed log-level change (CLAUDE.md
// section 6.2) would need this to become reloadable — not implemented yet.
var agentOptions = configStore.Load();
builder.Services.AddSingleton(agentOptions);

// This used to read builder.Configuration["UpdateWatch2:LogLevel"] — a
// configuration key nothing in this codebase ever sets, so it always fell
// through to the "INFO" fallback below regardless of what agent.conf/the
// registry's own LogLevel value actually said. Fixed to read the value
// this agent is actually configured with. Confirmed by hand while
// diagnosing a real incident (a silently-dead RegistrationWorker producing
// zero log output): had LogLevel been genuinely wired up already, bumping
// it to DEBUG would at least have shown *something* happening, instead of
// looking identical to "nothing is running".
//
// Also — matching the server's own documented finding (CLAUDE.md) that
// builder.Logging.SetMinimumLevel(...) alone does not reliably take effect
// on a Generic-Host-style app, because Logging:LogLevel:* read reactively
// from IConfiguration wins over it — writing the value directly into
// configuration is what the console/EventLog providers' filter actually
// respects.
var mappedLogLevel = MapLogLevel(agentOptions.LogLevel);
builder.Configuration["Logging:LogLevel:Default"] = mappedLogLevel;
if (Enum.TryParse<LogLevel>(mappedLogLevel, out var minLevel))
{
    builder.Logging.SetMinimumLevel(minLevel);
}

// Where this agent's own client certificate lives, once issued — genuinely
// platform-specific storage (machine cert store vs. a file), see
// Certificates/{Windows,Linux}/*ClientCertificateStore.
// Where a self-update download is staged before IPlatformUpdateApplier
// applies it (updatewatch2-agent#14) — same "fixed platform-appropriate
// path outside AgentOptions' scalar config" pattern as caTrustStorePath
// below.
var selfUpdateStagingDirectory = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UpdateWatch2", "agent-update")
    : "/var/lib/updatewatch2/agent-update";

// Platform-agnostic (unlike IPlatformUpdateApplier) — plain file-age
// bookkeeping on the same staging directory above, registered
// unconditionally so it also runs (as a no-op) on a Linux host with no
// known package manager, where nothing is ever downloaded there in the
// first place. See SelfUpdateStagingCleaner's own doc comment.
builder.Services.AddSingleton(sp => new SelfUpdateStagingCleaner(
    selfUpdateStagingDirectory,
    sp.GetRequiredService<ILogger<SelfUpdateStagingCleaner>>()));

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IWindowsUpdateSession, WuaUpdateSession>();
    builder.Services.AddSingleton<IUpdateChecker, WindowsUpdateChecker>();
    builder.Services.AddSingleton<IClientCertificateStore, WindowsClientCertificateStore>();
    builder.Services.AddSingleton<IPlatformUpdateApplier, WindowsInstallerApplier>();
    builder.Services.AddSingleton<IAgentRebooter, WindowsAgentRebooter>();
    builder.Services.AddSingleton<IAgentSelfUpdater>(sp => new AgentSelfUpdateService(
        AgentUpdateAssetKind.WindowsInstaller,
        selfUpdateStagingDirectory,
        sp.GetRequiredService<IServerClient>(),
        sp.GetRequiredService<IPlatformUpdateApplier>(),
        sp.GetRequiredService<ILogger<AgentSelfUpdateService>>()));
    builder.Logging.AddEventLog(new EventLogSettings { SourceName = "UpdateWatch2 Agent" });
}
else if (OperatingSystem.IsLinux())
{
    switch (LinuxPackageManagerDetector.Detect())
    {
        case LinuxPackageManagerKind.Apt:
            builder.Services.AddSingleton<ILinuxUpdateSession, AptUpdateSession>();
            builder.Services.AddSingleton<IUpdateChecker, LinuxUpdateChecker>();
            // CA1416 can't see through this lambda to the OperatingSystem.IsLinux()
            // guard around the whole else-if branch it's registered in — it only
            // ever actually runs when DI resolves IPlatformUpdateApplier, which
            // only happens on Linux, since that's the only branch that registers it.
#pragma warning disable CA1416
            builder.Services.AddSingleton<IPlatformUpdateApplier>(sp => new LinuxPackageApplier(
                AgentUpdateAssetKind.LinuxDeb, sp.GetRequiredService<ILogger<LinuxPackageApplier>>()));
#pragma warning restore CA1416
            builder.Services.AddSingleton<IAgentSelfUpdater>(sp => new AgentSelfUpdateService(
                AgentUpdateAssetKind.LinuxDeb,
                selfUpdateStagingDirectory,
                sp.GetRequiredService<IServerClient>(),
                sp.GetRequiredService<IPlatformUpdateApplier>(),
                sp.GetRequiredService<ILogger<AgentSelfUpdateService>>()));
            break;
        case LinuxPackageManagerKind.Dnf:
            builder.Services.AddSingleton<ILinuxUpdateSession, DnfUpdateSession>();
            builder.Services.AddSingleton<IUpdateChecker, LinuxUpdateChecker>();
            // Same CA1416/lambda situation as the Apt case above.
#pragma warning disable CA1416
            builder.Services.AddSingleton<IPlatformUpdateApplier>(sp => new LinuxPackageApplier(
                AgentUpdateAssetKind.LinuxRpm, sp.GetRequiredService<ILogger<LinuxPackageApplier>>()));
#pragma warning restore CA1416
            builder.Services.AddSingleton<IAgentSelfUpdater>(sp => new AgentSelfUpdateService(
                AgentUpdateAssetKind.LinuxRpm,
                selfUpdateStagingDirectory,
                sp.GetRequiredService<IServerClient>(),
                sp.GetRequiredService<IPlatformUpdateApplier>(),
                sp.GetRequiredService<ILogger<AgentSelfUpdateService>>()));
            break;
        default:
            // No known package manager binary found (e.g. a minimal or
            // unsupported distro) — same fallback this platform used
            // unconditionally before updatewatch2-agent#8. No known
            // package format also means no safe way to self-update
            // (updatewatch2-agent#14).
            builder.Services.AddSingleton<IUpdateChecker, NoOpUpdateChecker>();
            builder.Services.AddSingleton<IAgentSelfUpdater, NoOpAgentSelfUpdater>();
            break;
    }

    builder.Services.AddSingleton<IClientCertificateStore, LinuxClientCertificateStore>();
    // Not gated on which package manager (if any) was detected above — a
    // plain shutdown -r doesn't depend on apt vs. dnf vs. neither, unlike
    // self-update/OS-update-install, which need the right tool.
    builder.Services.AddSingleton<IAgentRebooter, LinuxAgentRebooter>();
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
builder.Services.AddSingleton<IRegistrationWakeSignal, RegistrationWakeSignal>();
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
        // The server re-validates the presented client certificate on
        // every request, not just once per TLS handshake (no certificate
        // cache configured server-side), and SslOptions is only consulted
        // when a *new* TLS connection is negotiated — so an already-open
        // pooled connection keeps presenting whatever certificate (or lack
        // of one) was live when it was negotiated, regardless of any later
        // change to SslOptions.ClientCertificates. Zero disables pooling
        // for this handler entirely rather than just bounding it: a
        // non-zero-but-short lifetime (originally 2 minutes here) still
        // leaves a real race for updatewatch2-server#7's renewal hot-swap
        // and updatewatch2-server#11's self-heal-then-recover sequence,
        // both of which can change ClientCertificates twice within a much
        // shorter window than any nonzero lifetime — confirmed live: a
        // freshly recovered certificate got immediately re-rejected and
        // deleted again by a stale pooled connection from the moment just
        // before it was attached, permanently stranding the agent (the
        // one-shot registration token that recovery consumed is gone by
        // then, so there was no way back in without another admin
        // re-issuance). This agent's request volume is low (a heartbeat
        // every few minutes at most) — the extra TLS handshake per request
        // this costs is immaterial next to that cadence.
        PooledConnectionLifetime = TimeSpan.Zero,
    };
});

// A factory for a throwaway IServerClient on its own handler/connection
// pool — deliberately never the shared SocketsHttpHandler above. See
// RegistrationWorker's class-level remarks: bootstrap traffic (fetching
// the CA cert, every registration poll, all of it pre-certificate) must
// never share a connection pool with the shared handler, or pre-existing
// pooled connections from that traffic get reused for later, post-
// certificate calls — which then silently never present the certificate.
builder.Services.AddSingleton<Func<IServerClient>>(sp => () =>
{
    var validator = sp.GetRequiredService<PinnedServerCertificateValidator>();
    var opts = sp.GetRequiredService<AgentOptions>();
    var bootstrapHandler = new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, _) => validator.Validate(certificate),
        },
    };
    var bootstrapHttpClient = new HttpClient(bootstrapHandler);
    if (!string.IsNullOrWhiteSpace(opts.ServerAddress))
    {
        bootstrapHttpClient.BaseAddress = new Uri($"https://{opts.ServerAddress}:{opts.ServerPort}/");
    }

    return new ServerClient(bootstrapHttpClient, sp.GetRequiredService<ILogger<ServerClient>>());
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

// Registered as a singleton first, then exposed both as the hosted
// service and as IUpdateCheckTrigger via the same instance — HeartbeatWorker
// needs to call into it directly (an immediate check/report right after a
// successful remote-triggered install), and a plain AddHostedService<T>()
// here would give the hosted service its own separate instance instead.
builder.Services.AddSingleton<UpdateCheckWorker>();
builder.Services.AddSingleton<IUpdateCheckTrigger>(sp => sp.GetRequiredService<UpdateCheckWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateCheckWorker>());

builder.Services.AddHostedService<HeartbeatWorker>();

// The Generic Host default (5s) is too short for this agent's own normal
// shutdown path in practice: an in-flight HTTP call (registration/renewal
// retry, alive heartbeat) can legitimately still be running, and — worse —
// a real Windows Update search/download/install via WuaUpdateSession's
// synchronous COM calls has no way to be aborted mid-call once started
// (WindowsUpdateChecker's Task.Run(..., ct) only cancels the work item
// before it starts running, never a COM call already in progress). 30s
// gives ordinary in-flight work a real chance to finish cleanly without
// meaningfully delaying a normal stop/restart — but it still cannot
// unconditionally bound a real Windows Update install in progress, which
// is exactly why the try/catch around host.Run() below exists too.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

var host = builder.Build();

try
{
    host.Run();
}
catch (OperationCanceledException)
{
    // A real production crash this specific catch exists to prevent
    // (agent v0.14.2, confirmed via a real Windows Event Viewer APPCRASH
    // report): when stopping all IHostedServices takes longer than
    // HostOptions.ShutdownTimeout (raised above, but — per that comment —
    // still not something every case can be bounded by), Generic Host's
    // own WindowsServiceLifetime.StopAsync throws OperationCanceledException
    // on the now-cancelled token instead of just giving up quietly — and
    // nothing in Microsoft.Extensions.Hosting catches that for you. Left
    // unhandled, it propagates all the way out of host.Run() and crashes
    // the entire process (the reported stack trace was exactly
    // WindowsServiceLifetime.StopAsync -> Host.StopAsync ->
    // WaitForShutdownAsync -> RunAsync -> Run -> Main). The service was
    // already stopping when this happens — that is the whole reason this
    // exception exists in the first place — so there is nothing to
    // recover here, just exit instead of crashing. host.Services is
    // already disposed by this point (RunAsync's own finally block runs
    // before this exception reaches here), so this can't go through the
    // app's normal DI-backed logging pipeline — write directly to the
    // Windows Event Log instead, matching the SourceName the EventLog
    // logging provider above is already registered under, so it still
    // shows up grouped with this agent's other log entries in Event
    // Viewer.
    if (OperatingSystem.IsWindows())
    {
        EventLog.WriteEntry(
            "UpdateWatch2 Agent",
            "Shutdown did not complete within the configured timeout; exiting without a clean stop instead of crashing.",
            EventLogEntryType.Warning);
    }
}

static string MapLogLevel(string value) => value.Trim().ToUpperInvariant() switch
{
    "DEBUG" => nameof(LogLevel.Debug),
    "INFO" => nameof(LogLevel.Information),
    "WARNING" => nameof(LogLevel.Warning),
    "ERROR" => nameof(LogLevel.Error),
    _ => value,
};
