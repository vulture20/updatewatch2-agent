using System.Text.RegularExpressions;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Parses <c>dnf check-update</c>/<c>yum check-update</c> output.
/// Modeled on the documented dnf/yum output format
/// (<c>name.arch  version  repo</c> per pending package, preceded by a
/// "Last metadata expiration check" banner line) — <b>not</b> live-
/// verified against a real dnf/yum host, unlike <see cref="AptOutputParser"/>:
/// this project's dev sandbox is Debian-based and has neither tool
/// installed. Same honesty caveat this codebase already applies to
/// <c>WuaUpdateSession</c> — treat this as a well-researched first
/// implementation, and re-verify against a real Fedora/RHEL/openSUSE host
/// before relying on it in production.
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
