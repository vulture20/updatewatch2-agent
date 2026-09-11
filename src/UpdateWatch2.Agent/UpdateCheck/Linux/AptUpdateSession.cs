using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Real apt/dpkg-based update checking for Debian-derived distros —
/// selected in Program.cs when <see cref="LinuxPackageManagerDetector"/>
/// finds <c>apt-get</c> on the host.
///
/// <para>
/// Unlike <c>WuaUpdateSession</c>'s COM interop, the search half of this
/// class WAS live-verified in this project's own dev sandbox (Debian 13
/// trixie): <c>apt list --upgradable</c> was run for real against the
/// sandbox's actual, unmodified package cache, and <see cref="AptOutputParser"/>
/// is written and tested against that real output shape (including the
/// locale trap documented on <see cref="ShellCommand"/>), not a guess
/// from apt's man page. <see cref="DownloadAndInstallAsync"/> was
/// deliberately never run for real — doing so would mutate that
/// sandbox's installed packages — so treat the install half the same as
/// every other not-live-verified install path in this codebase.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public class AptUpdateSession(ILogger<AptUpdateSession> logger) : ILinuxUpdateSession
{
    // Written by update-notifier-common after certain installs (most
    // notably a new kernel) — absent both when no reboot is needed AND
    // when that package isn't installed at all (common on a minimal/
    // container-style Debian install, confirmed in this project's own
    // sandbox). Both cases are treated the same: no evidence a reboot is
    // required, the same honest best-effort this codebase already
    // applies to signals it can't always determine for sure.
    private const string RebootRequiredMarker = "/var/run/reboot-required";

    public async Task<UpdateCheckResult> SearchForUpdatesAsync(CancellationToken ct)
    {
        var refresh = await ShellCommand.RunAsync("apt-get", ["-qq", "update"], ct);
        if (refresh.ExitCode != 0)
        {
            logger.LogWarning(
                "apt-get update exited with code {ExitCode}; continuing with whatever package list is already cached. stderr: {StdErr}",
                refresh.ExitCode, refresh.StandardError.Trim());
        }

        var listing = await ShellCommand.RunAsync("apt", ["list", "--upgradable"], ct);
        var upgradable = AptOutputParser.ParseUpgradable(listing.StandardOutput);

        var updates = upgradable
            .Select(package => new DetectedUpdate(
                Title: $"{package.Package} {package.NewVersion}",
                PackageId: package.Package,
                Description: $"{package.OldVersion} → {package.NewVersion}"))
            .ToList();

        return new UpdateCheckResult(updates, RebootRequired: File.Exists(RebootRequiredMarker));
    }

    /// <summary>
    /// Builds the <c>apt-get</c> argument list for a given selection —
    /// pulled out as its own testable pure function (mirroring
    /// <see cref="AptOutputParser"/>'s own public-static-method
    /// convention) after an automated security review flagged the
    /// original inline version as an argument-injection risk:
    /// <paramref name="packageNames"/> ultimately traces back to
    /// <c>Db.Entities.UpdateItem.PackageId</c> as parsed from this
    /// agent's own <c>apt list --upgradable</c> output — not normally
    /// attacker-controlled, but a real Debian package name can't start
    /// with <c>-</c> by policy, so nothing here was actually relying on
    /// that being enforced anywhere. Without a <c>--</c> end-of-options
    /// marker, a value that *did* start with <c>-</c> (a malicious/
    /// compromised third-party repo entry, say) would be parsed by
    /// <c>apt-get</c> as an additional flag rather than a package name —
    /// classic option-smuggling argument injection, not shell injection
    /// (this project never goes through a shell — see <see cref="ShellCommand"/> —
    /// so metacharacters like <c>;</c>/<c>|</c> were never the risk here).
    /// The <c>--</c> marker is the standard, complete fix: everything
    /// after it is always treated as a positional package name by
    /// <c>apt-get</c>'s GNU-style argument parser, regardless of what it
    /// starts with.
    /// </summary>
    public static string[] BuildInstallArgs(IReadOnlyList<string>? packageNames) =>
        packageNames is null
            // null: upgrade everything pending, the original behavior.
            ? ["-y", "-o", "Dpkg::Options::=--force-confold", "dist-upgrade"]
            // Non-null: an admin selected only some packages to install —
            // "install --only-upgrade" restricts itself to packages that
            // already have a newer version available, the same semantics
            // dist-upgrade already has, just scoped to exactly these
            // names rather than a plain "install" that could otherwise
            // pull in something that isn't actually an upgrade.
            : ["-y", "-o", "Dpkg::Options::=--force-confold", "install", "--only-upgrade", "--", .. packageNames];

    public async Task<InstallOutcome> DownloadAndInstallAsync(IReadOnlyList<string>? packageNames, CancellationToken ct)
    {
        var args = BuildInstallArgs(packageNames);

        var result = await ShellCommand.RunAsync(
            "apt-get",
            args,
            ct,
            extraEnvironment: new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" });

        if (result.ExitCode != 0)
        {
            logger.LogWarning("apt-get {Args} exited with code {ExitCode}: {StdErr}", string.Join(' ', args), result.ExitCode, result.StandardError.Trim());
            return InstallOutcome.Failed;
        }

        return InstallOutcome.Succeeded;
    }
}
