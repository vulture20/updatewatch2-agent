namespace UpdateWatch2.Agent;

/// <summary>
/// SemVer version of the agent itself, independent of the server, protocol,
/// and DB schema versions (see the server repo's CLAUDE.md). Keep in sync
/// with the repository root VERSION file; bump both together.
/// </summary>
public static class AgentVersion
{
    public const string Current = "0.1.1";
}
