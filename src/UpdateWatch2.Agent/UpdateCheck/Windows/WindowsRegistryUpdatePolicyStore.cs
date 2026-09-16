using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// The real, registry-touching half of <see cref="IWindowsUpdatePolicyStore"/> —
/// see <see cref="WindowsUpdatePolicyEnforcer"/> for why this policy is
/// enforced at all.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsRegistryUpdatePolicyStore : IWindowsUpdatePolicyStore
{
    private const string KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string ValueName = "NoAutoUpdate";

    public int? GetNoAutoUpdateValue()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as int?;
    }

    public void DisableNativeAutomaticUpdates()
    {
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }
}
