using System.Runtime.Versioning;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.Configuration.Linux;

namespace UpdateWatch2.Agent.Tests.Configuration;

/// <summary>Exercises the Linux-only config store; run this class on a Linux CI leg.</summary>
[SupportedOSPlatform("linux")]
public class LinuxFileConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-test-{Guid.NewGuid()}.conf");

    [Fact]
    public void Load_returns_defaults_when_file_does_not_exist()
    {
        var store = new LinuxFileConfigStore(_path);

        var options = store.Load();

        Assert.Equal(new AgentOptions().ServerPort, options.ServerPort);
        Assert.Equal("", options.ServerAddress);
    }

    [Fact]
    public void Save_then_load_round_trips_all_fields()
    {
        var store = new LinuxFileConfigStore(_path);
        var original = new AgentOptions
        {
            ServerAddress = "updatewatch2.example.com",
            ServerPort = 9443,
            UpdateCheckIntervalMinutes = 60,
            UpdateCheckJitterSeconds = 45,
            AliveIntervalMinutes = 2,
            LogLevel = "DEBUG",
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.ServerAddress, loaded.ServerAddress);
        Assert.Equal(original.ServerPort, loaded.ServerPort);
        Assert.Equal(original.UpdateCheckIntervalMinutes, loaded.UpdateCheckIntervalMinutes);
        Assert.Equal(original.UpdateCheckJitterSeconds, loaded.UpdateCheckJitterSeconds);
        Assert.Equal(original.AliveIntervalMinutes, loaded.AliveIntervalMinutes);
        Assert.Equal(original.LogLevel, loaded.LogLevel);
    }

    public void Dispose() => File.Delete(_path);
}
