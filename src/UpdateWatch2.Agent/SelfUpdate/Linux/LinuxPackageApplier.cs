using System.Diagnostics;
using System.Runtime.Versioning;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.SelfUpdate.Linux;

/// <summary>
/// Installs an already-downloaded <c>.deb</c>/<c>.rpm</c> via the host's
/// own package manager, launched inside a fresh, detached transient
/// systemd unit rather than as a plain child process of this agent.
/// <paramref name="assetKind"/> picks dpkg vs. rpm, chosen once in
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
/// </para>
///
/// <para>
/// <b>Why a transient <i>service</i> unit, not <c>--scope</c> (agent
/// v1.0.33, fixing `updatewatch2-agent#24`):</b> the first fix for the
/// deadlock above used <c>systemd-run --scope</c>, which creates a
/// brand-new transient unit with its own separate cgroup — <c>prerm</c>
/// stopping <c>updatewatch2-agent.service</c> no longer reaches back and
/// kills the install performing it. That fixed the deadlock but came with
/// a real cost: a <c>--scope</c> unit's process is a *child of the
/// systemd-run client itself*, and <c>systemd-run --wait</c> (which would
/// let this method actually observe the real exit code) is documented —
/// confirmed from this host's own <c>man systemd-run</c>, not assumed —
/// to be **incompatible with <c>--scope</c>**: "`--wait`... may not be
/// combined with `--no-block`, `--scope`, or the various path/socket/timer
/// options." So the original fix could stop the install from being killed,
/// but structurally couldn't also report on whether it actually succeeded.
/// The fix here is dropping <c>--scope</c> in favor of <c>systemd-run</c>'s
/// *default* mode — a transient **service** unit — which has the identical
/// detachment property for a different reason: systemd's own manager
/// (PID 1) forks the process directly via D-Bus activation, so it was
/// never a child of the calling agent process, or even of the
/// <c>systemd-run</c> client process, in the first place. Dropping
/// <c>--scope</c> therefore keeps the "donthack" deadlock fixed *and*
/// makes <c>--wait</c> usable — confirmed correct, not just reasoned
/// about, by a real QEMU-VM repro of both properties together (see below).
/// </para>
///
/// <para>
/// <b>The actual fix</b>: adds <c>--wait</c> (systemd-run blocks until the
/// transient unit terminates and forwards its real exit code as its own —
/// per the same man page: "If systemd-run waits for the service to end,
/// the exit code is forwarded from the service"), bounded by
/// <see cref="ApplyTimeout"/> (generous but finite, so a genuinely hung
/// transaction can't stall a heartbeat tick forever) linked with the
/// caller's own <paramref name="ct"/> via
/// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>.
/// A real, nonzero exit *in dpkg/rpm's own small error-code range* now
/// returns <c>false</c> — this correctly means "the install genuinely
/// failed", not just "this method never found out" — the same distinction
/// `updatewatch2-agent#24` was filed over. A timeout returns `false` too,
/// conservatively. Two real, live-verified regressions were found and
/// fixed while getting here — kept in this comment deliberately, not
/// smoothed over, since both are exactly the kind of thing only a live
/// run catches:
/// </para>
/// <list type="number">
/// <item>A first version also added <c>--pipe</c> (streaming the unit's
/// stdout/stderr live back to this process, to capture the real error
/// text directly). <b>Removed again after a live regression</b>: see
/// <see cref="FetchUnitJournalAsync"/>'s own doc comment for exactly what
/// broke and how error detail is captured instead (a plain, after-the-fact
/// <c>journalctl</c> call).</item>
/// <item>Even without <c>--pipe</c>, the <c>systemd-run</c> *client*
/// process this method spawns (via <see cref="Process.Start()"/>, a plain
/// child of this agent, unlike the transient unit it dbus-activates) is
/// still a member of this agent's own cgroup — so a genuinely successful
/// install, where <c>prerm</c> stops <c>updatewatch2-agent.service</c>
/// partway through, still SIGTERMs this client before it can necessarily
/// observe the real (independent, unaffected) transient unit's true
/// outcome. Live-verified doing exactly this: `dpkg` had genuinely
/// completed successfully (`dpkg -l` showed `ii`, the service really came
/// back up on the new version) while this client's own exit code read 143
/// (128+SIGTERM — a process killed by a signal, not a real application
/// exit) — a false negative. Fixed by treating any exit code in the
/// 128-192 signal-termination range as inconclusive-but-likely-successful
/// (see the code's own comment at that branch) rather than a confirmed
/// failure — a genuine dpkg/rpm-level failure (the actual
/// `updatewatch2-agent#24` scenario) is never in that range, since it
/// exits with the tool's own small error code well before ever reaching
/// `prerm`, so this never masks a real failure, only resolves an ambiguity
/// this specific race can't otherwise avoid.</item>
/// </list>
/// <para>
/// In the success case, this call still can't guarantee it returns before
/// this very process is stopped/replaced mid-<c>prerm</c> — no different
/// from before this fix — but now correctly reports failure instead of a
/// blind "true" whenever it *does* get to observe a real, unambiguous
/// failure, which is exactly the scenario `updatewatch2-agent#24` reported
/// (an early, pre-`prerm` failure — e.g. an architecture mismatch — that
/// never stops this service at all, so this method was always going to
/// survive long enough to see it cleanly, and previously didn't even try).
/// </para>
///
/// <para>
/// <b>Live-verified, not just reasoned about</b> (agent v1.0.33, in an
/// isolated QEMU VM — never a privileged container sharing this project's
/// own shared host's real kernel/cgroups, per the "operational discovery"
/// this session already recorded): both properties confirmed together, on
/// the actual fixed code, across three real attempts (the two regressions
/// above, each caught by actually running the full scenario, then a third,
/// clean run confirming both fixes together). A deliberately
/// architecture-mismatched package correctly makes this method's
/// underlying `systemd-run --wait` invocation exit nonzero with `dpkg`'s
/// real message ("package
/// architecture (arm64) does not match system (amd64)") visible in the
/// unit's journal — where before this fix, the identical scenario
/// silently returned `true` regardless (see `updatewatch2-agent#24`'s own
/// diagnosis comment for the pre-fix behavior). The remaining, narrower
/// exposure this fix doesn't reach: a genuine failure occurring *after*
/// `prerm` has already stopped this very service (this process may not
/// survive to observe or report it either way, same limitation the class
/// always had), and a pre-v1.0.20 agent that could still be offered a
/// mismatched-architecture asset in the first place (a separate,
/// already-fixed gap — see `AgentSelfUpdateService.SelectAsset`).
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxPackageApplier(AgentUpdateAssetKind assetKind, ILogger<LinuxPackageApplier> logger) : IPlatformUpdateApplier
{
    /// <summary>
    /// Builds the <c>dpkg</c>/<c>rpm</c> invocation for an already-
    /// downloaded artifact — pulled out as its own testable pure function
    /// (mirroring <c>Apt</c>/<c>DnfUpdateSession.BuildInstallArgs</c>'s
    /// own convention) after checking, at the user's request, whether
    /// this class was similarly affected by the argument-injection class
    /// that hit those two. It structurally isn't: <paramref name="downloadedFilePath"/>
    /// is always <c>Path.Combine</c> of a fixed, agent-owned absolute
    /// staging directory and a filename — <c>Path.Combine</c> only omits
    /// its first argument when the second is itself rooted, and either
    /// way the result is guaranteed to start with <c>/</c>, never
    /// <c>-</c>, so <c>dpkg</c>/<c>rpm</c>'s argument parser can never
    /// mistake it for a flag the way a bare, unconstrained package-name
    /// string could. The <c>--</c> marker added here anyway is pure
    /// defense-in-depth/consistency with that sibling fix, not a fix for
    /// an actually-reachable issue — both tools' getopt-based parsers
    /// honor it the same standard way, with no behavior change for the
    /// always-absolute path this already only ever receives.
    /// </summary>
    public static (string Command, string[] Args) BuildInstallCommand(AgentUpdateAssetKind assetKind, string downloadedFilePath) =>
        assetKind == AgentUpdateAssetKind.LinuxRpm
            ? ("rpm", ["-U", "--", downloadedFilePath])
            : ("dpkg", ["-i", "--", downloadedFilePath]);

    /// <summary>
    /// Builds the full <c>systemd-run</c> invocation for an already-
    /// downloaded artifact — pulled out as its own testable pure function,
    /// mirroring <see cref="BuildInstallCommand"/>'s own convention, since
    /// the exact flag set here is what `updatewatch2-agent#24`'s fix is
    /// actually about (no `--scope`, <c>--wait</c> added — see this class's
    /// own doc comment for why, and for why <b>not</b> <c>--pipe</c>: a
    /// real live-verified regression, not a theoretical one — see below).
    /// </summary>
    public static string[] BuildSystemdRunArgs(AgentUpdateAssetKind assetKind, string downloadedFilePath, string unitName)
    {
        var (command, installArgs) = BuildInstallCommand(assetKind, downloadedFilePath);
        return [
            "--collect", // clean up the transient unit automatically once it exits — nothing left to leak
            $"--unit={unitName}",
            "--wait", // block until the unit terminates and forward its real exit code as this process's own
            "--", // everything after this is the command to run, never parsed as systemd-run's own options
            command,
            .. installArgs,
        ];
    }

    /// <summary>
    /// Bounds the <c>--wait</c> above — generous for a real
    /// <c>dpkg</c>/<c>rpm</c> transaction, but finite, so a genuinely hung
    /// install can't stall a heartbeat tick forever.
    /// </summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(10);

    public async Task<bool> ApplyAsync(string downloadedFilePath, CancellationToken ct)
    {
        var (command, _) = BuildInstallCommand(assetKind, downloadedFilePath);
        var unitName = $"updatewatch2-agent-selfupdate-{Guid.NewGuid():N}";
        var systemdRunArgs = BuildSystemdRunArgs(assetKind, downloadedFilePath, unitName);

        var startInfo = new ProcessStartInfo("systemd-run") { UseShellExecute = false };
        foreach (var arg in systemdRunArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var timeoutCts = new CancellationTokenSource(ApplyTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            await process.WaitForExitAsync(linkedCts.Token);

            if (process.ExitCode == 0)
            {
                logger.LogInformation(
                    "Applied the downloaded agent update package ({Path}) — this process's own service should be stopped and replaced shortly.",
                    downloadedFilePath);
                return true;
            }

            if (process.ExitCode is >= 128 and <= 128 + 64)
            {
                // The systemd-run CLIENT process we spawned above is a
                // plain child of THIS process — unlike the transient unit
                // it dbus-activates (which really is detached, in its own
                // cgroup, forked directly by PID 1), the client itself is
                // not, and inherits this agent's own cgroup exactly like
                // the very "donthack" deadlock's original plain child
                // process did. On a genuinely successful install, prerm
                // stops updatewatch2-agent.service partway through — which
                // SIGTERMs everything left in its cgroup, including this
                // client — before the real, independent transient unit has
                // necessarily finished (or even before this client's own
                // --wait has had a chance to observe its real outcome). A
                // signal-terminated process's exit code follows the
                // 128+signal convention (SIGTERM=15 -> 143), which is
                // exactly what a real live-verified run of this scenario
                // showed: `dpkg` genuinely completed successfully (`dpkg -l`
                // confirmed `ii`, the service genuinely came back up on the
                // new version) despite this client reporting exit code 143 —
                // a false negative, not a real failure. A genuine
                // dpkg/rpm-level failure (the actual `updatewatch2-agent#24`
                // scenario, e.g. an architecture mismatch) is never in this
                // range — it exits with the tool's own small error code
                // (1, 2, ...) well before ever reaching prerm, so this
                // branch never masks a real failure, only resolves the
                // ambiguity this specific race leaves behind.
                logger.LogWarning(
                    "Could not confirm the outcome of installing the agent update package ({Path}): the waiting process was itself terminated (exit code {ExitCode}, likely by prerm stopping this very service) before it could observe the real result. Treating this as applied, since that is what actually happened when this exact scenario was live-verified.",
                    downloadedFilePath, process.ExitCode);
                return true;
            }

            var journal = await FetchUnitJournalAsync(unitName, ct);
            logger.LogError(
                "Installing the downloaded agent update package ({Path}) failed: {Command} exited with code {ExitCode}. {Journal}",
                downloadedFilePath, command, process.ExitCode, journal);
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The bounded timeout fired, not a genuine shutdown — an
            // unconfirmed outcome is never reported as applied. Killing
            // the systemd-run client here does not affect the detached
            // unit it launched, which keeps running independently either
            // way (that independence is the whole point of not using
            // --scope's alternative, a plain child process).
            logger.LogWarning(
                "Timed out after {Timeout} waiting for the agent update package ({Path}) to install.",
                ApplyTimeout, downloadedFilePath);
            TryKill(process);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to launch the package install via systemd-run.");
            return false;
        }
    }

    /// <summary>
    /// Fetches the failed unit's own journal for a human-readable error
    /// detail — deliberately a separate, after-the-fact <c>journalctl</c>
    /// call rather than <c>systemd-run --pipe</c> streaming the unit's
    /// stdout/stderr back live: a real, live-verified QEMU-VM regression
    /// (agent v1.0.33) found that combining <c>--pipe</c> with this same
    /// self-*replacing* scenario reintroduces a variant of the very
    /// "donthack" deadlock class this file already fixed once, through a
    /// different channel — <c>--pipe</c> relays the unit's stdio live
    /// through the <c>systemd-run</c> *client* process, which (unlike the
    /// detached unit itself) is a plain child of this agent and dies with
    /// it the moment <c>prerm</c> stops this service mid-transaction;
    /// <c>dpkg</c>/<c>rpm</c> then hits a broken pipe/SIGPIPE writing to
    /// its now-gone relay and aborts with a real, unrelated-looking fatal
    /// error (observed exit code 2, dpkg's own generic fatal-error code) —
    /// confirmed live: the identical successful-update scenario that
    /// worked cleanly without <c>--pipe</c> failed exactly this way with
    /// it. The journal itself has no such dependency — systemd captures a
    /// unit's stdio into the journal directly regardless of whether
    /// anything is still listening, so fetching it after the fact, once
    /// <c>--wait</c> has already returned, is unaffected by whether this
    /// agent process (or the systemd-run client it spawned) is still
    /// around by then.
    /// </summary>
    private async Task<string> FetchUnitJournalAsync(string unitName, CancellationToken ct)
    {
        try
        {
            var result = await ShellCommand.RunAsync(
                "journalctl", ["--no-pager", "--output=cat", "-u", $"{unitName}.service"], ct, logger: logger);
            return result.StandardOutput.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(failed to fetch journal: {ex.Message})";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the call — fine, nothing to kill.
        }
    }
}
