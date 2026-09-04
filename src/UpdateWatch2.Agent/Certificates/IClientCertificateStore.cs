using System.Security.Cryptography.X509Certificates;

namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Stores/loads this agent's own client certificate, once issued by the
/// server. Genuinely platform-specific (Windows: the machine certificate
/// store; Linux: a file) — see the Windows/Linux implementations, selected
/// in Program.cs the same way <c>IAgentConfigStore</c> already is.
/// </summary>
public interface IClientCertificateStore
{
    /// <summary>Loads the previously received client certificate, or null if none has been received yet.</summary>
    X509Certificate2? Load();

    /// <summary>Persists a newly issued client certificate (PFX bytes, exactly as received from the server).</summary>
    void Save(byte[] pfxBytes);

    /// <summary>
    /// Removes a stored certificate by its SHA-256 thumbprint — a no-op if
    /// nothing currently stored matches that thumbprint. Two distinct
    /// callers rely on this: renewal/re-issuance calls it with the OLD
    /// thumbprint *after* <see cref="Save"/> has already written the new
    /// certificate (so the Windows machine store doesn't accumulate an
    /// orphaned entry for every certificate this agent has ever held,
    /// updatewatch2-agent#3 — and, on Linux, so the file left behind by
    /// that already-superseded thumbprint is correctly a no-op); self-heal
    /// calls it with the CURRENT thumbprint with no preceding
    /// <see cref="Save"/> at all, specifically to make the next
    /// <see cref="Load"/> return null (updatewatch2-server#11/updatewatch2-agent#5)
    /// — on Linux this must actually delete the file, not silently do
    /// nothing, or that recovery path never triggers.
    /// </summary>
    void Delete(string thumbprintSha256);
}
