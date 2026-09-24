using System.Text.RegularExpressions;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Parses <c>dnf check-update</c>/<c>yum check-update</c> output
/// (<c>name.arch  version  repo</c> per pending package). Live-verified
/// against real <c>dnf -q check-update</c> output from a real Fedora 44
/// (dnf5) host (agent v1.0.28, see <see cref="DnfUpdateSession"/>'s own
/// doc comment) — correctly parsed all 26 of that host's genuinely
/// pending packages, including dnf5's own "Upgrades" section header line
/// preceding the list (not present in classic dnf/yum output), which this
/// parser silently and correctly skips rather than mis-parsing, and a
/// package version carrying an epoch prefix (e.g. <c>1:3.5.8-1.fc44</c>),
/// which stays intact as a single opaque version token. A genuinely older
/// classic-dnf/yum host's exact output has still not been captured for
/// real — treat that specific shape as well-researched but not
/// live-verified, the same caveat <see cref="DnfUpdateSession"/>'s own
/// <c>yum</c> fallback branch now carries.
/// </summary>
public static partial class DnfOutputParser
{
    public readonly record struct AvailablePackage(string Package, string Architecture, string Version, string Repository);

    [GeneratedRegex(@"^(?<name>\S+)\.(?<arch>\S+)\s+(?<version>\S+)\s+(?<repo>\S+)$")]
    private static partial Regex PackageLinePattern();

    public static IReadOnlyList<AvailablePackage> ParseCheckUpdate(string stdout)
    {
        var results = new List<AvailablePackage>();
        foreach (var rawLine in stdout.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var match = PackageLinePattern().Match(trimmed);
            if (match.Success)
            {
                results.Add(new AvailablePackage(
                    match.Groups["name"].Value,
                    match.Groups["arch"].Value,
                    match.Groups["version"].Value,
                    match.Groups["repo"].Value));
            }
        }

        return results;
    }
}
