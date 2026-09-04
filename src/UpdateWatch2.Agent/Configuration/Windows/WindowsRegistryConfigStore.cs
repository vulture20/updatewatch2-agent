using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace UpdateWatch2.Agent.Configuration.Windows;

/// <summary>
/// Reads/writes agent configuration under
/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\UpdateWatch2\Agent</c>. Values are
/// normally written once by the NSIS installer (see
/// installer/nsis/setup.nsi); <see cref="Save"/> is provided for
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
            RegistrationRetryIntervalSeconds = ReadInt(key, nameof(AgentOptions.RegistrationRetryIntervalSeconds), 30),
            RegistrationToken = (string?)key.GetValue(nameof(AgentOptions.RegistrationToken)),
            ClientCertificateThumbprint = (string?)key.GetValue(nameof(AgentOptions.ClientCertificateThumbprint)),
            CertificateRenewalLeadTimeDays = ReadInt(key, nameof(AgentOptions.CertificateRenewalLeadTimeDays), 60),
            CertificateMaintenanceIntervalSeconds = ReadInt(key, nameof(AgentOptions.CertificateMaintenanceIntervalSeconds), 900),
        };
    }

    public void Save(AgentOptions options)
    {
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RestrictedToAdministratorsAndSystem());
        key.SetValue(nameof(AgentOptions.ServerAddress), options.ServerAddress);
        key.SetValue(nameof(AgentOptions.ServerPort), options.ServerPort, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.UpdateCheckIntervalMinutes), options.UpdateCheckIntervalMinutes, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.UpdateCheckJitterSeconds), options.UpdateCheckJitterSeconds, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.AliveIntervalMinutes), options.AliveIntervalMinutes, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.LogLevel), options.LogLevel);
        key.SetValue(nameof(AgentOptions.RegistrationRetryIntervalSeconds), options.RegistrationRetryIntervalSeconds, RegistryValueKind.DWord);
        // SetValue with a null string throws, so a not-yet-set/cleared value
        // deletes the named value instead of writing an empty string —
        // keeps Load()'s null-coalescing-free reads (via `as string`)
        // accurate for "never set" vs. "explicitly empty".
        SetOrDeleteString(key, nameof(AgentOptions.RegistrationToken), options.RegistrationToken);
        SetOrDeleteString(key, nameof(AgentOptions.ClientCertificateThumbprint), options.ClientCertificateThumbprint);
        key.SetValue(nameof(AgentOptions.CertificateRenewalLeadTimeDays), options.CertificateRenewalLeadTimeDays, RegistryValueKind.DWord);
        key.SetValue(nameof(AgentOptions.CertificateMaintenanceIntervalSeconds), options.CertificateMaintenanceIntervalSeconds, RegistryValueKind.DWord);
    }

    private static void SetOrDeleteString(RegistryKey key, string name, string? value)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value);
        }
    }

    private static int ReadInt(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) is int value ? value : fallback;

    // AgentOptions now carries a bearer secret (RegistrationToken — see
    // updatewatch2-agent#1) that lets whoever holds it complete this
    // agent's onboarding and receive its client certificate.
    // HKLM\SOFTWARE's default ACL commonly grants standard, non-admin
    // users read access to subkeys created under it, which would expose
    // that token; this breaks inheritance from the parent key and grants
    // only Administrators/SYSTEM, the same boundary already used for the
    // server's CA/leaf certificates and this agent's own client
    // certificate file. Flagged by an automated security review after the
    // RegistrationToken field was added — this fix followed, though it is
    // unverified against a real Windows registry in this session (no
    // Windows host available to run it against).
    private static RegistrySecurity RestrictedToAdministratorsAndSystem()
    {
        var security = new RegistrySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            RegistryRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            RegistryRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
