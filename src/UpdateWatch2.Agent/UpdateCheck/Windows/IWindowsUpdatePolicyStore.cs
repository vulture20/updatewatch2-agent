namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// Thin seam over the one registry value <see cref="WindowsUpdatePolicyEnforcer"/>
/// cares about, so that orchestration logic (decide whether a write is even
/// needed, log accordingly) can be unit-tested with a hand-written fake —
/// the same split <see cref="IWindowsUpdateSession"/> already established
/// for the COM-touching search/install path, applied here to the
/// registry-touching policy path instead.
/// </summary>
public interface IWindowsUpdatePolicyStore
{
    /// <summary>
    /// The current value of <c>HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoUpdate</c>,
    /// or null if the key/value doesn't exist yet (Windows' own default —
    /// native Automatic Updates enabled).
    /// </summary>
    int? GetNoAutoUpdateValue();

    /// <summary>
    /// Sets that same value to 1 — the standard, long-documented Group
    /// Policy equivalent of "Configure Automatic Updates: Disabled" —
    /// fully disabling Windows' own native Automatic Updates client (its
    /// own independent check/download/install/reboot cycle), without
    /// affecting the separate Windows Update Agent (WUApiLib) COM API this
    /// agent's own <see cref="WuaUpdateSession"/> uses directly.
    /// </summary>
    void DisableNativeAutomaticUpdates();
}
