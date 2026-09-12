using System.Runtime.Versioning;
using UpdateWatch2.Agent.Restart.Windows;

namespace UpdateWatch2.Agent.Tests.Restart.Windows;

/// <summary>
/// Covers <see cref="WindowsAgentRestarter.BuildRestartScript"/> only —
/// the pure, testable half of <see cref="WindowsAgentRestarter"/>; the
/// actual launching-a-detached-cmd-process half remains untested here,
/// same as every other Windows-only class in this codebase. Marked
/// <c>[SupportedOSPlatform("windows")]</c>, mirroring
/// <c>AptUpdateSessionTests</c>' own precedent for its Linux counterpart
/// — the method under test is pure string-building with nothing actually
/// Windows-specific in it, so it runs fine on this project's Linux
/// (ubuntu-latest) CI regardless of the attribute; the attribute exists
/// purely to satisfy CA1416 at the call site, matching the containing
/// production class's own attribute.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAgentRestarterTests
{
    [Fact]
    public void BuildRestartScript_stops_then_starts_the_named_service()
    {
        var script = WindowsAgentRestarter.BuildRestartScript("UpdateWatch2 Agent");

        Assert.Contains("sc stop \"UpdateWatch2 Agent\"", script);
        Assert.Contains("sc start \"UpdateWatch2 Agent\"", script);
        Assert.True(script.IndexOf("sc stop", StringComparison.Ordinal) < script.IndexOf("sc start", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRestartScript_deletes_itself_afterward()
    {
        var script = WindowsAgentRestarter.BuildRestartScript("UpdateWatch2 Agent");

        Assert.Contains("del \"%~f0\"", script);
    }
}
