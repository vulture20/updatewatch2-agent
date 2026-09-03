namespace UpdateWatch2.Agent.Configuration;

/// <summary>
/// Local agent configuration — server address/port and the update-check /
/// alive-heartbeat cadence. Stored in the Windows registry (set by the NSIS
/// installer) or, on Linux, an equivalent config file; see
/// <see cref="IAgentConfigStore"/>. The agent's identity (hostname) is not
/// part of this — it's read from the OS at runtime, per CLAUDE.md
/// ("Agents are identified by hostname").
/// </summary>
public class AgentOptions
{
    public string ServerAddress { get; set; } = "";

    public int ServerPort { get; set; } = 8443;

    /// <summary>Base interval between update checks.</summary>
    public int UpdateCheckIntervalMinutes { get; set; } = 240;

    /// <summary>
    /// Random jitter (0..N seconds) added to <see cref="UpdateCheckIntervalMinutes"/>
    /// so many agents don't hit the server at the same moment.
    /// </summary>
    public int UpdateCheckJitterSeconds { get; set; } = 300;

    public int AliveIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// DEBUG/INFO/WARNING/ERROR — same values as the server's
    /// UPDATEWATCH2_LOGLEVEL. Can be set locally or pushed centrally from
    /// the server UI (not implemented yet).
    /// </summary>
    public string LogLevel { get; set; } = "INFO";
}
