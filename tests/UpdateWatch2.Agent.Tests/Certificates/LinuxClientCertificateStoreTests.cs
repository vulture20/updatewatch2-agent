using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates.Linux;

namespace UpdateWatch2.Agent.Tests.Certificates;

[SupportedOSPlatform("linux")]
public class LinuxClientCertificateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-cert-test-{Guid.NewGuid()}.pfx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Load_returns_null_when_no_certificate_has_been_saved()
    {
        var store = new LinuxClientCertificateStore(_path);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips_the_certificate()
    {
        var store = new LinuxClientCertificateStore(_path);
        var pfxBytes = CreateThrowawayCertificate("test-agent");

        store.Save(pfxBytes);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("CN=test-agent", loaded.Subject);
    }

    [Fact]
    public void Save_restricts_the_file_to_owner_only()
    {
        var store = new LinuxClientCertificateStore(_path);

        store.Save(CreateThrowawayCertificate("perm-test"));

        var mode = File.GetUnixFileMode(_path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Delete_removes_the_file_when_the_thumbprint_matches_the_currently_stored_certificate()
    {
        // The self-heal case (updatewatch2-server#11/updatewatch2-agent#5):
        // no Save preceded this call, so Delete itself must make Load()
        // start returning null, or RegistrationWorker's recovery loop
        // never notices the certificate is gone.
        var store = new LinuxClientCertificateStore(_path);
        var pfxBytes = CreateThrowawayCertificate("delete-match-test");
        store.Save(pfxBytes);
        using var saved = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);
        var thumbprint = saved.GetCertHashString(HashAlgorithmName.SHA256);

        store.Delete(thumbprint);

        Assert.Null(store.Load());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Delete_is_a_no_op_when_the_thumbprint_does_not_match_the_currently_stored_certificate()
    {
        // The renewal case (updatewatch2-server#7/updatewatch2-agent#3):
        // Save already wrote the NEW certificate before Delete is called
        // with the OLD thumbprint — Delete must not remove what Save just
        // wrote.
        var store = new LinuxClientCertificateStore(_path);
        store.Save(CreateThrowawayCertificate("delete-mismatch-test"));

        store.Delete("some-other-thumbprint-entirely");

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("CN=delete-mismatch-test", loaded.Subject);
    }

    [Fact]
    public void Delete_is_a_no_op_when_no_certificate_has_been_saved()
    {
        var store = new LinuxClientCertificateStore(_path);

        store.Delete("any-thumbprint-whatsoever");

        Assert.Null(store.Load());
    }

    private static byte[] CreateThrowawayCertificate(string subjectCn)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.Export(X509ContentType.Pfx);
    }
}
