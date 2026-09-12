using System.Runtime.Versioning;
using UpdateWatch2.Agent.Reboot.Windows;

namespace UpdateWatch2.Agent.Tests.Reboot.Windows;

/// <summary>
/// Covers <see cref="WindowsAgentRebooter.BuildRebootArgs"/> only — the
/// pure, testable half of <see cref="WindowsAgentRebooter"/>; the actual
/// launching-shutdown.exe half remains untested here, same as every other
/// Windows-only class in this codebase. Marked
/// <c>[SupportedOSPlatform("windows")]</c>, mirroring
/// <c>AptUpdateSessionTests</c>' own precedent for its Linux counterpart —
/// the method under test is pure argument-building with nothing actually
/// Windows-specific in it, so it runs fine on this project's Linux
/// (ubuntu-latest) CI regardless of the attribute; the attribute exists
/// purely to satisfy CA1416 at the call site, matching the containing
/// production class's own attribute.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAgentRebooterTests
{
    [Fact]
    public void BuildRebootArgs_schedules_a_reboot_with_the_given_delay_and_message()
    {
        var args = WindowsAgentRebooter.BuildRebootArgs(60, "UpdateWatch2: reboot requested by an administrator.");

        Assert.Equal(["/r", "/t", "60", "/c", "UpdateWatch2: reboot requested by an administrator."], args);
    }
}
