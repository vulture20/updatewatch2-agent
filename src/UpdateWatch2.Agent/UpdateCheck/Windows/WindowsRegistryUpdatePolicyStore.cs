using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// The real, registry-touching half of <see cref="IWindowsUpdatePolicyStore"/> —
/// see <see cref="WindowsUpdatePolicyEnforcer"/> for why this policy is
/// enforced at all.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsRegistryUpdatePolicyStore(ILogger<WindowsRegistryUpdatePolicyStore> logger) : IWindowsUpdatePolicyStore
{
    private const string KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string ValueName = "NoAutoUpdate";

    public int? GetNoAutoUpdateValue()
    {
        logger.LogDebug(@"Registry: reading HKLM\{KeyPath}\{ValueName}", KeyPath, ValueName);
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        var value = key?.GetValue(ValueName) as int?;
        logger.LogDebug(@"Registry: HKLM\{KeyPath}\{ValueName} = {Value}", KeyPath, ValueName, value?.ToString() ?? "<not set>");
        return value;
    }

    public void DisableNativeAutomaticUpdates()
    {
        logger.LogDebug(@"Registry: writing HKLM\{KeyPath}\{ValueName} = 1 (DWORD)", KeyPath, ValueName);
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }
}
