using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent;

/// <summary>
/// Drives the register-then-poll-until-approved-and-certified flow
/// (updatewatch2-agent#1). Runs once at startup:
///
/// - If a client certificate is already stored locally, it's loaded and
///   attached with no network call at all — re-registering on every
///   restart would trip the server's one-shot certificate delivery (see
///   the server's AgentRegistrationService), so an already-certified agent
///   must never call RegisterAsync again.
/// - Otherwise: pin the server's CA certificate on first contact if it
///   isn't already pinned (see PinnedServerCertificateValidator's
///   trust-on-first-use remarks), then poll
///   RegistrationRetryIntervalSeconds apart until the server hands back a
///   certificate, store and attach it, and mark this agent ready —
///   unblocking HeartbeatWorker/UpdateCheckWorker, which both wait on
///   IAgentCertificateState before doing anything that needs a
///   certificate.
/// </summary>
public class RegistrationWorker(
    AgentOptions options,
    IAgentConfigStore configStore,
    IServerClient serverClient,
    FileCaTrustStore caTrustStore,
    IClientCertificateStore certificateStore,
    SocketsHttpHandler httpHandler,
    IAgentCertificateState certificateState,
    ILogger<RegistrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var existingCertificate = certificateStore.Load();
            if (existingCertificate is not null)
            {
                AttachClientCertificate(existingCertificate);
                certificateState.MarkReady();
                logger.LogInformation("Using previously issued client certificate — skipping registration.");
                return;
            }

            await EnsureCaPinnedAsync(stoppingToken);
            await PollUntilCertifiedAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down before registration completed — fine, nothing to clean up.
        }
    }

    private async Task EnsureCaPinnedAsync(CancellationToken ct)
    {
        if (caTrustStore.Load() is not null)
        {
            return;
        }

        var caBytes = await serverClient.FetchCaCertificateAsync(ct);
        caTrustStore.Save(caBytes);
        logger.LogInformation("Pinned the server's CA certificate on first contact.");
    }

    private async Task PollUntilCertifiedAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await serverClient.RegisterAsync(options.RegistrationToken, stoppingToken);

                if (result.RegistrationToken is not null && result.RegistrationToken != options.RegistrationToken)
                {
                    options.RegistrationToken = result.RegistrationToken;
                    configStore.Save(options);
                }

                if (result.Approved && result.Certificate is not null)
                {
                    var pfxBytes = Convert.FromBase64String(result.Certificate);
                    certificateStore.Save(pfxBytes);
                    options.RegistrationToken = null;
                    configStore.Save(options);

                    var storedCertificate = certificateStore.Load()
                        ?? throw new InvalidOperationException("Client certificate was just saved but could not be reloaded.");
                    AttachClientCertificate(storedCertificate);
                    certificateState.MarkReady();
                    logger.LogInformation("Registration complete — received and installed the client certificate.");
                    return;
                }

                if (result.Approved)
                {
                    // Approved, but the server has no certificate to
                    // deliver (one-shot delivery already happened) and
                    // none is stored locally either — e.g. this agent was
                    // wiped and reinstalled under the same hostname. Needs
                    // an admin-mediated re-issuance, which doesn't exist
                    // yet — a deliberate follow-up, not implemented here.
                    logger.LogWarning(
                        "Server reports this agent as approved but has no certificate to deliver, and none is stored locally. " +
                        "An admin-mediated re-issuance is needed but not yet implemented — will keep retrying.");
                }
                else
                {
                    logger.LogInformation("Still waiting for admin approval.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Registration attempt failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.RegistrationRetryIntervalSeconds)), stoppingToken);
        }
    }

    private void AttachClientCertificate(X509Certificate2 certificate) =>
        httpHandler.SslOptions.ClientCertificates!.Add(certificate);
}
