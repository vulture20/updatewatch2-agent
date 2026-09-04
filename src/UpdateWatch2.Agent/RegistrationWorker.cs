using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;

namespace UpdateWatch2.Agent;

/// <summary>
/// A persistent maintenance loop, running for the process's lifetime, that
/// drives onboarding (register-then-poll-until-approved-and-certified,
/// updatewatch2-agent#1) and — unlike its original run-once-at-startup
/// design — keeps watching afterward so a certificate lost later in the
/// process's life (wiped/reinstalled under the same hostname, a corrupted
/// local store) is recovered without a service restart
/// (updatewatch2-server#8/updatewatch2-agent#3):
///
/// - Each iteration starts by checking <see cref="IClientCertificateStore.Load"/>.
/// - If a certificate is present: attach it (idempotent — a no-op once
///   already attached and <see cref="IAgentCertificateState.IsReady"/>) and
///   idle for <see cref="AgentOptions.CertificateMaintenanceIntervalSeconds"/>
///   before checking again. Still looping, not returning, so a certificate
///   lost later is noticed on a later iteration — this is what makes
///   "no restart needed" actually true, not just true at startup.
/// - If no certificate is present — never registered yet, or one was lost
///   since this process started, the same recovery path either way — the
///   config store is re-read directly (bypassing the cached
///   <see cref="AgentOptions"/> DI singleton, which is otherwise loaded
///   once at startup) so a freshly admin-issued re-issuance token, written
///   into the registry/config file while this process keeps running, is
///   picked up without a restart. One registration attempt is made, then
///   the loop idles <see cref="AgentOptions.RegistrationRetryIntervalSeconds"/>
///   before trying again.
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
///
/// This worker owns proactively renewing a certificate that is still valid
/// but approaching expiry (updatewatch2-server#7) is deliberately NOT part
/// of this class — that lives on <see cref="HeartbeatWorker"/> instead,
/// piggybacked on its existing periodic cadence, since it needs the
/// already-certified shared handler, not the bootstrap one this class
/// uses.
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
        var bootstrapClient = createBootstrapClient();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var certificate = certificateStore.Load();
                if (certificate is not null)
                {
                    if (!certificateState.IsReady)
                    {
                        AttachAndMarkReady(certificate);
                        logger.LogInformation("Using previously issued client certificate — skipping registration.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.CertificateMaintenanceIntervalSeconds)), stoppingToken);
                    continue;
                }

                // No usable local certificate — either never registered yet,
                // or one was lost/wiped/corrupted since this process
                // started. Re-read the config store directly (not the
                // cached AgentOptions singleton) so a freshly admin-issued
                // token, placed into the registry/config file without a
                // restart, is picked up here.
                var fresh = configStore.Load();
                if (fresh.RegistrationToken != options.RegistrationToken)
                {
                    options.RegistrationToken = fresh.RegistrationToken;
                }

                try
                {
                    await EnsureCaPinnedAsync(bootstrapClient, stoppingToken);
                    await TryRegisterOnceAsync(bootstrapClient, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Registration attempt failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.RegistrationRetryIntervalSeconds)), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — fine, nothing to clean up.
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

    private async Task TryRegisterOnceAsync(IServerClient bootstrapClient, CancellationToken ct)
    {
        var result = await bootstrapClient.RegisterAsync(options.RegistrationToken, ct);

        if (result.RegistrationToken is not null && result.RegistrationToken != options.RegistrationToken)
        {
            options.RegistrationToken = result.RegistrationToken;
            configStore.Save(options);
        }

        if (result.Approved && result.Certificate is not null)
        {
            // Only non-null in the recovery case — an entry the local store
            // still has under an old thumbprint even though this iteration
            // otherwise reached here via the "no certificate" branch (e.g.
            // a partial prior failure). Nothing to clean up on a genuine
            // first-ever registration.
            var previousThumbprint = certificateStore.Load()?.GetCertHashString(HashAlgorithmName.SHA256);

            var pfxBytes = Convert.FromBase64String(result.Certificate);
            certificateStore.Save(pfxBytes);
            if (previousThumbprint is not null)
            {
                // Save first, Delete second — if Delete were to fail, this
                // agent is left with (at worst) an orphaned old entry
                // alongside a working new certificate, never with no
                // working certificate at all.
                certificateStore.Delete(previousThumbprint);
            }

            options.RegistrationToken = null;
            configStore.Save(options);

            var storedCertificate = certificateStore.Load()
                ?? throw new InvalidOperationException("Client certificate was just saved but could not be reloaded.");
            AttachAndMarkReady(storedCertificate);
            logger.LogInformation("Registration complete — received and installed the client certificate.");
            return;
        }

        if (result.Approved)
        {
            // Approved, but the server has no certificate to deliver
            // (one-shot delivery already happened) and none is stored
            // locally either — e.g. this agent was wiped and reinstalled
            // under the same hostname, or an admin cleared its certificate
            // for re-issuance (updatewatch2-server#8). Recovery just needs
            // a fresh registration token in the local config — this loop
            // re-reads that config every iteration (see ExecuteAsync), so
            // placing one there is enough; no restart required.
            logger.LogWarning(
                "Server reports this agent as approved but has no certificate to deliver, and none is stored locally. " +
                "Waiting for a fresh registration token (e.g. from an admin-mediated re-issuance) — will keep retrying.");
        }
        else
        {
            logger.LogInformation("Still waiting for admin approval.");
        }
    }

    private void AttachAndMarkReady(X509Certificate2 certificate)
    {
        // Clear then Add, not just Add — this method runs both on first
        // attach and after a recovery re-registration, so it must be safe
        // to call more than once without leaving a stale certificate
        // alongside the current one (which would make TLS client-cert
        // selection ambiguous, both chaining to the same pinned CA).
        sharedHttpHandler.SslOptions.ClientCertificates!.Clear();
        sharedHttpHandler.SslOptions.ClientCertificates!.Add(certificate);
        certificateState.MarkReady();
    }
}
