using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Tests.Configuration;

public class AgentOptionsTests
{
    [Fact]
    public void ResolveHostname_returns_the_OS_hostname_when_no_override_is_set()
    {
        var options = new AgentOptions();

        Assert.Equal(Environment.MachineName, options.ResolveHostname());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveHostname_treats_a_null_empty_or_whitespace_override_as_unset(string? blank)
    {
        var options = new AgentOptions { HostnameOverride = blank };

        Assert.Equal(Environment.MachineName, options.ResolveHostname());
    }

    [Fact]
    public void ResolveHostname_returns_the_override_when_set()
    {
        var options = new AgentOptions { HostnameOverride = "renamed-host" };

        Assert.Equal("renamed-host", options.ResolveHostname());
    }
}
