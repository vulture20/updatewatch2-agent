using System.Runtime.Versioning;
using System.Text.Json;

namespace UpdateWatch2.Agent.Configuration.Linux;

/// <summary>
/// Reads/writes agent configuration as JSON at <see cref="DefaultPath"/> —
/// the functional equivalent of the Windows registry store, per CLAUDE.md
/// section 4.1. Placeholder for the future Linux agent; not yet wired into
/// a distro package (.deb/.rpm) installer.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxFileConfigStore(string path = LinuxFileConfigStore.DefaultPath) : IAgentConfigStore
{
    public const string DefaultPath = "/etc/updatewatch2/agent.conf";

    public AgentOptions Load()
    {
        if (!File.Exists(path))
        {
            return new AgentOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentOptions>(json) ?? new AgentOptions();
    }

    public void Save(AgentOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));

        // AgentOptions now carries a bearer secret (RegistrationToken —
        // see updatewatch2-agent#1) that lets whoever holds it complete
        // this agent's onboarding and receive its client certificate.
        // File.WriteAllText leaves the default umask permissions (commonly
        // world-readable), which would expose that token to any local
        // user; restrict to owner-only, the same boundary already used for
        // the server's CA/leaf certificates and this agent's own client
        // certificate file. Flagged by an automated security review after
        // the RegistrationToken field was added — this fix followed.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
