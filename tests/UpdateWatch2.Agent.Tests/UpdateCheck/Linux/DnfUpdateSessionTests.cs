using System.Runtime.Versioning;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// Covers <see cref="DnfUpdateSession.BuildInstallArgs"/> only — see
/// <see cref="AptUpdateSessionTests"/>'s own doc comment for why this is
/// tested in isolation from the actual dnf/yum-shelling-out half, for the
/// argument-injection reasoning an automated security review raised that
/// this now guards against, and for why this class carries
/// <c>[SupportedOSPlatform("linux")]</c> too.
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
}
