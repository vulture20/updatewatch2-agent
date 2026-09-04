namespace UpdateWatch2.Agent.Certificates;

/// <summary>
/// Signals when this agent has a usable client certificate attached to its
/// HTTP handler — the gate <see cref="HeartbeatWorker"/> and
/// <see cref="UpdateCheckWorker"/> wait on before making any cert-protected
/// server call, rather than spamming their existing error-log-and-retry
/// loops during the (expected) pre-approval period. Set once, by
/// <see cref="RegistrationWorker"/>, and never unset — a certificate, once
/// attached, stays attached for the process lifetime.
/// </summary>
public interface IAgentCertificateState
{
    bool IsReady { get; }

    void MarkReady();

    Task WaitUntilReadyAsync(CancellationToken ct = default);
}
