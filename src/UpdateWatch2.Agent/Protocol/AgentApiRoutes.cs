namespace UpdateWatch2.Agent.Protocol;

/// <summary>
/// Server routes the agent calls into. These endpoints don't exist on the
/// server yet — see updatewatch2-server issue #3 ("Implement agent-facing
/// protocol endpoints"). Keep this in sync with whatever routes that issue
/// lands.
/// </summary>
public static class AgentApiRoutes
{
    public const string Register = "/api/agent/register";
    public const string Alive = "/api/agent/alive";
    public const string ReportUpdates = "/api/agent/updates";
}
