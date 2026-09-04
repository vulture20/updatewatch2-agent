using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Persists the server's CA certificate this agent has pinned as its trust
/// anchor (public key only — this never touches private key material).
/// Deliberately a plain file at a fixed, agent-owned path, not the OS trust
/// store: injecting a certificate into the system-wide trust store is a
/// separate, more invasive decision this feature doesn't make unilaterally
/// (see updatewatch2-agent#1). Pure file I/O, so no
/// [SupportedOSPlatform] guard is needed — Program.cs picks the
/// platform-appropriate default path.
/// </summary>
public class FileCaTrustStore(string path)
{
    public X509Certificate2? Load() =>
        File.Exists(path) ? X509CertificateLoader.LoadCertificateFromFile(path) : null;

    public void Save(byte[] certificateBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, certificateBytes);
    }
}
