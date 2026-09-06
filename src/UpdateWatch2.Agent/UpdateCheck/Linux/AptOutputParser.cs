using System.Text.RegularExpressions;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Parses <c>apt list --upgradable</c>'s output. Live-verified against
/// this project's own dev sandbox (Debian 13 trixie), not just apt's man
/// page: the pattern below is written against real output captured with
/// <c>LC_ALL=C apt list --upgradable</c> against the sandbox's own,
/// unmodified package cache — see <see cref="ShellCommand"/>'s doc
/// comment for why forcing that locale matters. <c>AptOutputParserTests</c>
/// pins a couple of those captured lines verbatim as regression fixtures.
/// </summary>
public static partial class AptOutputParser
{
    public readonly record struct UpgradablePackage(string Package, string NewVersion, string OldVersion);

    // Real captured example this is matched against:
    // "docker-ce-cli/trixie 5:29.8.0-1~debian.13~trixie amd64 [upgradable from: 5:29.7.2-1~debian.13~trixie]"
    // Deliberately ignores the suite after the "/" (stable-security,
    // trixie, ...) and the architecture column — neither is part of
    // DetectedUpdate's shape, and the suite in particular varies by
    // distro release name in a way not worth modeling here.
    [GeneratedRegex(@"^(?<pkg>\S+)/\S+\s+(?<newVersion>\S+)\s+\S+\s+\[upgradable from:\s*(?<oldVersion>\S+)\]")]
    private static partial Regex UpgradableLinePattern();

    public static IReadOnlyList<UpgradablePackage> ParseUpgradable(string stdout)
    {
        var results = new List<UpgradablePackage>();
        foreach (var rawLine in stdout.Split('\n'))
        {
            var match = UpgradableLinePattern().Match(rawLine.Trim());
            if (match.Success)
            {
                results.Add(new UpgradablePackage(
                    match.Groups["pkg"].Value,
                    match.Groups["newVersion"].Value,
                    match.Groups["oldVersion"].Value));
            }
        }

        return results;
    }
}
