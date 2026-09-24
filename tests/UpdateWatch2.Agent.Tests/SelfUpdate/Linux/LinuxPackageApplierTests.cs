using System.Runtime.Versioning;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.SelfUpdate.Linux;

namespace UpdateWatch2.Agent.Tests.SelfUpdate.Linux;

/// <summary>
/// Covers <see cref="LinuxPackageApplier.BuildInstallCommand"/> and
/// <see cref="LinuxPackageApplier.BuildSystemdRunArgs"/> — the pure
/// argument-building halves, pulled out so this could be unit tested; the
/// actual process-launching/exit-code-observing half of
/// <see cref="LinuxPackageApplier.ApplyAsync"/> remains untested here, same
/// as the rest of this class (see its own doc comment — that half needs a
/// real systemd host, live-verified instead in an isolated QEMU VM).
/// <see cref="LinuxPackageApplier.BuildInstallCommand"/>'s own tests were
/// added while checking, at the user's request, whether this class was
/// similarly affected by the argument-injection class found in
/// <c>Apt</c>/<c>DnfUpdateSession</c> — it wasn't (the downloaded file
/// path this always receives is structurally guaranteed to start with
/// <c>/</c>, never <c>-</c>), but the same <c>--</c> end-of-options
/// marker was added anyway for defense-in-depth/consistency, and these
/// tests are what actually proves it's present.
/// <see cref="LinuxPackageApplier.BuildSystemdRunArgs"/>'s own tests were
/// added for `updatewatch2-agent#24`'s fix (agent v1.0.33).
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

    /// <summary>
    /// Covers <see cref="LinuxPackageApplier.BuildSystemdRunArgs"/> —
    /// added for `updatewatch2-agent#24`'s fix (agent v1.0.33): no
    /// <c>--scope</c> (incompatible with <c>--wait</c>, confirmed from a
    /// real <c>man systemd-run</c>, not assumed — see the class's own doc
    /// comment), and <c>--wait</c> present, so this method's own exit code
    /// genuinely reflects the wrapped install. Deliberately asserts
    /// <c>--pipe</c> is *not* present — a real, live-verified QEMU-VM
    /// regression found combining it with <c>--wait</c> here reintroduces
    /// a SIGPIPE variant of the original "donthack" deadlock (see the
    /// class's own doc comment); this assertion is what would catch a
    /// future regression re-adding it.
    /// </summary>
    [Fact]
    public void BuildSystemdRunArgs_never_uses_scope_or_pipe_but_always_waits()
    {
        var args = LinuxPackageApplier.BuildSystemdRunArgs(
            AgentUpdateAssetKind.LinuxDeb, "/var/lib/updatewatch2/agent-update/x.deb", "updatewatch2-agent-selfupdate-abc123");

        Assert.DoesNotContain("--scope", args);
        Assert.DoesNotContain("--pipe", args);
        Assert.Contains("--wait", args);
        Assert.Contains("--collect", args);
        Assert.Contains("--unit=updatewatch2-agent-selfupdate-abc123", args);
    }

    [Fact]
    public void BuildSystemdRunArgs_places_the_command_and_its_args_after_the_end_of_options_marker()
    {
        var args = LinuxPackageApplier.BuildSystemdRunArgs(
            AgentUpdateAssetKind.LinuxRpm, "/var/lib/updatewatch2/agent-update/x.rpm", "updatewatch2-agent-selfupdate-abc123");

        var separatorIndex = Array.IndexOf(args, "--");
        Assert.True(separatorIndex >= 0, "Expected a \"--\" end-of-options marker in the built systemd-run arguments.");
        Assert.Equal(["rpm", "-U", "--", "/var/lib/updatewatch2/agent-update/x.rpm"], args[(separatorIndex + 1)..]);
    }
}
