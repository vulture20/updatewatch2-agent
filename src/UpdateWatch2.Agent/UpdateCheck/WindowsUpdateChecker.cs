using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Placeholder — always reports no updates found. Real detection needs to
/// integrate the Windows Update Agent API (WUApiLib COM interop) to
/// search, without installing, and to read the reboot-required state.
/// A future Linux checker (see CLAUDE.md section 4.1) will implement the
/// same interface against the distro package manager.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsUpdateChecker(ILogger<WindowsUpdateChecker> logger) : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        logger.LogWarning("WindowsUpdateChecker is a placeholder and does not perform a real update search yet.");
        return Task.FromResult(new UpdateCheckResult(Updates: [], RebootRequired: false));
    }
}
