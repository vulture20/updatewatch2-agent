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
}
