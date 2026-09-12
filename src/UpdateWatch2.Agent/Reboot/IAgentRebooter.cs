namespace UpdateWatch2.Agent.Reboot;

/// <summary>
/// Reboots this agent's own machine on a remote trigger from the server —
/// a distinct action from <c>SelfUpdate.IAgentSelfUpdater</c> (which
/// replaces the agent binary with a newer version) and from
/// <c>UpdateCheck.IUpdateChecker.InstallAsync</c> (which installs OS
/// updates and, per CLAUDE.md's rule, never reboots the machine itself —
/// "the admin decides when to actually trigger a reboot", which this
/// interface is that decision being carried out). Reboots the whole
/// machine, not just this agent's own service process.
///
/// <para>
/// <see cref="RequestReboot"/> only ever SCHEDULES the platform's reboot
/// command and returns — it never waits for the machine to actually go
/// down, since that would mean this very process never returns at all.
/// <see cref="UpdateWatch2.Agent.HeartbeatWorker"/> relies on this: it
/// still needs to acknowledge the reboot to the server
/// (<c>POST .../reboot-ack</c>) in the window between scheduling the
/// reboot and the machine actually going down.
/// </para>
/// </summary>
public interface IAgentRebooter
{
    void RequestReboot();
}
