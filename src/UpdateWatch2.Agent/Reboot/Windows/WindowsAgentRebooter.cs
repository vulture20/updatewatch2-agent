using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.Reboot.Windows;

/// <summary>
/// Reboots the machine via the built-in <c>shutdown.exe /r</c> — simpler
/// than the earlier service-restart mechanism this replaced (which needed
/// a detached helper script, since stopping this agent's own service via
/// SCM would terminate this process before it could run a follow-up
/// "start" command itself): <c>shutdown.exe</c> only SCHEDULES the reboot
/// with the OS and returns immediately, regardless of what process
/// invoked it or whether that process is still running by the time the
/// scheduled reboot actually happens.
///
/// <para>
/// <b>NOT live-verified on Windows</b> — same honesty caveat this
/// codebase already carries for every other Windows-only class
/// (<c>WuaUpdateSession</c>, <c>WindowsInstallerApplier</c>): no Windows
/// host was available when this was written.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAgentRebooter(ILogger<WindowsAgentRebooter> logger) : IAgentRebooter
{
    // A short grace period rather than an immediate /t 0 — gives this
    // agent's own AcknowledgeRebootAsync call a moment to actually reach
    // the server before the machine goes down. 10s is comfortably more
    // than that HTTP call ever takes in practice; a full 60s (this
    // constant's original value) was reported as needlessly long for
    // what's an automated admin action, not a user-facing warning period.
    private const int DelaySeconds = 10;
    private const string Message = "UpdateWatch2: reboot requested by an administrator.";

    /// <summary>
    /// Pulled out as its own testable pure function, mirroring this
    /// codebase's <c>BuildInstallArgs</c>/<c>BuildInstallCommand</c>
    /// convention on the Linux/apt/dnf/self-update side.
    /// </summary>
    public static string[] BuildRebootArgs(int delaySeconds, string message) =>
        ["/r", "/t", delaySeconds.ToString(), "/c", message];

    public void RequestReboot()
    {
        try
        {
            var startInfo = new ProcessStartInfo("shutdown.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in BuildRebootArgs(DelaySeconds, Message))
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit(); // shutdown.exe itself exits almost instantly once the reboot is scheduled — not the machine going down.

            logger.LogInformation("Scheduled a machine reboot in {DelaySeconds}s via shutdown.exe.", DelaySeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule a machine reboot via shutdown.exe.");
            throw;
        }
    }
}
