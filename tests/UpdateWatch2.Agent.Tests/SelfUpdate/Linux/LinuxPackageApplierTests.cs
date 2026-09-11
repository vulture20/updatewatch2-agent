using System.Runtime.Versioning;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.SelfUpdate.Linux;

namespace UpdateWatch2.Agent.Tests.SelfUpdate.Linux;

/// <summary>
/// Covers <see cref="LinuxPackageApplier.BuildInstallCommand"/> only — the
/// pure command-building half, pulled out so this could be unit tested;
/// the actual <c>systemd-run</c>-launching half remains untested here,
/// same as the rest of this class (see its own doc comment). Added while
/// checking, at the user's request, whether this class was similarly
/// affected by the argument-injection class found in
/// <c>Apt</c>/<c>DnfUpdateSession</c> — it wasn't (the downloaded file
/// path this always receives is structurally guaranteed to start with
/// <c>/</c>, never <c>-</c>), but the same <c>--</c> end-of-options
/// marker was added anyway for defense-in-depth/consistency, and these
/// tests are what actually proves it's present.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxPackageApplierTests
{
    [Fact]
    public void BuildInstallCommand_uses_dpkg_for_a_deb_asset()
    {
        var (command, args) = LinuxPackageApplier.BuildInstallCommand(AgentUpdateAssetKind.LinuxDeb, "/var/lib/updatewatch2/agent-update/updatewatch2-agent_0.15.3_amd64.deb");

        Assert.Equal("dpkg", command);
        Assert.Equal(["-i", "--", "/var/lib/updatewatch2/agent-update/updatewatch2-agent_0.15.3_amd64.deb"], args);
    }

    [Fact]
    public void BuildInstallCommand_uses_rpm_for_an_rpm_asset()
    {
        var (command, args) = LinuxPackageApplier.BuildInstallCommand(AgentUpdateAssetKind.LinuxRpm, "/var/lib/updatewatch2/agent-update/updatewatch2-agent-0.15.3-1.x86_64.rpm");

        Assert.Equal("rpm", command);
        Assert.Equal(["-U", "--", "/var/lib/updatewatch2/agent-update/updatewatch2-agent-0.15.3-1.x86_64.rpm"], args);
    }

    [Fact]
    public void BuildInstallCommand_inserts_an_end_of_options_marker_before_the_path()
    {
        var (_, args) = LinuxPackageApplier.BuildInstallCommand(AgentUpdateAssetKind.LinuxDeb, "/var/lib/updatewatch2/agent-update/x.deb");

        Assert.Contains("--", args);
        Assert.Equal("--", args[^2]);
    }
}
