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

    public async Task<InstallOutcome> DownloadAndInstallAsync(CancellationToken ct)
    {
        var result = await ShellCommand.RunAsync(
            "apt-get",
            ["-y", "-o", "Dpkg::Options::=--force-confold", "dist-upgrade"],
            ct,
            extraEnvironment: new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" });

        if (result.ExitCode != 0)
        {
            logger.LogWarning("apt-get dist-upgrade exited with code {ExitCode}: {StdErr}", result.ExitCode, result.StandardError.Trim());
            return InstallOutcome.Failed;
        }

        return InstallOutcome.Succeeded;
    }
}
