using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UpdateWatch2.Agent.Communication;

/// <summary>
/// Produces the <see cref="RegisterRequest.OperatingSystem"/> value. On
/// Windows, <see cref="RuntimeInformation.OSDescription"/> alone is a
/// generic string like "Microsoft Windows 10.0.26200" — not what an admin
/// looking at the agent overview wants ("is this actually Windows 11? A
/// Server box?"). Reported directly as a usability gap, not a defect: the
/// underlying raw value is accurate, just unfriendly.
/// </summary>
public static class OperatingSystemDescriber
{
    public static string Describe()
    {
        var generic = RuntimeInformation.OSDescription;
        if (!OperatingSystem.IsWindows())
        {
            return generic;
        }

        var friendly = TryDescribeWindows();

        // The generic value stays visible in parentheses rather than being
        // replaced outright — it's the one value guaranteed to be accurate
        // even when the friendly-name registry lookup below fails or is
        // incomplete on some Windows configuration this wasn't tested
        // against.
        return friendly is null ? generic : $"{friendly} ({generic})";
    }

    [SupportedOSPlatform("windows")]
    private static string? TryDescribeWindows()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return BuildFriendlyName(
                installationType: key?.GetValue("InstallationType") as string,
                productName: key?.GetValue("ProductName") as string,
                displayVersion: key?.GetValue("DisplayVersion") as string,
                buildNumber: Environment.OSVersion.Version.Build);
        }
        catch (Exception)
        {
            // Never let a registry read failure (permissions, a missing
            // value on some Windows edition/version this wasn't tested
            // against, ...) take down registration over what is, after
            // all, just a cosmetic display improvement.
            return null;
        }
    }

    /// <summary>
    /// The actual decision logic, kept pure and public (rather than private
    /// inside <see cref="TryDescribeWindows"/>) specifically so it's
    /// testable from this cross-platform test suite, which has no Windows
    /// host to exercise the registry-reading half above on.
    ///
    /// <paramref name="buildNumber"/>, not <paramref name="productName"/>,
    /// is what decides Windows 10 vs. 11: the <c>ProductName</c> registry
    /// value is well known to still read "Windows 10 ..." on genuine
    /// Windows 11 installs — Microsoft never updated it after the Windows
    /// 11 rebrand — so trusting it directly would silently mislabel every
    /// real Windows 11 machine as Windows 10. Windows 11 starts at build
    /// 22000; this is the one signal actually reliable for telling them
    /// apart. Server installs aren't affected by that mislabeling bug
    /// (there was no server rebrand to bungle), so <paramref name="productName"/>
    /// is trusted as-is for them.
    /// </summary>
    public static string? BuildFriendlyName(string? installationType, string? productName, string? displayVersion, int buildNumber)
    {
        if (string.Equals(installationType, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return productName;
        }

        var edition = buildNumber >= 22000 ? "Windows 11" : "Windows 10";
        return string.IsNullOrEmpty(displayVersion) ? edition : $"{edition} {displayVersion}";
    }
}
