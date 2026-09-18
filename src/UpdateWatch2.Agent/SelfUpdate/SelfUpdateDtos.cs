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
/// This agent's own CPU architecture, resolved once in <c>Program.cs</c>
/// via <see cref="System.Runtime.InteropServices.RuntimeInformation.OSArchitecture"/>
/// (the OS's own native architecture, not
/// <see cref="System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture"/> —
/// what SHOULD be installed here, decoupled from what happens to be
/// currently running, e.g. under emulation) and combined with
/// <see cref="AgentUpdateAssetKind"/> by <see cref="AgentSelfUpdateService"/>
/// to pick exactly one of a <see cref="Communication.AgentUpdateOffer"/>'s
/// six asset slots (updatewatch2-agent#22/#23). Deliberately a separate
/// enum from <see cref="AgentUpdateAssetKind"/>, not folded into it —
/// <see cref="AgentUpdateAssetKind"/> alone already fully determines which
/// package FORMAT/command applies (dpkg vs. rpm vs. the Windows
/// installer), which is all <c>Linux.LinuxPackageApplier</c> and
/// <c>Program.cs</c>'s <c>IPlatformUpdateApplier</c>/<c>ILinuxUpdateSession</c>
/// selection ever needed to know — expanding that enum to six values
/// instead would have forced every one of those call sites to learn about
/// architecture too, for no reason.
/// </summary>
public enum AgentUpdateAssetArch
{
    X64,
    Arm64,
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
