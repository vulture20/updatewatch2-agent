namespace UpdateWatch2.Agent.Communication;

/// <summary>Talks to the server's agent-facing API (mutual TLS — see updatewatch2-agent#1/updatewatch2-server#1).</summary>
public interface IServerClient
{
    /// <summary>Fetches the server's CA certificate (public key only) — this agent's trust anchor. Anonymous; no cert needed.</summary>
    Task<byte[]> FetchCaCertificateAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers (or polls the registration status of) this agent with the
    /// server. Per CLAUDE.md's onboarding flow, a newly registered agent
    /// stays unapproved until an admin confirms it — callers should keep
    /// retrying (with backoff) while <see cref="RegisterResult.Approved"/>
    /// is false rather than treating that as an error. Pass the token from
    /// the previous response (or null on first contact) — see
    /// RegistrationWorker for the full state machine this drives.
    /// </summary>
    Task<RegisterResult> RegisterAsync(string? registrationToken, CancellationToken ct = default);

    /// <summary>
    /// Sends an alive heartbeat. The returned <see cref="AliveOutcome"/>
    /// distinguishes a certificate-rejection (401/403 — the server no
    /// longer trusts this agent's certificate, e.g. an admin reissued it
    /// while this agent kept running, updatewatch2-server#11) from any
    /// other failure, so a caller can react specifically to the former
    /// without treating an unrelated server error the same way.
    /// </summary>
    Task<AliveOutcome> SendAliveAsync(CancellationToken ct = default);

    Task ReportUpdatesAsync(ReportUpdatesRequest report, CancellationToken ct = default);

    /// <summary>Fetches the server's four version numbers. Anonymous; no cert needed — used for protocol-compatibility detection (updatewatch2-server#3).</summary>
    Task<VersionResponse> FetchVersionAsync(CancellationToken ct = default);

    /// <summary>
    /// Requests a fresh client certificate before the current one expires
    /// (updatewatch2-server#7) — authenticated by the CURRENT still-valid
    /// certificate, not a token. Must be called on the shared, cert-attached
    /// <see cref="IServerClient"/> (see HeartbeatWorker), never a bootstrap
    /// one — same connection-pooling reasoning as RegistrationWorker's
    /// class-level remarks: SslOptions is only consulted when a NEW TLS
    /// connection is negotiated.
    /// </summary>
    Task<RenewCertificateResult> RenewCertificateAsync(CancellationToken ct = default);
}
