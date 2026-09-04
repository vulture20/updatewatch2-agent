using System.Runtime.Versioning;
using System.Security.Cryptography;
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

    // Deliberately thumbprint-scoped, not "always delete the file" — this
    // is called from two different situations that need opposite outcomes
    // on the SAME single-file store: renewal (Save writes the new cert,
    // then Delete(oldThumbprint) runs — must NOT remove what Save just
    // wrote) and self-heal-from-rejection (Delete(currentThumbprint) runs
    // with nothing having called Save first — MUST remove the file, or
    // Load() keeps returning the now-untrusted certificate forever and
    // RegistrationWorker's recovery loop never sees it as gone). Found by
    // a live run, not by reasoning about it up front: the original no-op
    // implementation was written only against the renewal call pattern
    // (where it happens to look correct) and silently broke self-heal
    // (updatewatch2-server#11/updatewatch2-agent#5), which needs the file
    // to actually disappear.
    public void Delete(string thumbprintSha256)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var current = X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
        if (string.Equals(current.GetCertHashString(HashAlgorithmName.SHA256), thumbprintSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
        }
    }
}
