using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.SelfUpdate.Linux;

/// <summary>
/// Installs an already-downloaded <c>.deb</c>/<c>.rpm</c> via the host's
/// own package manager, launched inside a fresh, detached
/// <c>systemd-run --scope</c> rather than as a plain child process of this
/// agent. <paramref name="assetKind"/> picks dpkg vs. rpm, chosen once in
/// <c>Program.cs</c> the same way <c>LinuxPackageManagerDetector</c>
/// already picks between <c>AptUpdateSession</c> and <c>DnfUpdateSession</c>.
///
/// <para>
/// <b>Why <c>systemd-run</c>, not just <c>Process.Start</c>:</b> an earlier
/// version of this class ran <c>dpkg -i</c>/<c>rpm -U</c> as a plain
/// awaited child of this process and only fired the restart afterward —
/// which deadlocked every single time, confirmed live (agent v0.12.2,
/// host "donthack"): <c>dpkg -i</c> upgrading an already-installed package
/// runs the OLD package's <c>prerm</c> hook mid-transaction
/// (<c>installer/linux/prerm.sh</c>), which stops THIS systemd unit to
/// release the file lock before new files are unpacked. This unit's
/// <c>updatewatch2-agent.service</c> has no <c>KillMode=</c> override, so
/// systemd's default (<c>control-group</c>) sends SIGTERM to every process
/// in its cgroup when stopped — not just this agent's own main PID, but
/// also the <c>dpkg</c> child process it had just spawned, since a plain
/// child inherits its parent's cgroup. <c>dpkg</c> died mid-transaction
/// (exit code 143 = SIGTERM) before ever finishing, and the explicit
/// <c>systemctl restart</c> this class used to run afterward was never
/// even reached. A *manually* run <c>dpkg -i</c> (an admin's own SSH
/// session, verified working — see CLAUDE.md's ".deb upgrade doesn't
/// restart" note) never hits this, because that shell's cgroup is
/// unrelated to this unit's — only a *self*-triggered install, running
/// inside the very unit prerm is about to stop, deadlocks this way.
/// <c>systemd-run --scope</c> creates a brand-new transient unit with its
/// own separate cgroup, so <c>prerm</c> stopping
/// <c>updatewatch2-agent.service</c> no longer reaches back and kills the
/// install performing it — <c>dpkg</c>/<c>rpm</c> now completes its normal
/// stop-old/unpack/restart-new transaction (via
/// <c>installer/linux/postinst.sh</c>'s own already-correct restart logic)
/// exactly as a manual upgrade already does, just triggered by this agent
/// instead of an admin.
/// </para>
///
/// <para>
/// A consequence of detaching this way: this method can no longer observe
/// or report the install's actual exit code — it only confirms the
/// detached scope was launched, not that <c>dpkg</c>/<c>rpm</c> inside it
/// actually succeeded (that only happens after this agent process is
/// itself replaced/restarted, at which point nothing here is left running
/// to report it anyway). A genuine install failure is now visible via
/// <c>journalctl -u updatewatch2-agent-selfupdate-*.scope</c> on the host,
/// not this agent's own log — the same visibility trade-off this class's
/// final <c>systemctl restart</c> call already accepted even before this
/// fix, just now extended to the whole apply step. If it never restarts
/// on the new version, the next heartbeat's offer simply gets retried.
/// </para>
///
/// <para>
/// Confirmed live for the actual deadlock this fixes (the "donthack"
/// incident above); the fixed detached-scope path itself is <b>NOT YET
/// live-verified</b> — same honesty caveat this project's other
/// Linux-install code already carries (this dev sandbox has no systemd —
/// see CLAUDE.md's note on <c>AptUpdateSession</c>), and installing a
/// package here would mutate the sandbox's real system. Re-verify a real
/// self-update cycle (offer -> download -> detached install -> new
/// version reporting on the next heartbeat) on a real systemd host before
/// trusting this further.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxPackageApplier(AgentUpdateAssetKind assetKind, ILogger<LinuxPackageApplier> logger) : IPlatformUpdateApplier
{
    public Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct)
    {
        var (command, installArgs) = assetKind == AgentUpdateAssetKind.LinuxRpm
            ? ("rpm", new[] { "-U", downloadedFilePath })
            : ("dpkg", new[] { "-i", downloadedFilePath });

        try
        {
            var startInfo = new ProcessStartInfo("systemd-run") { UseShellExecute = false };
            startInfo.ArgumentList.Add("--collect"); // clean up the transient scope automatically once it exits — nothing left to leak
            startInfo.ArgumentList.Add("--scope");
            startInfo.ArgumentList.Add($"--unit=updatewatch2-agent-selfupdate-{Guid.NewGuid():N}");
            startInfo.ArgumentList.Add("--"); // everything after this is the command to run, never parsed as systemd-run's own options
            startInfo.ArgumentList.Add(command);
            foreach (var arg in installArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Deliberately not awaited — see this class's doc comment.
            // Stopping this unit (which the package's own prerm hook does
            // moments from now) sends SIGTERM to this process, but no
            // longer to the detached scope performing the install.
            Process.Start(startInfo);

            logger.LogInformation(
                "Launched the downloaded agent update package ({Path}) inside a detached systemd scope — " +
                "this process's own service will be stopped and replaced shortly.",
                downloadedFilePath);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to launch the detached package install via systemd-run.");
            return Task.FromResult(false);
        }
    }
}
