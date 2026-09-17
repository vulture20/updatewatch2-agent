using Microsoft.Extensions.Logging;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Tests.Configuration;

public class LogLevelStateTests
{
    [Theory]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("ERROR", LogLevel.Error)]
    public void Constructor_maps_the_initial_raw_level_case_insensitively(string raw, LogLevel expected)
    {
        var state = new LogLevelState(raw);

        Assert.Equal(expected, state.Current);
    }

    [Fact]
    public void Constructor_maps_an_unrecognized_value_to_Information()
    {
        var state = new LogLevelState("NOT-A-LEVEL");

        Assert.Equal(LogLevel.Information, state.Current);
    }

    [Fact]
    public void Update_changes_Current_immediately()
    {
        var state = new LogLevelState("INFO");

        state.Update("DEBUG");

        Assert.Equal(LogLevel.Debug, state.Current);
    }

    [Fact]
    public void Update_is_case_insensitive_like_the_constructor()
    {
        var state = new LogLevelState("INFO");

        state.Update("error");

        Assert.Equal(LogLevel.Error, state.Current);
    }
}
