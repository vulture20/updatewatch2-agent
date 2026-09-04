using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Agent.Certificates.Linux;

/// <summary>
/// Stores the issued client certificate as a PFX file at
/// <see cref="DefaultPath"/>, alongside <c>agent.conf</c> — the Linux
/// equivalent of the Windows machine certificate store.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxClientCertificateStore(string path = LinuxClientCertificateStore.DefaultPath) : IClientCertificateStore
{
    public const string DefaultPath = "/etc/updatewatch2/agent.pfx";

    public X509Certificate2? Load() =>
        File.Exists(path) ? X509CertificateLoader.LoadPkcs12FromFile(path, password: null) : null;

    public void Save(byte[] pfxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, pfxBytes);

        // Restricting this to owner-only is only actually meaningful once
        // a real systemd unit runs this service as a dedicated non-root
        // account — installer/linux/ doesn't exist yet, so that's a
        // documented prerequisite gap, not silently assumed away.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // No-op: a single file, already unconditionally overwritten by Save —
    // there's no separate "old entry" to clean up the way the Windows
    // machine store needs.
    public void Delete(string thumbprintSha256)
    {
    }
}
