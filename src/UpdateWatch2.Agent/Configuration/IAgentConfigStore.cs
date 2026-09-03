namespace UpdateWatch2.Agent.Configuration;

/// <summary>
/// Reads/writes <see cref="AgentOptions"/> from local storage — the
/// Windows registry (functional today) or, on Linux, a config file
/// (planned — see CLAUDE.md section 4.1). <see cref="Save"/> exists mainly
/// for tests and for the Linux path; on Windows, values are normally
/// written once by the NSIS installer rather than by the running service.
/// </summary>
public interface IAgentConfigStore
{
    AgentOptions Load();

    void Save(AgentOptions options);
}
