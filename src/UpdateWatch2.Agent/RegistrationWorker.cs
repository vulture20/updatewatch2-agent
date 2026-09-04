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
///
/// The bootstrap calls (fetching the CA certificate, every registration
/// poll) run over their own private <see cref="IServerClient"/>, built by
/// <paramref name="createBootstrapClient"/> on its own throwaway
/// HttpClient/handler — deliberately *not* the shared
/// <see cref="SocketsHttpHandler"/> the rest of the agent uses. Confirmed
/// live to matter, not just in theory: HTTP keep-alive connections are
/// pooled per (scheme, host, port) and reused regardless of whether a
/// client certificate is added to the handler afterward, since SslOptions
/// is only consulted when a *new* TLS connection is negotiated. Making
/// these bootstrap calls on the shared handler meant the very
/// pre-certificate connections it opened stayed pooled and got reused for
/// the first "real" (post-registration) calls too — which therefore never
/// actually presented the certificate and got rejected. Isolating the
/// bootstrap traffic onto its own connection pool means the shared
/// handler's pool stays completely empty until the certificate is already
/// attached, so its first-ever connection is the first one that needs it.
/// </summary>
public class RegistrationWorker(
    AgentOptions options,
    IAgentConfigStore configStore,
    FileCaTrustStore caTrustStore,
    IClientCertificateStore certificateStore,
    SocketsHttpHandler sharedHttpHandler,
    Func<IServerClient> createBootstrapClient,
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
                sharedHttpHandler.SslOptions.ClientCertificates!.Add(existingCertificate);
                certificateState.MarkReady();
                logger.LogInformation("Using previously issued client certificate — skipping registration.");
                return;
            }

            var bootstrapClient = createBootstrapClient();
            await EnsureCaPinnedAsync(bootstrapClient, stoppingToken);
            await PollUntilCertifiedAsync(bootstrapClient, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down before registration completed — fine, nothing to clean up.
        }
    }

    private async Task EnsureCaPinnedAsync(IServerClient bootstrapClient, CancellationToken ct)
    {
        if (caTrustStore.Load() is not null)
        {
            return;
        }

        var caBytes = await bootstrapClient.FetchCaCertificateAsync(ct);
        caTrustStore.Save(caBytes);
        logger.LogInformation("Pinned the server's CA certificate on first contact.");
    }

    private async Task PollUntilCertifiedAsync(IServerClient bootstrapClient, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await bootstrapClient.RegisterAsync(options.RegistrationToken, stoppingToken);

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
                    sharedHttpHandler.SslOptions.ClientCertificates!.Add(storedCertificate);
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
}
