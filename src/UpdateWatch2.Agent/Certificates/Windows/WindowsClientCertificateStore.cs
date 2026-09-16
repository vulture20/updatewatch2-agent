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
public class WindowsClientCertificateStore(IAgentConfigStore configStore, AgentOptions options, ILogger<WindowsClientCertificateStore> logger) : IClientCertificateStore
{
    public X509Certificate2? Load()
    {
        if (options.ClientCertificateThumbprint is not { Length: > 0 } thumbprint)
        {
            logger.LogDebug("X509Store: no ClientCertificateThumbprint configured, nothing to load.");
            return null;
        }

        logger.LogDebug("X509Store: opening My/LocalMachine (ReadOnly) to look up {Thumbprint}", thumbprint);
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
                logger.LogDebug("X509Store: found matching certificate for {Thumbprint}", thumbprint);
                return candidate;
            }
        }

        logger.LogDebug("X509Store: no certificate matching {Thumbprint} found in the store.", thumbprint);
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

        logger.LogDebug("X509Store: opening My/LocalMachine (ReadWrite) to import a new certificate ({ByteCount} byte(s) of PFX).", pfxBytes.Length);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);

        options.ClientCertificateThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        logger.LogDebug("X509Store: imported certificate {Thumbprint}", options.ClientCertificateThumbprint);
        configStore.Save(options);
    }

    public void Delete(string thumbprintSha256)
    {
        logger.LogDebug("X509Store: opening My/LocalMachine (ReadWrite) to delete {Thumbprint}", thumbprintSha256);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        // Same SHA-256-not-legacy-Thumbprint reasoning as Load() above.
        var removed = false;
        foreach (var candidate in store.Certificates)
        {
            if (string.Equals(candidate.GetCertHashString(HashAlgorithmName.SHA256), thumbprintSha256, StringComparison.OrdinalIgnoreCase))
            {
                store.Remove(candidate);
                removed = true;
            }
        }

        logger.LogDebug("X509Store: delete of {Thumbprint} {Outcome}", thumbprintSha256, removed ? "removed a matching certificate" : "found no matching certificate");
    }
}
