using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.Restart.Linux;

/// <summary>
/// Restarts the <c>updatewatch2-agent</c> systemd unit via a plain
/// <c>systemctl restart --no-block</c> child process — deliberately NOT
/// wrapped in a detached <c>systemd-run --scope</c> the way
/// <c>SelfUpdate.Linux.LinuxPackageApplier</c> has to be for its own
/// dpkg/rpm install step.
///
/// <para>
/// <b>Why this is safe without the cgroup-escape trick, unlike
/// <c>LinuxPackageApplier</c>:</b> that class's own doc comment explains a
/// real, live-confirmed deadlock — <c>dpkg</c>/<c>rpm</c> itself has to
/// keep running, mid-transaction, all the way through the point where
/// this unit gets stopped (by the package's own <c>prerm</c> hook) and
/// then started again (by <c>postinst.sh</c>) — and a plain child process
/// sharing this unit's cgroup gets SIGTERM'd the instant the stop happens,
/// killing that in-flight transaction before it can finish. A bare
/// <c>systemctl restart</c> has no equivalent problem: <c>systemctl</c> is
/// only a thin client that submits one job ("stop then start this unit")
/// to systemd's PID 1 manager process over D-Bus and then returns — the
/// actual stop-then-start sequence is carried out entirely by PID 1,
/// which is not part of this unit's cgroup and is never affected by it
/// being torn down. Nothing in this agent's own cgroup needs to survive
/// for the restart to actually happen, unlike <c>dpkg</c>/<c>rpm</c>
/// needing to survive to reach their own "start the new version" step.
/// <c>--no-block</c> is passed anyway so the <c>systemctl</c> child
/// returns the instant the job is queued rather than waiting around for a
/// "job complete" reply that may never arrive once this process (and
/// likely the <c>systemctl</c> child too, being in the same cgroup) gets
/// SIGTERM'd moments later.
/// </para>
///
/// <para>
/// <b>Live-verified</b> — not just reasoned about — with a throwaway
/// harness on a real systemd host (hostname <c>hpn54l</c>, systemd 257):
/// a transient unit's own process called <c>systemctl restart --no-block</c>
/// on itself, and a fresh instance of the unit (a new process, appending a
/// new line to a shared log file) came up afterward — confirmed
/// repeatedly, since the harness script had no delay before re-triggering
/// its own restart and kept doing so every time a fresh instance started,
/// until systemd's own start-limit-burst protection (5 restarts by
/// default) stopped it — exactly the shape of confirmation this class's
/// production use needs, just exercised faster and more often than a
/// real admin-triggered restart ever would be.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxAgentRestarter(ILogger<LinuxAgentRestarter> logger) : IAgentRestarter
{
    // Must stay identical to installer/linux/updatewatch2-agent.service's
    // own unit file name (the systemd unit name is that file's basename).
    private const string UnitName = "updatewatch2-agent";

    /// <summary>
    /// Pulled out as its own testable pure function, mirroring
    /// <c>Apt</c>/<c>DnfUpdateSession.BuildInstallArgs</c>'s convention —
    /// <c>--</c> guards against a (here, hardcoded and never attacker-
    /// controlled) unit name that could otherwise be misread as an
    /// additional flag by systemctl's argument parser, the same
    /// defense-in-depth already applied to those sibling command-builders.
    /// </summary>
    public static string[] BuildRestartArgs(string unitName) => ["restart", "--no-block", "--", unitName];

    public void RequestRestart()
    {
        try
        {
            var startInfo = new ProcessStartInfo("systemctl") { UseShellExecute = false };
            foreach (var arg in BuildRestartArgs(UnitName))
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Deliberately not awaited — see this class's own doc comment
            // on why that's safe here, unlike the detached-scope dance
            // LinuxPackageApplier needs for its own dpkg/rpm install step.
            Process.Start(startInfo);

            logger.LogInformation("Requested a restart of the '{UnitName}' systemd unit.", UnitName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to request a restart via systemctl.");
            throw;
        }
    }
}
