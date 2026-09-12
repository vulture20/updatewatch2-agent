using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.Reboot.Linux;

/// <summary>
/// Reboots the machine via systemd's own <c>shutdown -r +1 "message"</c> —
/// schedules the reboot and returns immediately, the same "only schedule,
/// never wait" shape <see cref="Windows.WindowsAgentRebooter"/> uses via
/// <c>shutdown.exe</c>. Not to be confused with
/// <c>SelfUpdate.Linux.LinuxPackageApplier</c>'s detached
/// <c>systemd-run --scope</c> dance: that class needs to keep
/// <c>dpkg</c>/<c>rpm</c> itself alive through this very unit being
/// stopped and restarted, mid-transaction — a machine reboot has no
/// equivalent concern, since scheduling it doesn't require anything in
/// this agent's own process (or cgroup) to survive at all; the scheduled
/// job is carried out by systemd's PID 1 independently of whatever
/// eventually happens to this process.
/// </summary>
/// <remarks>
/// <b>NOT live-verified</b> — deliberately, not for lack of a real Linux
/// host: the dev sandbox this codebase is otherwise live-verified against
/// (see CLAUDE.md's many "confirmed live on hpn54l" notes) is a shared
/// environment other work depends on, and actually rebooting it is out of
/// scope for a routine verification pass. Only <see cref="BuildRebootArgs"/>
/// has real test coverage; re-verify a real scheduled reboot on a
/// disposable Linux host before relying on this in production.
/// </remarks>
[SupportedOSPlatform("linux")]
public class LinuxAgentRebooter(ILogger<LinuxAgentRebooter> logger) : IAgentRebooter
{
    // Mirrors WindowsAgentRebooter's own delay/message choice, for
    // consistency across platforms.
    private const string DelayMinutes = "+1";
    private const string Message = "UpdateWatch2: reboot requested by an administrator.";

    /// <summary>
    /// Pulled out as its own testable pure function, mirroring this
    /// codebase's <c>BuildInstallArgs</c>/<c>BuildInstallCommand</c>
    /// convention on the apt/dnf/self-update side.
    /// </summary>
    public static string[] BuildRebootArgs(string delayMinutes, string message) => ["-r", delayMinutes, message];

    public void RequestReboot()
    {
        try
        {
            var startInfo = new ProcessStartInfo("shutdown") { UseShellExecute = false };
            foreach (var arg in BuildRebootArgs(DelayMinutes, Message))
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit(); // shutdown itself returns almost instantly once the reboot is scheduled — not the machine going down.

            logger.LogInformation("Scheduled a machine reboot ({DelayMinutes} min) via shutdown.", DelayMinutes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule a machine reboot via shutdown.");
            throw;
        }
    }
}
