namespace UpdateWatch2.Agent.UpdateCheck.Linux;

public enum LinuxPackageManagerKind
{
    Apt,
    Dnf,
    None,
}

/// <summary>
/// Picks which <see cref="ILinuxUpdateSession"/> Program.cs should
/// register, by checking for the package-manager binaries this project
/// already ships packages for (<c>installer/linux/</c> builds both a
/// <c>.deb</c> and an <c>.rpm</c>, so both families need a real
/// implementation, not just one). <c>yum</c> is treated the same as
/// <c>dnf</c> — <see cref="DnfUpdateSession"/> resolves which of the two
/// binaries actually exists on its own when it runs, since on a modern
/// Fedora/RHEL host <c>yum</c> is usually just a symlink to <c>dnf</c>
/// anyway.
/// </summary>
public static class LinuxPackageManagerDetector
{
    private static readonly string[] AptPaths = ["/usr/bin/apt-get", "/bin/apt-get"];
    private static readonly string[] DnfPaths = ["/usr/bin/dnf", "/bin/dnf"];
    private static readonly string[] YumPaths = ["/usr/bin/yum", "/bin/yum"];

    /// <param name="fileExists">
    /// Injectable for testing — defaults to <see cref="File.Exists(string)"/>.
    /// </param>
    public static LinuxPackageManagerKind Detect(Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (AptPaths.Any(fileExists))
        {
            return LinuxPackageManagerKind.Apt;
        }

        if (DnfPaths.Any(fileExists) || YumPaths.Any(fileExists))
        {
            return LinuxPackageManagerKind.Dnf;
        }

        return LinuxPackageManagerKind.None;
    }
}
