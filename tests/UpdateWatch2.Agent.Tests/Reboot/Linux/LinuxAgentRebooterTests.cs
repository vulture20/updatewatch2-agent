using System.Runtime.Versioning;
using UpdateWatch2.Agent.Reboot.Linux;

namespace UpdateWatch2.Agent.Tests.Reboot.Linux;

/// <summary>
/// Covers <see cref="LinuxAgentRebooter.BuildRebootArgs"/> only — the
/// pure, testable half of <see cref="LinuxAgentRebooter"/>; the actual
/// launching-systemd-run half remains untested here, mirroring
/// <c>AptUpdateSessionTests</c>' precedent for its own class's
/// <c>BuildInstallArgs</c>.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxAgentRebooterTests
{
    [Fact]
    public void BuildRebootArgs_schedules_a_systemctl_reboot_after_the_given_delay()
    {
        var args = LinuxAgentRebooter.BuildRebootArgs(10, "updatewatch2-agent-reboot-abc123");

        Assert.Equal(
            ["--collect", "--unit=updatewatch2-agent-reboot-abc123", "--on-active=10", "--", "systemctl", "reboot"],
            args);
    }
}
