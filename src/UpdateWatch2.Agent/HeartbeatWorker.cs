using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.UpdateCheck;
using CheckerInstallOutcome = UpdateWatch2.Agent.UpdateCheck.InstallOutcome;
using WireInstallOutcome = UpdateWatch2.Agent.Communication.InstallOutcome;

namespace UpdateWatch2.Agent;

/// <summary>
/// Sends a periodic alive message to the server (CLAUDE.md section 2.4)
/// and, piggybacked on the same cadence, checks for a protocol-version
/// mismatch (updatewatch2-server#3/updatewatch2-agent#2), whether this
/// agent's client certificate needs proactive renewal
/// (updatewatch2-server#7/updatewatch2-agent#3), whether the server has
/// stopped trusting the certificate this agent is still presenting
/// (updatewatch2-server#11/updatewatch2-agent#5 — e.g. an admin reissued
/// it while this agent kept running, as opposed to genuinely losing it,
/// which <see cref="RegistrationWorker"/> already handles), and whether an
/// admin has remote-triggered an install (updatewatch2-server#10/
/// updatewatch2-agent#4, driven straight off this heartbeat's own alive
/// response rather than <see cref="UpdateCheckWorker"/>'s much coarser,
/// jittered interval — a manual "install now" trigger is time-sensitive by
/// definition, unlike routine update detection), and whether the server has
/// offered a newer agent release to self-update to
/// (updatewatch2-server#14/updatewatch2-agent#14, same reasoning: also
/// time-sensitive and also driven off this same alive response), and
/// whether the server reports this agent's own client certificate was
/// signed under a CA root a rotation has since superseded
/// (updatewatch2-server#6 follow-up — CA rotation never reissues an
/// already-onboarded agent's leaf on its own, so this prompts an eager
/// renewal instead of relying solely on the lead-time check above), and —
/// purely local, no server involvement — deleting old downloaded
/// self-update packages from the staging directory that are older than
/// <see cref="AgentOptions.SelfUpdateStagingRetentionDays"/>, always
/// keeping at least the most recent one (see
/// <see cref="SelfUpdate.SelfUpdateStagingCleaner"/>) — reusing this
/// existing periodic cycle rather than a one-time startup check means a
/// server upgrade, an approaching expiry, a mid-lifetime revocation, a
/// fresh install request, a fresh agent release, a CA rotation activating,
/// or old update packages piling up that happens while this agent keeps
/// running all get detected/handled too, not just a condition already
/// present at this agent's own last startup.
/// </summary>
public class HeartbeatWorker(
    AgentOptions options,
    IServerClient serverClient,
    IAgentCertificateState certificateState,
    IClientCertificateStore certificateStore,
    FileCaTrustStore caTrustStore,
    SocketsHttpHandler sharedHttpHandler,
    IUpdateChecker updateChecker,
    IAgentSelfUpdater selfUpdater,
    SelfUpdateStagingCleaner selfUpdateStagingCleaner,
    ILogger<HeartbeatWorker> logger) : BackgroundService
{
    // A single 401/403 could in principle be some transient fluke this
    // worker hasn't anticipated — requiring it twice in a row, with
    // anything else (a success, or a non-cert-related failure) resetting
    // the count, keeps this from ever firing on a one-off (updatewatch2-server#11's
    // own design note). In practice a single clean 401/403 IS already an
    // unambiguous signal (it only happens after a full round-trip
    // completes — a real network problem throws instead, never reaches
    // this check at all), so this is deliberate extra caution, not a
    // response to an observed false positive.
    private const int CertificateRejectionThreshold = 2;

    private int _consecutiveCertificateRejections;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing here works before RegistrationWorker has attached a
        // client certificate (updatewatch2-agent#1) — wait rather than
        // hitting the cert-gated alive endpoint and logging the same
        // expected failure on every tick.
        await certificateState.WaitUntilReadyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await HandleAliveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send alive heartbeat");
            }

            // Deliberately its own try/catch, independent of the alive
            // call above — a transient failure to fetch the version must
            // not affect the heartbeat itself.
            try
            {
                await CheckProtocolVersionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to check the server's protocol version");
            }

            // Deliberately its own try/catch too — a failed renewal attempt
            // just gets retried next tick (RegistrationWorker's own
            // maintenance loop is the fallback if this agent's certificate
            // is ever lost outright, not this worker's job).
            try
            {
                await CheckCertificateRenewalAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to check/renew this agent's client certificate");
            }

            // Also its own try/catch — this is purely additive trust
            // maintenance (updatewatch2-server#6): a failure here changes
            // nothing about this agent's current, still-working trust, it
            // just retries picking up a pending root next tick.
            try
            {
                await CheckCaTrustRefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh this agent's trusted CA root bundle");
            }

            // Also its own try/catch — purely local housekeeping with no
            // server round-trip involved, so a failure here can't affect
            // anything else this tick does; it just retries next tick.
            try
            {
                selfUpdateStagingCleaner.CleanupOldFiles(TimeSpan.FromDays(options.SelfUpdateStagingRetentionDays));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up old self-update packages");
            }

            await Task.Delay(TimeSpan.FromMinutes(options.AliveIntervalMinutes), stoppingToken);
        }
    }

    private async Task HandleAliveAsync(CancellationToken ct)
    {
        var result = await serverClient.SendAliveAsync(ct);
        if (result.Outcome != AliveOutcome.CertificateRejected)
        {
            _consecutiveCertificateRejections = 0;

            if (result.Outcome == AliveOutcome.Success)
            {
                if (result.InstallRequested)
                {
                    await HandleInstallRequestAsync(ct);
                }

                if (result.AgentUpdateAvailable is not null)
                {
                    await HandleSelfUpdateAsync(result.AgentUpdateAvailable, ct);
                }

                if (result.CertificateRotationPending)
                {
                    await HandleCertificateRotationRenewalAsync(ct);
                }
            }

            return;
        }

        _consecutiveCertificateRejections++;
        if (_consecutiveCertificateRejections < CertificateRejectionThreshold)
        {
            return;
        }

        SelfHealRejectedCertificate();
        _consecutiveCertificateRejections = 0;
    }

    /// <summary>
    /// Invoked inline on the heartbeat's own tick, not fired off onto a
    /// background <see cref="Task"/> — safe today only because every
    /// <see cref="IUpdateChecker.InstallAsync"/> implementation is still a
    /// placeholder that returns near-instantly (the same honesty caveat
    /// <c>WindowsUpdateChecker.CheckAsync</c> already carries). A future
    /// real WUApiLib-backed implementation that can genuinely take minutes
    /// would need to move this off the heartbeat's own await chain, or
    /// every other heartbeat responsibility (renewal checks, self-heal,
    /// the next tick's own alive call) would be delayed behind it for as
    /// long as the install takes — not done here since nothing in this
    /// codebase can actually take that long yet.
    /// </summary>
    private async Task HandleInstallRequestAsync(CancellationToken ct)
    {
        logger.LogInformation("Server requested an install — invoking the update installer.");

        WireInstallOutcome outcome;
        try
        {
            var checkerOutcome = await updateChecker.InstallAsync(ct);
            outcome = checkerOutcome == CheckerInstallOutcome.Succeeded ? WireInstallOutcome.Succeeded : WireInstallOutcome.Failed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Install failed");
            outcome = WireInstallOutcome.Failed;
        }

        try
        {
            await serverClient.AcknowledgeInstallAsync(outcome, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Self-resolving: the server keeps reporting this install as
            // pending until an acknowledgement actually lands, so the next
            // heartbeat tick just retries the whole thing (including a
            // redundant but harmless re-install) rather than needing its
            // own dedicated retry logic here.
            logger.LogWarning(ex, "Failed to acknowledge the install outcome to the server — it will keep reporting the install as pending.");
        }
    }

    /// <summary>
    /// Invoked inline on the heartbeat's own tick, same as
    /// <see cref="HandleInstallRequestAsync"/> — a fresh agent release is
    /// just as time-sensitive as a manually-triggered install, not
    /// something to defer to <see cref="UpdateCheckWorker"/>'s coarser
    /// cadence. No acknowledgement call back to the server: see
    /// <see cref="IAgentSelfUpdater"/>'s doc comment for why the offer
    /// naturally stops being sent once this agent's next successful
    /// heartbeat (after whatever <see cref="SelfUpdateOutcome.Applied"/>
    /// actually triggers — a service restart on either platform) reports
    /// its new <c>AgentVersion</c>.
    /// </summary>
    private async Task HandleSelfUpdateAsync(AgentUpdateOffer offer, CancellationToken ct)
    {
        try
        {
            var outcome = await selfUpdater.ApplyAsync(offer, ct);
            switch (outcome)
            {
                case SelfUpdateOutcome.Applied:
                    logger.LogInformation(
                        "Applied a self-update to agent version {Version} — a platform-specific restart to pick it up should follow shortly.",
                        offer.Version);
                    break;
                case SelfUpdateOutcome.NotApplicable:
                    break;
                default:
                    logger.LogWarning("Self-update to agent version {Version} did not succeed: {Outcome}", offer.Version, outcome);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Self-update failed unexpectedly.");
        }
    }

    private void SelfHealRejectedCertificate()
    {
        var current = certificateStore.Load();
        if (current is null)
        {
            // Already gone by some other path — nothing left to clean up;
            // RegistrationWorker's maintenance loop already owns recovery
            // from here.
            return;
        }

        logger.LogWarning(
            "This agent's client certificate was rejected {Threshold} times in a row — the server no longer trusts it " +
            "(e.g. an admin reissued it via updatewatch2-server#8 while this agent kept running). Dropping the local " +
            "certificate so RegistrationWorker's maintenance loop recovers it the same way it recovers a genuinely " +
            "lost certificate.",
            CertificateRejectionThreshold);

        var thumbprint = current.GetCertHashString(HashAlgorithmName.SHA256);
        certificateStore.Delete(thumbprint);

        // Not Add-alongside — an already-rejected certificate has no
        // business staying attached to the handler at all.
        sharedHttpHandler.SslOptions.ClientCertificates!.Clear();
    }

    private async Task CheckProtocolVersionAsync(CancellationToken ct)
    {
        var version = await serverClient.FetchVersionAsync(ct);
        if (version.Protocol != Protocol.ProtocolVersion.Current)
        {
            // Logged every tick it's still mismatched, not just once — an
            // admin watching this agent's log should see it stays
            // relevant until actually fixed (matching how the server's
            // mail-unreachable admin warning stays live rather than
            // one-shot), not just a message that scrolled by once.
            logger.LogWarning(
                "Protocol version mismatch: this agent is on {AgentProtocolVersion}, the server is on {ServerProtocolVersion}. " +
                "Update the agent and/or server so they match.",
                Protocol.ProtocolVersion.Current, version.Protocol);
        }
    }

    private async Task CheckCertificateRenewalAsync(CancellationToken ct)
    {
        var current = certificateStore.Load();
        if (current is null)
        {
            // Nothing to renew — RegistrationWorker's maintenance loop owns
            // recovering a missing certificate, not this worker.
            return;
        }

        if (current.NotAfter - DateTime.UtcNow > TimeSpan.FromDays(options.CertificateRenewalLeadTimeDays))
        {
            return;
        }

        await RenewClientCertificateAsync(current, ct);
    }

    /// <summary>
    /// Invoked inline on the heartbeat's own tick, same as
    /// <see cref="HandleInstallRequestAsync"/>/<see cref="HandleSelfUpdateAsync"/>
    /// — a CA root rotation activating (updatewatch2-server#6) is just as
    /// time-sensitive as those, since this agent's leaf keeps chaining to a
    /// root that could be retired at any point. Reuses the exact same
    /// renew-and-hot-swap logic <see cref="CheckCertificateRenewalAsync"/>
    /// already has for its own expiry-lead-time trigger, just without
    /// waiting for that window — see <see cref="RenewClientCertificateAsync"/>.
    /// No acknowledgement call back to the server: this is self-correcting,
    /// same reasoning as <see cref="HandleSelfUpdateAsync"/> — once renewed,
    /// this agent's next heartbeat naturally reports
    /// <c>certificateRotationPending = false</c> on its own, since the
    /// server computes it fresh every time from the stored issuing root.
    /// </summary>
    private async Task HandleCertificateRotationRenewalAsync(CancellationToken ct)
    {
        var current = certificateStore.Load();
        if (current is null)
        {
            // Shouldn't normally happen — this heartbeat only reached this
            // agent's server-side record at all because it authenticated
            // with a certificate a moment ago — but RegistrationWorker
            // owns recovering a genuinely missing certificate either way.
            return;
        }

        logger.LogInformation(
            "Server reports this agent's client certificate was issued under a CA root that's no longer current " +
            "— renewing immediately instead of waiting for the normal expiry-based schedule.");
        await RenewClientCertificateAsync(current, ct);
    }

    private async Task RenewClientCertificateAsync(X509Certificate2 current, CancellationToken ct)
    {
        var result = await serverClient.RenewCertificateAsync(ct);
        if (!result.Success || result.Certificate is null)
        {
            logger.LogWarning("Client certificate renewal request failed — will retry next heartbeat.");
            return;
        }

        var previousThumbprint = current.GetCertHashString(HashAlgorithmName.SHA256);
        certificateStore.Save(Convert.FromBase64String(result.Certificate));
        certificateStore.Delete(previousThumbprint);

        var refreshed = certificateStore.Load()
            ?? throw new InvalidOperationException("Renewed client certificate was just saved but could not be reloaded.");

        // Clear then Add — not Add alongside the old one, which would leave
        // two certificates simultaneously eligible for TLS client-cert
        // selection, both chaining to the same pinned CA, with no
        // guarantee which one a new connection actually presents.
        sharedHttpHandler.SslOptions.ClientCertificates!.Clear();
        sharedHttpHandler.SslOptions.ClientCertificates!.Add(refreshed);
        logger.LogInformation("Client certificate renewed — new expiry {NotAfter}", refreshed.NotAfter);
    }

    /// <summary>
    /// Pre-fetches every root the server's CA currently knows about
    /// (updatewatch2-server#6) and adds any this agent doesn't already
    /// trust — purely additive, never removes a root, so this can never
    /// make this agent's own trust worse, only more current ahead of an
    /// eventual rotation activation. Piggybacked on the heartbeat cadence
    /// like every other maintenance check here, not a one-time startup
    /// check, so a root an admin prepares at any point during this agent's
    /// lifetime gets picked up without a restart.
    /// </summary>
    private async Task CheckCaTrustRefreshAsync(CancellationToken ct)
    {
        var bundle = await serverClient.FetchCaCertificateBundleAsync(ct);
        if (caTrustStore.MergeAdditional(bundle))
        {
            logger.LogInformation("Trusted at least one newly published CA root ahead of an eventual rotation.");
        }
    }
}
