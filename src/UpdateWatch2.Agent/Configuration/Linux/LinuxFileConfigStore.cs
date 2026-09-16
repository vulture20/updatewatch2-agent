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
        var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });

        // AgentOptions now carries a bearer secret (RegistrationToken —
        // see updatewatch2-agent#1) that lets whoever holds it complete
        // this agent's onboarding and receive its client certificate.
        //
        // A prior fix (below) called File.WriteAllText then
        // File.SetUnixFileMode afterward — a second security review found
        // that left a real TOCTOU window: between those two calls the file
        // exists on disk with whatever mode File.WriteAllText's own
        // creation leaves it at (the process's default reduced by umask,
        // commonly world-readable — confirmed by hand: umask 0022 produces
        // 644), not the intended owner-only mode. A local attacker racing
        // that window (e.g. an inotify watch on this directory) could read
        // the bearer token during it. Creating the file with the
        // restrictive mode already applied (FileStreamOptions.UnixCreateMode)
        // closes the window entirely instead of narrowing it after the
        // fact — UnixCreateMode only takes effect when the file is
        // actually created, so the explicit SetUnixFileMode call below is
        // kept too, purely to migrate a file an older binary already
        // created with the wrong mode; it's a no-op once this path has
        // run once.
        using (var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        }))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
