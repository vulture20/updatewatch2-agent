using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UpdateWatch2.Agent.Configuration.Windows;

/// <summary>
/// Reads/writes agent configuration under
/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\UpdateWatch2\Agent</c>. Values are
/// normally written once by the NSIS installer (see
/// installer/nsis/setup.nsi.template); <see cref="Save"/> is provided for
/// completeness and for values the running service updates itself (e.g. a
/// log level pushed from the server — not implemented yet).
///
/// Uninstall must remove this entire key without leaving residue, per
/// CLAUDE.md's "sauberes Onboarding/Offboarding" principle — that removal
/// belongs in the NSIS uninstaller, not here.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsRegistryConfigStore : IAgentConfigStore
{
    private const string KeyPath = @"SOFTWARE\UpdateWatch2\Agent";

    public AgentOptions Load()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        if (key is null)
        {
            return new AgentOptions();
        }

        return new AgentOptions
        {
            ServerAddress = (string?)key.GetValue(nameof(AgentOptions.ServerAddress)) ?? "",
            ServerPort = ReadInt(key, nameof(AgentOptions.ServerPort), 8443),
            UpdateCheckIntervalMinutes = ReadInt(key, nameof(AgentOptions.UpdateCheckIntervalMinutes), 240),
            UpdateCheckJitterSeconds = ReadInt(key, nameof(AgentOptions.UpdateCheckJitterSeconds), 300),
            AliveIntervalMinutes = ReadInt(key, nameof(AgentOptions.AliveIntervalMinutes), 5),
            LogLevel = (string?)key.GetValue(nameof(AgentOptions.LogLevel)) ?? "INFO",
        };
    }

    public void Save(AgentOptions options)
    {
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath);
        key.SetValue(nameof(AgentOptions.ServerAddress), options.ServerAddress);
        key.SetValue(nameof(AgentOptions.ServerPort), options.ServerPort, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.UpdateCheckIntervalMinutes), options.UpdateCheckIntervalMinutes, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.UpdateCheckJitterSeconds), options.UpdateCheckJitterSeconds, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.AliveIntervalMinutes), options.AliveIntervalMinutes, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.LogLevel), options.LogLevel);
    }

    private static int ReadInt(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) is int value ? value : fallback;
}
