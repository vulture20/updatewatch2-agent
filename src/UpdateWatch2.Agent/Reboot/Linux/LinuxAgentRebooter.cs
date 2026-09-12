using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.Reboot.Linux;

/// <summary>
/// Reboots the machine by scheduling a one-shot systemd timer —
/// <c>systemd-run --on-active=&lt;seconds&gt; -- systemctl reboot</c> —
/// rather than the classic <c>shutdown -r +&lt;minutes&gt;</c> this class
/// used originally. <c>shutdown</c>'s own <c>+m</c> syntax only accepts
/// whole minutes (no wall-message-free way to get, say, a 10-second
/// delay), which meant this class couldn't match
/// <see cref="Windows.WindowsAgentRebooter"/>'s second-granular delay —
/// found by a user report comparing the two: Windows' `/t 60` and Linux's
/// `+1` LOOK like the same "1" at a glance, but `+1` in <c>shutdown</c>
/// syntax means 1 *minute*, not 1 second, so they were never actually as
/// different as that comparison suggested; switching to
/// <c>systemd-run --on-active=</c> (which takes plain seconds) is what
/// actually lets both platforms share one exact delay constant.
///
/// <para>
/// <c>systemd-run</c> only SCHEDULES the timer and returns immediately —
/// the same "only schedule, never wait" shape
/// <see cref="Windows.WindowsAgentRebooter"/> uses via <c>shutdown.exe</c>.
/// Not to be confused with <c>SelfUpdate.Linux.LinuxPackageApplier</c>'s
/// own <c>systemd-run --scope</c> use: that class needs to keep
/// <c>dpkg</c>/<c>rpm</c> itself alive through this very unit being
/// stopped and restarted, mid-transaction — a machine reboot has no
/// equivalent concern, since nothing in this agent's own process (or
/// cgroup) needs to survive for the scheduled job to run; it's carried
/// out by systemd's PID 1 independently of whatever eventually happens to
/// this process. One real trade-off from dropping <c>shutdown</c>:
/// <c>systemd-run</c> has no equivalent of its wall-broadcast message to
/// logged-in users, so that's gone — acceptable for a headless server
/// agent, and Windows' own <c>/c "message"</c> was likewise never
/// essential to the feature, just a courtesy.
/// </para>
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
    // Same value, same reasoning, as WindowsAgentRebooter.DelaySeconds —
    // long enough for AcknowledgeRebootAsync to reach the server, nowhere
    // near the minute-plus this used to be stuck at on either platform.
    private const int DelaySeconds = 10;

    /// <summary>
    /// Pulled out as its own testable pure function, mirroring this
    /// codebase's <c>BuildInstallArgs</c>/<c>BuildInstallCommand</c>
    /// convention on the apt/dnf/self-update side. <paramref name="unitName"/>
    /// is unique per call (a fresh GUID) so two reboot requests in quick
    /// succession can't collide on the same transient unit name.
    /// </summary>
    public static string[] BuildRebootArgs(int delaySeconds, string unitName) =>
        ["--collect", $"--unit={unitName}", $"--on-active={delaySeconds}", "--", "systemctl", "reboot"];

    public void RequestReboot()
    {
        try
        {
            var unitName = $"updatewatch2-agent-reboot-{Guid.NewGuid():N}";
            var startInfo = new ProcessStartInfo("systemd-run") { UseShellExecute = false };
            foreach (var arg in BuildRebootArgs(DelaySeconds, unitName))
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit(); // systemd-run itself returns almost instantly once the timer is scheduled — not the machine going down.

            logger.LogInformation("Scheduled a machine reboot in {DelaySeconds}s via systemd-run.", DelaySeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule a machine reboot via systemd-run.");
            throw;
        }
    }
}
