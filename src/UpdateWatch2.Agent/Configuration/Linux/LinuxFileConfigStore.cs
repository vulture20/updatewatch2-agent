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
    }
}
