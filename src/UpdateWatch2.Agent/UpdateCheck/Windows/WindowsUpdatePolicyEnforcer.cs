namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// Upholds CLAUDE.md's "update installation never triggers a reboot
/// itself" rule at the OS level, not just in this agent's own code —
/// reported by a user directly ("Nach den Windows-Updates wird teilweise
/// ein automatischer und unerwünschter Neustart durchgeführt"). This
/// agent's own <see cref="WuaUpdateSession.DownloadAndInstall"/> never
/// initiates a reboot (confirmed by that class's own comment), and neither
/// server nor agent code ever sets a pending reboot request except through
/// an explicit admin action (<c>AgentsController.Reboot</c>) — but Windows'
/// own separate, native "Automatic Updates" client (distinct from the
/// Windows Update Agent/WUApiLib COM API this agent uses directly to
/// search/download/install) runs independently by default and, per its own
/// default consumer settings, can download, install, AND reboot updates
/// entirely on its own schedule, regardless of what this agent does — the
/// standard, long-documented way IT administrators have always prevented
/// that (the same mechanism a WSUS- or Intune-managed fleet relies on) is
/// the "Configure Automatic Updates: Disabled" Group Policy, which is just
/// a registry DWORD (see <see cref="IWindowsUpdatePolicyStore"/>). Setting
/// it does not affect the separate WUApiLib COM API this agent's own
/// <see cref="WuaUpdateSession"/> calls directly — that keeps working
/// exactly as before.
///
/// <para>
/// Deliberately NOT itself marked <c>[SupportedOSPlatform("windows")]</c> —
/// same split as <see cref="WindowsUpdateChecker"/>/<see cref="IWindowsUpdateSession"/>:
/// only <see cref="WindowsRegistryUpdatePolicyStore"/> touches the registry
/// directly, so this orchestration logic (decide whether a write is even
/// needed, log accordingly) gets real coverage under this project's Linux
/// CI against a hand-written fake store.
/// </para>
///
/// <para>
/// If a domain Group Policy also manages this same key, that policy wins
/// on its next refresh regardless of what this class writes locally — this
/// is a no-op safety net in that case, not a conflict, since GPO always
/// takes precedence over a locally-set value in the same key. Run once at
/// startup (see Program.cs) rather than on every heartbeat tick: unlike
/// this codebase's other self-healing checks, nothing here needs to react
/// to a mid-lifetime change faster than the next service restart would
/// already catch it on its own.
/// </para>
/// </summary>
public class WindowsUpdatePolicyEnforcer(IWindowsUpdatePolicyStore store, ILogger<WindowsUpdatePolicyEnforcer> logger)
{
    public void EnsureNativeAutomaticUpdatesDisabled()
    {
        try
        {
            if (store.GetNoAutoUpdateValue() == 1)
            {
                return;
            }

            store.DisableNativeAutomaticUpdates();
            logger.LogWarning(
                "Windows' own native Automatic Updates client was enabled (or not yet configured) on this machine — " +
                "disabled it (HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU\\NoAutoUpdate=1) so only " +
                "this agent's own controlled install/reboot flow manages updates. If a domain Group Policy manages " +
                "this same key, that policy takes precedence on its next refresh regardless of this change.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify/disable Windows' native Automatic Updates policy.");
        }
    }
}
