using System.Runtime.Versioning;
using UpdateWatch2.Agent.Reboot.Linux;

namespace UpdateWatch2.Agent.Tests.Reboot.Linux;

/// <summary>
/// Covers <see cref="LinuxAgentRebooter.BuildRebootArgs"/> only — the
/// pure, testable half of <see cref="LinuxAgentRebooter"/>; the actual
/// launching-shutdown half remains untested here, mirroring
/// <c>AptUpdateSessionTests</c>' precedent for its own class's
/// <c>BuildInstallArgs</c>.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxAgentRebooterTests
{
    [Fact]
    public void BuildRebootArgs_schedules_a_reboot_with_the_given_delay_and_message()
    {
        var args = LinuxAgentRebooter.BuildRebootArgs("+1", "UpdateWatch2: reboot requested by an administrator.");

        Assert.Equal(["-r", "+1", "UpdateWatch2: reboot requested by an administrator."], args);
    }
}
