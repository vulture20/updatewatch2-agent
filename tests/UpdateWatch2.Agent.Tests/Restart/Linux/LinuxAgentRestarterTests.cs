using System.Runtime.Versioning;
using UpdateWatch2.Agent.Restart.Linux;

namespace UpdateWatch2.Agent.Tests.Restart.Linux;

/// <summary>
/// Covers <see cref="LinuxAgentRestarter.BuildRestartArgs"/> only — the
/// pure, testable half of <see cref="LinuxAgentRestarter"/>; the actual
/// launching-systemctl half remains untested here, mirroring
/// <c>AptUpdateSessionTests</c>' precedent for its own class's
/// <c>BuildInstallArgs</c>.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxAgentRestarterTests
{
    [Fact]
    public void BuildRestartArgs_restarts_the_named_unit_without_blocking()
    {
        var args = LinuxAgentRestarter.BuildRestartArgs("updatewatch2-agent");

        Assert.Equal(["restart", "--no-block", "--", "updatewatch2-agent"], args);
    }
}
