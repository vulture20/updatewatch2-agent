using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Persists the server CA root certificate(s) this agent has pinned as its
/// trust anchor (public key only — this never touches private key
/// material). Deliberately a plain file at a fixed, agent-owned path, not
/// the OS trust store: injecting a certificate into the system-wide trust
/// store is a separate, more invasive decision this feature doesn't make
/// unilaterally (see updatewatch2-agent#1). Pure file I/O, so no
/// [SupportedOSPlatform] guard is needed — Program.cs picks the
/// platform-appropriate default path.
///
/// Holds a COLLECTION, not a single certificate, since CA root rotation
/// (updatewatch2-server#6) means this agent may need to trust more than
/// one root at once — its original, TOFU-pinned one and a newly prepared
/// one it pre-fetched ahead of an admin activating a rotation. The file
/// format is whatever <see cref="X509Certificate2Collection.Import(byte[])"/>
/// accepts (a single raw DER cert, or a PKCS7 certs-only bundle) — both
/// round-trip through this store unchanged, so the very first cert this
/// agent ever saves (a single DER cert, from the bootstrap
/// <c>FetchCaCertificateAsync</c> call) reads back exactly the same via
/// <see cref="LoadAll"/> as a later PKCS7 bundle would.
/// </summary>
public class FileCaTrustStore(string path)
{
    public X509Certificate2Collection LoadAll()
    {
        var collection = new X509Certificate2Collection();
        if (File.Exists(path))
        {
            ImportPublicCertificates(collection, File.ReadAllBytes(path));
        }

        return collection;
    }

    /// <summary>The first pinned root, or null if none — used only where callers just need "is anything pinned yet at all" (RegistrationWorker's bootstrap check).</summary>
    public X509Certificate2? Load() => LoadAll().Cast<X509Certificate2?>().FirstOrDefault();

    public void Save(byte[] certificateOrBundleBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, certificateOrBundleBytes);
    }

    /// <summary>
    /// Adds any root from <paramref name="bundleBytes"/> not already
    /// trusted (compared by SHA-256 thumbprint), leaving every already-
    /// trusted root untouched — this never removes trust on its own
    /// (see updatewatch2-server#6's design notes on why retiring a root is
    /// deliberately an explicit, separate admin action, not something an
    /// agent infers for itself). Returns true if anything new was added.
    /// </summary>
    public bool MergeAdditional(byte[] bundleBytes)
    {
        if (bundleBytes.Length == 0)
        {
            return false;
        }

        var existing = LoadAll();
        var existingThumbprints = existing.Cast<X509Certificate2>()
            .Select(c => c.GetCertHashString(HashAlgorithmName.SHA256))
            .ToHashSet();

        var incoming = new X509Certificate2Collection();
        ImportPublicCertificates(incoming, bundleBytes);

        var added = false;
        foreach (var cert in incoming.Cast<X509Certificate2>())
        {
            if (existingThumbprints.Add(cert.GetCertHashString(HashAlgorithmName.SHA256)))
            {
                existing.Add(cert);
                added = true;
            }
        }

        if (added)
        {
            Save(existing.Export(X509ContentType.Pkcs7)!);
        }

        return added;
    }

    // X509CertificateLoader (the non-obsolete replacement this project
    // otherwise uses everywhere — see InternalCertificateAuthority) has no
    // method for a certs-only, no-private-key, possibly-multi-cert blob:
    // its collection loaders are all Pkcs12-specific (password + private
    // key material), and its single-cert loader can't return more than
    // one. X509Certificate2Collection.Import(byte[]) is genuinely the only
    // API that accepts either a single raw DER cert or a PKCS7 certs-only
    // bundle — confirmed by reflecting X509CertificateLoader's full public
    // surface, not assumed. The obsolete warning (SYSLIB0057) exists
    // because Import's OTHER overloads can also load a PKCS12 with a
    // private key unsafely; this call site only ever handles public
    // certificates, so it's suppressed here rather than worked around with
    // something less correct.
#pragma warning disable SYSLIB0057
    private static void ImportPublicCertificates(X509Certificate2Collection collection, byte[] bytes) => collection.Import(bytes);
#pragma warning restore SYSLIB0057
}
