using System.Runtime.Versioning;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// Covers <see cref="DnfUpdateSession.BuildInstallArgs"/> and
/// <see cref="DnfUpdateSession.BuildDownloadOnlyArgs"/> — see
/// <see cref="AptUpdateSessionTests"/>'s own doc comment for why these are
/// tested in isolation from the actual dnf/yum-shelling-out half, for the
/// argument-injection reasoning an automated security review raised that
/// <c>BuildInstallArgs</c> now guards against (not applicable to
/// <c>BuildDownloadOnlyArgs</c>, which takes no caller-supplied input),
/// and for why this class carries <c>[SupportedOSPlatform("linux")]</c>
/// too.
/// </summary>
[SupportedOSPlatform("linux")]
public class DnfUpdateSessionTests
{
    [Fact]
    public void BuildInstallArgs_updates_everything_when_no_selection_is_given()
    {
        var args = DnfUpdateSession.BuildInstallArgs(packageNames: null);

        Assert.Equal(["-y", "update"], args);
    }

    [Fact]
    public void BuildInstallArgs_scopes_to_only_the_selected_packages()
    {
        var args = DnfUpdateSession.BuildInstallArgs(["httpd", "openssl"]);

        Assert.Equal(["-y", "update", "--", "httpd", "openssl"], args);
    }

    [Fact]
    public void BuildInstallArgs_inserts_an_end_of_options_marker_before_the_package_names()
    {
        var args = DnfUpdateSession.BuildInstallArgs(["--allowerasing", "httpd"]);

        var separatorIndex = Array.IndexOf(args, "--");
        Assert.True(separatorIndex >= 0, "Expected a \"--\" end-of-options marker in the built arguments.");
        Assert.Equal(["--allowerasing", "httpd"], args[(separatorIndex + 1)..]);
    }

    [Fact]
    public void BuildInstallArgs_adds_nogpgcheck_when_allow_unauthenticated_is_enabled()
    {
        var args = DnfUpdateSession.BuildInstallArgs(packageNames: null, allowUnauthenticated: true);

        Assert.Equal(["-y", "--nogpgcheck", "update"], args);
    }

    [Fact]
    public void BuildInstallArgs_omits_nogpgcheck_by_default()
    {
        var args = DnfUpdateSession.BuildInstallArgs(packageNames: null);

        Assert.DoesNotContain("--nogpgcheck", args);
    }

    [Fact]
    public void BuildInstallArgs_places_nogpgcheck_before_the_end_of_options_marker()
    {
        var args = DnfUpdateSession.BuildInstallArgs(["httpd"], allowUnauthenticated: true);

        Assert.Equal(["-y", "--nogpgcheck", "update", "--", "httpd"], args);
    }

    [Fact]
    public void BuildDownloadOnlyArgs_downloads_everything_pending_without_installing()
    {
        var args = DnfUpdateSession.BuildDownloadOnlyArgs();

        Assert.Equal(["-y", "update", "--downloadonly"], args);
    }

    [Fact]
    public void BuildDownloadOnlyArgs_adds_nogpgcheck_when_enabled()
    {
        var args = DnfUpdateSession.BuildDownloadOnlyArgs(allowUnauthenticated: true);

        Assert.Equal(["-y", "--nogpgcheck", "update", "--downloadonly"], args);
    }
}
