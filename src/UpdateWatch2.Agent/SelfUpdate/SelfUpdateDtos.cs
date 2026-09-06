namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// Which of an <see cref="Communication.AgentUpdateOffer"/>'s three asset
/// slots is relevant on this platform — chosen once in <c>Program.cs</c>
/// (Windows is always <see cref="WindowsInstaller"/>; Linux is
/// <see cref="LinuxDeb"/> or <see cref="LinuxRpm"/> depending on
/// <c>UpdateCheck.Linux.LinuxPackageManagerDetector</c>, mirroring how that
/// same detector already picks between <c>AptUpdateSession</c> and
/// <c>DnfUpdateSession</c> for OS-update checking).
/// </summary>
public enum AgentUpdateAssetKind
{
    WindowsInstaller,
    LinuxDeb,
    LinuxRpm,
}

/// <summary>
/// Outcome of <see cref="IAgentSelfUpdater.ApplyAsync"/>.
/// <see cref="NotApplicable"/> deliberately covers every "there was
/// nothing to do" case uniformly (no offer at all, the offer isn't
/// actually newer than this agent's own version, or this release has no
/// asset for this platform) — <see cref="UpdateWatch2.Agent.HeartbeatWorker"/>
/// treats all of them the same way.
/// </summary>
public enum SelfUpdateOutcome
{
    NotApplicable,
    Applied,
    DownloadFailed,
    IntegrityCheckFailed,
    ApplyFailed,
}
