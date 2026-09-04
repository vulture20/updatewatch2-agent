namespace UpdateWatch2.Agent.Protocol;

/// <summary>
/// SemVer version of the agent-server transfer protocol. Must be kept in
/// sync with <c>UpdateWatch2.Server.Protocol.ProtocolVersion</c> whenever
/// the request/response shapes in <see cref="AgentApiRoutes"/> change, so
/// mismatched agent/server builds can detect incompatibility.
/// </summary>
public static class ProtocolVersion
{
    public const string Current = "0.3.0";
}
