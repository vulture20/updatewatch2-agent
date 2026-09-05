using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Validates the server's TLS certificate against this agent's pinned CA
/// (see <see cref="FileCaTrustStore"/>) instead of the OS trust store. A
/// custom <c>RemoteCertificateValidationCallback</c> on
/// <c>SocketsHttpHandler.SslOptions</c> entirely replaces .NET's default
/// validation, including hostname checking — so this must re-implement
/// that itself (a SAN check against the configured <c>ServerAddress</c>),
/// or it would accept any certificate the pinned CA ever issued regardless
/// of which host it names.
///
/// Before a CA has been pinned yet — an agent's very first contact, before
/// <see cref="RegistrationWorker"/> has fetched and saved one — there is
/// nothing to validate against: this accepts unconditionally, a deliberate
/// trust-on-first-use (TOFU) decision, not an oversight. It's logged at
/// Warning every time, with the residual risk spelled out, rather than
/// silently accepted.
/// </summary>
public class PinnedServerCertificateValidator(FileCaTrustStore caTrustStore, AgentOptions options, ILogger<PinnedServerCertificateValidator> logger)
{
    public bool Validate(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        var pinnedRoots = caTrustStore.LoadAll();
        if (pinnedRoots.Count == 0)
        {
            logger.LogWarning(
                "No pinned server CA certificate yet — trusting the server on this first contact (trust-on-first-use). " +
                "A network attacker present at exactly this moment could intercept this connection; verify out of band if that's a concern for this deployment.");
            return true;
        }

        var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        // Multiple pinned roots, not just one, since CA root rotation
        // (updatewatch2-server#6) can leave this agent trusting an old
        // root and a newly pre-fetched pending one at the same time — the
        // server's leaf only ever needs to chain to ANY one of them.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(pinnedRoots);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!chain.Build(certificate2))
        {
            logger.LogWarning("Server certificate does not chain to the pinned CA — rejecting.");
            return false;
        }

        var sanNames = certificate2.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault()?.EnumerateDnsNames() ?? [];
        if (!string.IsNullOrEmpty(options.ServerAddress) && sanNames.Contains(options.ServerAddress, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        logger.LogWarning("Server certificate does not list the configured server address ({ServerAddress}) as a SAN — rejecting.", options.ServerAddress);
        return false;
    }
}
