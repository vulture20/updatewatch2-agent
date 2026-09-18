using System.Runtime.Versioning;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// Covers <see cref="AptUpdateSession.BuildInstallArgs"/> and
/// <see cref="AptUpdateSession.BuildDownloadOnlyArgs"/> — the pure
/// argument-building halves of <see cref="AptUpdateSession"/>, pulled out
/// specifically so they could be unit tested; the actual
/// <c>apt-get</c>-shelling-out half remains untested here, same as the
/// rest of this class (see its own doc comment). <c>BuildInstallArgs</c>'s
/// tests were added after an automated security review flagged the
/// original inline version as an argument-injection risk (a package name
/// starting with <c>-</c> could be parsed as a flag rather than a
/// positional argument without a <c>--</c> end-of-options marker) — these
/// tests are what actually proves that marker is present.
/// <c>BuildDownloadOnlyArgs</c> takes no caller-supplied input at all (it
/// always downloads everything currently pending, mirroring
/// <c>Windows.IWindowsUpdateSession.DownloadOnly</c>'s own semantics), so
/// the same injection concern doesn't apply to it. Marked
/// <c>[SupportedOSPlatform("linux")]</c> like
/// <c>LinuxFileConfigStoreTests</c>/<c>LinuxClientCertificateStoreTests</c>
/// — the methods under test are pure and platform-agnostic in practice, but
/// their containing class carries the same attribute, and this project's CI
/// already runs entirely on Linux (ubuntu-latest) anyway.
/// </summary>
[SupportedOSPlatform("linux")]
public class AptUpdateSessionTests
{
    [Fact]
    public void BuildInstallArgs_upgrades_everything_when_no_selection_is_given()
    {
        var args = AptUpdateSession.BuildInstallArgs(packageNames: null);

        Assert.Equal(["-y", "-o", "Dpkg::Options::=--force-confold", "dist-upgrade"], args);
    }

    [Fact]
    public void BuildInstallArgs_scopes_to_only_the_selected_packages()
    {
        var args = AptUpdateSession.BuildInstallArgs(["nginx", "openssl"]);

        Assert.Equal(["-y", "-o", "Dpkg::Options::=--force-confold", "install", "--only-upgrade", "--", "nginx", "openssl"], args);
    }

    [Fact]
    public void BuildInstallArgs_inserts_an_end_of_options_marker_before_the_package_names()
    {
        // The actual fix: without "--", a package name starting with "-"
        // would be parsed by apt-get as an additional flag rather than a
        // positional argument (option-smuggling argument injection).
        // Everything after "--" is always treated as positional,
        // regardless of what it starts with.
        var args = AptUpdateSession.BuildInstallArgs(["--allow-downgrades", "nginx"]);

        var separatorIndex = Array.IndexOf(args, "--");
        Assert.True(separatorIndex >= 0, "Expected a \"--\" end-of-options marker in the built arguments.");
        Assert.Equal(["--allow-downgrades", "nginx"], args[(separatorIndex + 1)..]);
    }

    [Fact]
    public void BuildInstallArgs_adds_allow_unauthenticated_when_enabled()
    {
        var args = AptUpdateSession.BuildInstallArgs(packageNames: null, allowUnauthenticated: true);

        Assert.Equal(["-y", "--allow-unauthenticated", "-o", "Dpkg::Options::=--force-confold", "dist-upgrade"], args);
    }

    [Fact]
    public void BuildInstallArgs_places_allow_unauthenticated_before_the_end_of_options_marker()
    {
        var args = AptUpdateSession.BuildInstallArgs(["nginx"], allowUnauthenticated: true);

        Assert.Equal(
            ["-y", "--allow-unauthenticated", "-o", "Dpkg::Options::=--force-confold", "install", "--only-upgrade", "--", "nginx"],
            args);
    }

    [Fact]
    public void BuildInstallArgs_omits_allow_unauthenticated_by_default()
    {
        var args = AptUpdateSession.BuildInstallArgs(packageNames: null);

        Assert.DoesNotContain("--allow-unauthenticated", args);
    }

    [Fact]
    public void BuildDownloadOnlyArgs_downloads_everything_pending_without_installing()
    {
        var args = AptUpdateSession.BuildDownloadOnlyArgs();

        Assert.Equal(["-y", "--download-only", "dist-upgrade"], args);
    }

    [Fact]
    public void BuildDownloadOnlyArgs_adds_allow_unauthenticated_when_enabled()
    {
        var args = AptUpdateSession.BuildDownloadOnlyArgs(allowUnauthenticated: true);

        Assert.Equal(["-y", "--allow-unauthenticated", "--download-only", "dist-upgrade"], args);
    }
}
