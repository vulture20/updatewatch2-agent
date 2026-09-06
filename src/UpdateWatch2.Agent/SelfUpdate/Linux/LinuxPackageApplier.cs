using System.Diagnostics;
using System.Runtime.Versioning;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.SelfUpdate.Linux;

/// <summary>
/// Installs an already-downloaded <c>.deb</c>/<c>.rpm</c> via the host's
/// own package manager, then fires a detached <c>systemctl restart</c> for
/// this agent's own systemd unit (<c>installer/linux/updatewatch2-agent.service</c>)
/// — mirroring <c>AptUpdateSession</c>/<c>DnfUpdateSession</c>'s existing
/// "shell out to the platform's own tooling" convention rather than
/// reimplementing package installation. <paramref name="assetKind"/> picks
/// dpkg vs. rpm, chosen once in <c>Program.cs</c> the same way
/// <c>LinuxPackageManagerDetector</c> already picks between
/// <c>AptUpdateSession</c> and <c>DnfUpdateSession</c>.
///
/// <para>
/// The restart is deliberately fire-and-forget rather than awaited:
/// <c>systemctl restart</c> sends SIGTERM to THIS process (the current
/// main PID of the unit being restarted) as part of stopping it, so
/// awaiting it from inside this same process would mean waiting for our
/// own termination. Overwriting a running executable's file is safe on
/// Linux — the kernel keeps the old, now-unlinked inode backing the still-
/// running process alive until it exits — so the new binary only actually
/// takes effect once systemd restarts the unit.
/// </para>
///
/// <para>
/// <b>NOT live-verified</b> — this project's own dev sandbox has no
/// systemd (see CLAUDE.md's note on <c>AptUpdateSession</c>) and actually
/// installing a package here would mutate the sandbox's real system, the
/// same reason <c>AptUpdateSession.DownloadAndInstallAsync</c> itself was
/// never live-run either.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxPackageApplier(AgentUpdateAssetKind assetKind, ILogger<LinuxPackageApplier> logger) : IPlatformUpdateApplier
{
    private const string ServiceName = "updatewatch2-agent";

    public async Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct)
    {
        var install = assetKind == AgentUpdateAssetKind.LinuxRpm
            ? await ShellCommand.RunAsync("rpm", ["-U", downloadedFilePath], ct)
            : await ShellCommand.RunAsync("dpkg", ["-i", downloadedFilePath], ct);

        if (install.ExitCode != 0)
        {
            logger.LogError(
                "Failed to install the downloaded agent update package (exit code {ExitCode}): {StdErr}",
                install.ExitCode, install.StandardError.Trim());
            return false;
        }

        try
        {
            // Deliberately not awaited to completion — see this class's
            // doc comment: restarting this unit sends SIGTERM to this same
            // process.
            Process.Start(new ProcessStartInfo("systemctl", ["restart", ServiceName]) { UseShellExecute = false });
            logger.LogInformation("Installed the agent update package and requested a service restart to apply it.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Installed the agent update package but failed to trigger a service restart — a manual `systemctl restart {ServiceName}` is needed.",
                ServiceName);
            return false;
        }
    }
}
