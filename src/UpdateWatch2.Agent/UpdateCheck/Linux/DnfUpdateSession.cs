using System.ComponentModel;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Real dnf/yum-based update checking for RPM-based distros — selected in
/// Program.cs when <see cref="LinuxPackageManagerDetector"/> finds
/// <c>dnf</c> or <c>yum</c> but not <c>apt-get</c>. See
/// <see cref="DnfOutputParser"/>'s own doc comment for the honesty
/// caveat: this whole class, including the install path, has NOT been
/// live-verified against a real dnf/yum host — this project's dev
/// sandbox is Debian-based and has neither tool installed.
/// </summary>
[SupportedOSPlatform("linux")]
public class DnfUpdateSession(ILogger<DnfUpdateSession> logger) : ILinuxUpdateSession
{
    // dnf and yum both use exit code 100 to mean "updates are available"
    // (not an error) and 0 to mean "no updates" — documented in both
    // tools' man pages, distinct from every other nonzero code, which is
    // a genuine failure.
    private const int UpdatesAvailableExitCode = 100;

    private static string ResolveBinary() =>
        File.Exists("/usr/bin/dnf") || File.Exists("/bin/dnf") ? "dnf" : "yum";

    public async Task<UpdateCheckResult> SearchForUpdatesAsync(CancellationToken ct)
    {
        var binary = ResolveBinary();
        var checkUpdate = await ShellCommand.RunAsync(binary, ["-q", "check-update"], ct, logger: logger);
        if (checkUpdate.ExitCode != 0 && checkUpdate.ExitCode != UpdatesAvailableExitCode)
        {
            var stdErr = checkUpdate.StandardError.Trim();
            logger.LogError("{Binary} check-update exited with code {ExitCode}: {StdErr}", binary, checkUpdate.ExitCode, stdErr);
            // Success: false — the RebootRequired check that used to run
            // here regardless is now skipped too, since a failed
            // SearchForUpdatesAsync call is never reported to the server
            // either way (see UpdateCheckResult's own doc comment).
            return UpdateCheckResult.Failed($"{binary} check-update exited with code {checkUpdate.ExitCode}: {stdErr}");
        }

        var available = DnfOutputParser.ParseCheckUpdate(checkUpdate.StandardOutput);
        var updates = available
            .Select(package => new DetectedUpdate(
                Title: $"{package.Package}.{package.Architecture} {package.Version}",
                PackageId: package.Package,
                Description: $"{package.Version} ({package.Repository})"))
            .ToList();

        return new UpdateCheckResult(updates, RebootRequired: await IsRebootRequiredAsync(binary, ct));
    }

    /// <summary>
    /// Builds the <c>dnf</c>/<c>yum</c> argument list for a given
    /// selection — see <c>AptUpdateSession.BuildInstallArgs</c>'s own doc
    /// comment for why this is pulled out as its own testable pure
    /// function and the argument-injection reasoning behind the <c>--</c>
    /// marker; the same applies here verbatim (an RPM package name can't
    /// start with <c>-</c> either, so nothing was actually relying on
    /// that being enforced before this fix, and dnf's argparse-based
    /// parser honors <c>--</c> the same standard way apt-get's does).
    /// </summary>
    public static string[] BuildInstallArgs(IReadOnlyList<string>? packageNames) =>
        packageNames is null
            // null: update everything pending, the original behavior.
            ? ["-y", "update"]
            // Non-null: dnf/yum both accept specific package names as
            // trailing arguments to restrict the update to just those —
            // an admin's way to install only some pending updates while
            // sparing others.
            : ["-y", "update", "--", .. packageNames];

    public async Task<InstallResult> DownloadAndInstallAsync(IReadOnlyList<string>? packageNames, CancellationToken ct)
    {
        var binary = ResolveBinary();
        var args = BuildInstallArgs(packageNames);

        var result = await ShellCommand.RunAsync(binary, args, ct, logger: logger);
        if (result.ExitCode != 0)
        {
            var stdErr = result.StandardError.Trim();
            logger.LogWarning("{Binary} {Args} exited with code {ExitCode}: {StdErr}", binary, string.Join(' ', args), result.ExitCode, stdErr);
            return new InstallResult(InstallOutcome.Failed, $"{binary} exited with code {result.ExitCode}: {stdErr}");
        }

        return new InstallResult(InstallOutcome.Succeeded);
    }

    // dnf's needs-restarting plugin ships as its own not-always-installed
    // package (python3-dnf-plugin-needs-restarting on dnf hosts,
    // yum-utils' standalone `needs-restarting` script on yum ones, hence
    // the different invocation per binary below) — its absence is
    // treated the same as "not required" rather than as an error, the
    // same honest best-effort AptUpdateSession already applies to its own
    // reboot-marker check.
    private async Task<bool> IsRebootRequiredAsync(string binary, CancellationToken ct)
    {
        try
        {
            var result = binary == "dnf"
                ? await ShellCommand.RunAsync("dnf", ["needs-restarting", "-r"], ct, logger: logger)
                : await ShellCommand.RunAsync("needs-restarting", ["-r"], ct, logger: logger);

            // needs-restarting -r: exit 0 = no reboot needed, 1 = reboot needed.
            return result.ExitCode == 1;
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "needs-restarting is not available; cannot determine whether a reboot is required.");
            return false;
        }
    }

    // Public interface member — resolves the binary fresh (two cheap
    // File.Exists calls) and delegates to the same private helper
    // SearchForUpdatesAsync already uses, so this can run standalone on
    // HeartbeatWorker's much shorter cadence without paying for
    // check-update first. Confirmed with the user: a needs-restarting
    // subprocess spawn on every heartbeat (~48x more often than the old
    // every-240-minutes cadence) is still local-only and sub-second —
    // accepted as negligible rather than special-cased with a lighter
    // interval.
    public async Task<RebootCheckResult> IsRebootRequiredAsync(CancellationToken ct) =>
        new(await IsRebootRequiredAsync(ResolveBinary(), ct));
}
