using System.ComponentModel;
using System.Runtime.Versioning;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Real dnf/yum-based update checking for RPM-based distros — selected in
/// Program.cs when <see cref="LinuxPackageManagerDetector"/> finds
/// <c>dnf</c> or <c>yum</c> but not <c>apt-get</c>.
///
/// <para>
/// Live-verified end to end against a real Fedora 44 host (agent v1.0.28,
/// via the throwaway Fedora container <c>scripts/run-fedora-test-server.sh</c>
/// stands up, exercised by <c>DnfIntegrationTests</c>): real <c>check-update</c>,
/// standalone <c>needs-restarting -r</c>, <c>--downloadonly</c>, and both a
/// scoped (selective) and a full <c>update</c> install all ran and behaved
/// exactly as coded, including <c>needs-restarting -r</c> correctly flipping
/// to "reboot required" after upgrading core system libraries. One thing
/// worth knowing, not a bug: Fedora 44 ships dnf5 (Rust-based — both
/// <c>/usr/bin/dnf</c> and <c>/usr/bin/yum</c> are symlinks to it), which is
/// CLI-compatible with every command this class uses and — unlike classic
/// dnf/yum — bundles its own <c>needs_restarting</c> plugin, so
/// <see cref="IsRebootRequiredAsync(CancellationToken)"/> worked with no
/// extra package installed at all. This class's own <see cref="ResolveBinary"/>
/// <c>yum</c> fallback (a genuinely older/classic-dnf or bare-yum RPM host)
/// has still never been run for real — treat that specific branch as
/// well-researched but not live-verified, the same honesty caveat the
/// whole class used to carry.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class DnfUpdateSession(ILogger<DnfUpdateSession> logger, AgentOptions options) : ILinuxUpdateSession
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
    /// <param name="packageNames">Null updates everything pending; non-null scopes to just these.</param>
    /// <param name="allowUnauthenticated">
    /// Mirrors <see cref="AgentOptions.AllowUnauthenticatedPackages"/> —
    /// when true, adds dnf/yum's own <c>--nogpgcheck</c> flag (their
    /// equivalent of apt-get's <c>--allow-unauthenticated</c>) so a
    /// package with an invalid/missing GPG signature doesn't fail the
    /// whole transaction. Placed before the <c>--</c> marker like every
    /// other flag here, never after it.
    /// </param>
    public static string[] BuildInstallArgs(IReadOnlyList<string>? packageNames, bool allowUnauthenticated = false) =>
        packageNames is null
            // null: update everything pending, the original behavior.
            ? ["-y", .. allowUnauthenticated ? new[] { "--nogpgcheck" } : [], "update"]
            // Non-null: dnf/yum both accept specific package names as
            // trailing arguments to restrict the update to just those —
            // an admin's way to install only some pending updates while
            // sparing others.
            : ["-y", .. allowUnauthenticated ? new[] { "--nogpgcheck" } : [], "update", "--", .. packageNames];

    public async Task<InstallResult> DownloadAndInstallAsync(IReadOnlyList<string>? packageNames, CancellationToken ct)
    {
        if (options.AllowUnauthenticatedPackages)
        {
            logger.LogWarning("AllowUnauthenticatedPackages is enabled; dnf/yum will accept packages with an invalid or missing GPG signature.");
        }

        var binary = ResolveBinary();
        var args = BuildInstallArgs(packageNames, options.AllowUnauthenticatedPackages);

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

    /// <summary>
    /// Always everything currently pending — see
    /// <c>ILinuxUpdateSession.DownloadOnlyAsync</c>'s own doc comment.
    /// <c>--downloadonly</c> is a core dnf option (no plugin needed); on
    /// yum it requires the separate <c>yum-plugin-downloadonly</c> package
    /// — if that's missing, the command fails and this reports it as an
    /// ordinary <see cref="PreDownloadResult"/> failure rather than
    /// crashing, the same honest-best-effort treatment
    /// <see cref="IsRebootRequiredAsync(CancellationToken)"/>'s own
    /// needs-restarting fallback already applies to a missing optional
    /// tool.
    /// </summary>
    /// <param name="allowUnauthenticated">See <see cref="BuildInstallArgs"/>'s own parameter of the same name.</param>
    public static string[] BuildDownloadOnlyArgs(bool allowUnauthenticated = false) =>
        ["-y", .. allowUnauthenticated ? new[] { "--nogpgcheck" } : [], "update", "--downloadonly"];

    public async Task<PreDownloadResult> DownloadOnlyAsync(CancellationToken ct)
    {
        if (options.AllowUnauthenticatedPackages)
        {
            logger.LogWarning("AllowUnauthenticatedPackages is enabled; dnf/yum will accept packages with an invalid or missing GPG signature.");
        }

        var binary = ResolveBinary();
        var args = BuildDownloadOnlyArgs(options.AllowUnauthenticatedPackages);

        var result = await ShellCommand.RunAsync(binary, args, ct, logger: logger);
        if (result.ExitCode != 0)
        {
            var stdErr = result.StandardError.Trim();
            logger.LogWarning("{Binary} {Args} exited with code {ExitCode}: {StdErr}", binary, string.Join(' ', args), result.ExitCode, stdErr);
            return new PreDownloadResult(false, $"{binary} exited with code {result.ExitCode}: {stdErr}");
        }

        return new PreDownloadResult(true);
    }
}
