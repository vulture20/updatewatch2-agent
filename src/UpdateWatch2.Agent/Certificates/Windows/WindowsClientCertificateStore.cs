using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Certificates.Windows;

/// <summary>
/// Imports the issued client certificate into
/// <see cref="StoreName.My"/>/<see cref="StoreLocation.LocalMachine"/> and
/// tracks it by SHA-256 thumbprint in <see cref="AgentOptions.ClientCertificateThumbprint"/>
/// (persisted via <see cref="IAgentConfigStore"/>, the registry store on
/// this platform), rather than keeping a loose PFX file on disk.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsClientCertificateStore(IAgentConfigStore configStore, AgentOptions options) : IClientCertificateStore
{
    public X509Certificate2? Load()
    {
        if (options.ClientCertificateThumbprint is not { Length: > 0 } thumbprint)
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);

        // Deliberately not X509Store.Certificates.Find(X509FindType.FindByThumbprint, ...):
        // that compares against the legacy SHA-1 X509Certificate2.Thumbprint,
        // not the SHA-256 hash this project stores and compares everywhere
        // else (see the server-side InternalCertificateAuthority/CertificateValidator
        // remarks on the same SHA-1-vs-SHA-256 trap) — using it here would
        // silently never find the certificate.
        foreach (var candidate in store.Certificates)
        {
            if (string.Equals(candidate.GetCertHashString(HashAlgorithmName.SHA256), thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Save(byte[] pfxBytes)
    {
        // Not Exportable: once imported, the private key never leaves the
        // machine store again — deliberate hardening so a compromised
        // process on this machine can't re-export and exfiltrate the
        // credential, even though it can still use it locally.
        var certificate = X509CertificateLoader.LoadPkcs12(
            pfxBytes, password: null, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.MachineKeySet);

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);

        options.ClientCertificateThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        configStore.Save(options);
    }
}
