using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Tests.Certificates;

public class PinnedServerCertificateValidatorTests : IDisposable
{
    private readonly string _caPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-agent-ca-test-{Guid.NewGuid()}.pem");

    public void Dispose()
    {
        if (File.Exists(_caPath))
        {
            File.Delete(_caPath);
        }
    }

    [Fact]
    public void Accepts_unconditionally_when_no_CA_is_pinned_yet_TOFU()
    {
        var validator = CreateValidator(serverAddress: "updatewatch2.example.com");
        var (_, leaf) = CreateCaAndLeaf("updatewatch2.example.com");

        Assert.True(validator.Validate(leaf));
    }

    [Fact]
    public void Rejects_a_null_certificate()
    {
        var validator = CreateValidator(serverAddress: "updatewatch2.example.com");

        Assert.False(validator.Validate(null));
    }

    [Fact]
    public void Accepts_a_certificate_that_chains_to_the_pinned_CA_and_matches_the_configured_server_address()
    {
        var (root, leaf) = CreateCaAndLeaf("updatewatch2.example.com");
        new FileCaTrustStore(_caPath).Save(root.Export(X509ContentType.Cert));
        var validator = CreateValidator(serverAddress: "updatewatch2.example.com");

        Assert.True(validator.Validate(leaf));
    }

    [Fact]
    public void Rejects_a_certificate_that_chains_to_the_pinned_CA_but_names_a_different_host()
    {
        var (root, leaf) = CreateCaAndLeaf("some-other-host.example.com");
        new FileCaTrustStore(_caPath).Save(root.Export(X509ContentType.Cert));
        var validator = CreateValidator(serverAddress: "updatewatch2.example.com");

        Assert.False(validator.Validate(leaf));
    }

    [Fact]
    public void Rejects_a_certificate_that_does_not_chain_to_the_pinned_CA()
    {
        var (pinnedRoot, _) = CreateCaAndLeaf("updatewatch2.example.com");
        new FileCaTrustStore(_caPath).Save(pinnedRoot.Export(X509ContentType.Cert));
        var (_, unrelatedLeaf) = CreateCaAndLeaf("updatewatch2.example.com"); // signed by a *different* root
        var validator = CreateValidator(serverAddress: "updatewatch2.example.com");

        Assert.False(validator.Validate(unrelatedLeaf));
    }

    private PinnedServerCertificateValidator CreateValidator(string serverAddress) =>
        new(new FileCaTrustStore(_caPath), new AgentOptions { ServerAddress = serverAddress },
            NullLogger<PinnedServerCertificateValidator>.Instance);

    private static (X509Certificate2 Root, X509Certificate2 Leaf) CreateCaAndLeaf(string sanHostname)
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Test CA", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest($"CN={sanHostname}", leafKey, HashAlgorithmName.SHA256);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(sanHostname);
        leafRequest.CertificateExtensions.Add(sanBuilder.Build());
        using var leafPublicOnly = leafRequest.Create(
            root, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var leafWithKey = leafPublicOnly.CopyWithPrivateKey(leafKey);

        // Reload from exported bytes so the returned objects outlive the
        // `using`-scoped originals above.
        var rootReloaded = X509CertificateLoader.LoadCertificate(root.Export(X509ContentType.Cert));
        var leafReloaded = X509CertificateLoader.LoadPkcs12(leafWithKey.Export(X509ContentType.Pfx), password: null);
        return (rootReloaded, leafReloaded);
    }
}
