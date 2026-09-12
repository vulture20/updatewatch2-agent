namespace UpdateWatch2.Agent.Restart;

/// <summary>
/// Restarts this agent's own service process on a remote trigger from the
/// server — a distinct action from <c>SelfUpdate.IAgentSelfUpdater</c>
/// (which replaces the binary with a newer version) and from
/// <c>UpdateCheck.IUpdateChecker.InstallAsync</c> (which installs OS
/// updates and, per CLAUDE.md's rule, never reboots the machine itself).
/// A restart here means only "stop and start this agent's own service" —
/// never a machine reboot, never an OS-update install.
///
/// <para>
/// <see cref="RequestRestart"/> must return almost immediately — it only
/// LAUNCHES the platform-specific stop-then-start mechanism and never
/// waits for it to complete, since completion means this very process
/// gets torn down. <see cref="UpdateWatch2.Agent.HeartbeatWorker"/> relies
/// on this: it still needs to acknowledge the restart to the server
/// (<c>POST .../restart-ack</c>) in the brief window after this call
/// returns and before the process actually goes down.
/// </para>
/// </summary>
public interface IAgentRestarter
{
    void RequestRestart();
}
