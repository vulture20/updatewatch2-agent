using System.Diagnostics;
using System.Runtime.Versioning;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.SelfUpdate.Windows;

/// <summary>
/// Silently re-runs this project's own NSIS installer
/// (<c>installer/nsis/setup.nsi</c>) to upgrade the running
/// <c>UpdateWatch2 Agent</c> Windows service in place — the "re-run the
/// installer as a detached child process right before this process exits"
/// option updatewatch2-agent#14's pinned issue comment chose over a
/// separate updater-helper executable for a first cut. <c>/S</c> plus the
/// installer's existing unattended-install flags
/// (<c>/SERVERADDRESS=</c>/<c>/SERVERPORT=</c>) preserve this agent's
/// current server configuration across the upgrade.
///
/// <para>
/// <b>NOT live-verified</b> — same honesty caveat as <c>WuaUpdateSession</c>
/// and every other Windows-only class in this codebase (no Windows host
/// was available when this was written). In particular, this assumes the
/// Windows Service Control Manager does not kill a service's already-
/// launched child processes when the service itself stops — true for an
/// ordinary service that isn't placed in a kill-on-job-close Job Object,
/// which is the SCM's default, but unconfirmed against a real installer
/// run. Re-verify this specific assumption (does the installer process
/// launched here actually survive long enough to stop/replace/restart the
/// service?) on a real Windows host before relying on it in production.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsInstallerApplier(AgentOptions options, ILogger<WindowsInstallerApplier> logger) : IPlatformUpdateApplier
{
    public Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo(downloadedFilePath)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("/S");
            startInfo.ArgumentList.Add($"/SERVERADDRESS={options.ServerAddress}");
            startInfo.ArgumentList.Add($"/SERVERPORT={options.ServerPort}");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                logger.LogError("Launching the downloaded installer did not return a process handle.");
                return Task.FromResult(false);
            }

            logger.LogInformation(
                "Launched the downloaded installer ({Path}) silently to upgrade this agent in place — " +
                "this process's own service may be stopped and replaced shortly.",
                downloadedFilePath);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to launch the downloaded installer.");
            return Task.FromResult(false);
        }
    }
}
