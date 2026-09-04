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

    private static byte[] CreateThrowawayCertificate(string subjectCn)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.Export(X509ContentType.Pfx);
    }
}
