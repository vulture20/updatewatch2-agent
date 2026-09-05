using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates;

namespace UpdateWatch2.Agent.Tests.Certificates;

/// <summary>
/// Covers the multi-root trust support added for CA rotation
/// (updatewatch2-server#6) — <see cref="PinnedServerCertificateValidatorTests"/>
/// covers this store as used through the validator; these tests exercise
/// its own load/save/merge contract directly.
/// </summary>
public class FileCaTrustStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-ca-trust-test-{Guid.NewGuid()}.pem");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void LoadAll_returns_an_empty_collection_when_nothing_has_been_saved_yet()
    {
        var store = new FileCaTrustStore(_path);

        Assert.Empty(store.LoadAll());
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_then_LoadAll_round_trips_a_single_raw_DER_certificate()
    {
        // The exact shape RegistrationWorker's bootstrap FetchCaCertificateAsync
        // hands this store on an agent's very first contact — must keep
        // working unchanged now that this store supports more than one cert.
        var store = new FileCaTrustStore(_path);
        var root = CreateRoot();

        store.Save(root.Export(X509ContentType.Cert));

        var loaded = store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal(root.GetCertHashString(HashAlgorithmName.SHA256), loaded[0].GetCertHashString(HashAlgorithmName.SHA256));
    }

    [Fact]
    public void MergeAdditional_adds_a_new_root_without_disturbing_an_existing_one()
    {
        var store = new FileCaTrustStore(_path);
        var first = CreateRoot();
        var second = CreateRoot();
        store.Save(first.Export(X509ContentType.Cert));

        var added = store.MergeAdditional(second.Export(X509ContentType.Cert));

        Assert.True(added);
        var thumbprints = store.LoadAll().Cast<X509Certificate2>().Select(c => c.GetCertHashString(HashAlgorithmName.SHA256)).ToHashSet();
        Assert.Equal(2, thumbprints.Count);
        Assert.Contains(first.GetCertHashString(HashAlgorithmName.SHA256), thumbprints);
        Assert.Contains(second.GetCertHashString(HashAlgorithmName.SHA256), thumbprints);
    }

    [Fact]
    public void MergeAdditional_is_a_no_op_when_every_root_in_the_bundle_is_already_trusted()
    {
        var store = new FileCaTrustStore(_path);
        var root = CreateRoot();
        store.Save(root.Export(X509ContentType.Cert));

        var added = store.MergeAdditional(root.Export(X509ContentType.Cert));

        Assert.False(added);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void MergeAdditional_treats_an_empty_response_as_a_no_op_rather_than_throwing()
    {
        // Defensive: a server that (for whatever reason) ever returned an
        // empty body must not crash this agent's heartbeat loop.
        var store = new FileCaTrustStore(_path);
        var root = CreateRoot();
        store.Save(root.Export(X509ContentType.Cert));

        var added = store.MergeAdditional([]);

        Assert.False(added);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void MergeAdditional_can_add_more_than_one_new_root_from_a_single_bundle()
    {
        var store = new FileCaTrustStore(_path);
        var first = CreateRoot();
        var second = CreateRoot();
        var third = CreateRoot();
        store.Save(first.Export(X509ContentType.Cert));

        var bundle = new X509Certificate2Collection { second, third };
        var added = store.MergeAdditional(bundle.Export(X509ContentType.Pkcs7)!);

        Assert.True(added);
        Assert.Equal(3, store.LoadAll().Count);
    }

    private static X509Certificate2 CreateRoot()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Test CA {Guid.NewGuid()}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
    }
}
