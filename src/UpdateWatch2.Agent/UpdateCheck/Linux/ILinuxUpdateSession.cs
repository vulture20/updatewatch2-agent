namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Mirrors <see cref="Windows.IWindowsUpdateSession"/>'s testability seam
/// on the Linux side: the real, package-manager-shelling-out
/// implementation (<see cref="AptUpdateSession"/> for Debian-derived
/// distros, <see cref="DnfUpdateSession"/> for RPM-based ones) is
/// selected in Program.cs via <see cref="LinuxPackageManagerDetector"/>,
/// while <see cref="LinuxUpdateChecker"/> depends only on this interface
/// and is tested against a hand-written fake — the same
/// testable-orchestrator/untestable-OS-session split
/// <c>WindowsUpdateChecker</c>/<c>WuaUpdateSession</c> already established.
/// </summary>
public interface ILinuxUpdateSession
{
    Task<UpdateCheckResult> SearchForUpdatesAsync(CancellationToken ct);

    /// <summary>
    /// Installs whatever is currently pending — or, when
    /// <paramref name="packageNames"/> is non-null, only those named
    /// packages (an admin's way to install only some updates while
    /// sparing others; every Linux update this codebase reports always
    /// has a non-null <c>PackageId</c>, unlike the rare Windows case, so
    /// there's no "can't be individually named" fallback needed here).
    /// </summary>
    Task<InstallResult> DownloadAndInstallAsync(IReadOnlyList<string>? packageNames, CancellationToken ct);

    /// <summary>
    /// Reads whether a reboot is currently pending, independent of a full
    /// search — the same underlying signal <see cref="SearchForUpdatesAsync"/>
    /// already reports as part of its result, exposed standalone so it can
    /// run on a much shorter cadence than a full search is worth paying
    /// for. See <see cref="AptUpdateSession"/>/<see cref="DnfUpdateSession"/>
    /// for how cheap this is per platform.
    /// </summary>
    Task<RebootCheckResult> IsRebootRequiredAsync(CancellationToken ct);
}
