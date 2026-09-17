namespace UpdateWatch2.Agent.Configuration;

/// <summary>
/// The agent's currently-effective minimum log level, shared between
/// <c>Program.cs</c> (two <c>AddFilter</c> registrations that read
/// <see cref="Current"/> on every single logging call — see that file's own
/// comments for why this replaced the old startup-only <c>SetMinimumLevel</c>/
/// config-write approach) and <see cref="HeartbeatWorker"/> (which calls
/// <see cref="Update"/> when the server pushes a <c>DesiredLogLevel</c>
/// override, at the user's explicit request that a pushed LogLevel change
/// take effect immediately, without an agent restart). Same minimal
/// volatile-field shape as <c>UpdateCheck.IPreDownloadPolicyState</c> — a
/// single writer, many readers (every logging call), no ordering to
/// coordinate beyond "the next log call sees the latest value".
/// </summary>
public interface ILogLevelState
{
    LogLevel Current { get; }

    /// <summary>Accepts the same DEBUG/INFO/WARNING/ERROR strings as <see cref="AgentOptions.LogLevel"/> — case-insensitive, an unrecognized value maps to Information.</summary>
    void Update(string rawLevel);
}
