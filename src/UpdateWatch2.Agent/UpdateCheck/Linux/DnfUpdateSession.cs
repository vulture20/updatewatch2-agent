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
        var checkUpdate = await ShellCommand.RunAsync(binary, ["-q", "check-update"], ct);
        if (checkUpdate.ExitCode != 0 && checkUpdate.ExitCode != UpdatesAvailableExitCode)
        {
            logger.LogError(
                "{Binary} check-update exited with code {ExitCode}: {StdErr}",
                binary, checkUpdate.ExitCode, checkUpdate.StandardError.Trim());
            return new UpdateCheckResult([], RebootRequired: await IsRebootRequiredAsync(binary, ct));
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

    public async Task<InstallOutcome> DownloadAndInstallAsync(CancellationToken ct)
    {
        var binary = ResolveBinary();
        var result = await ShellCommand.RunAsync(binary, ["-y", "update"], ct);
        if (result.ExitCode != 0)
        {
            logger.LogWarning("{Binary} update exited with code {ExitCode}: {StdErr}", binary, result.ExitCode, result.StandardError.Trim());
            return InstallOutcome.Failed;
        }

        return InstallOutcome.Succeeded;
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
                ? await ShellCommand.RunAsync("dnf", ["needs-restarting", "-r"], ct)
                : await ShellCommand.RunAsync("needs-restarting", ["-r"], ct);

            // needs-restarting -r: exit 0 = no reboot needed, 1 = reboot needed.
            return result.ExitCode == 1;
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "needs-restarting is not available; cannot determine whether a reboot is required.");
            return false;
        }
    }
}
