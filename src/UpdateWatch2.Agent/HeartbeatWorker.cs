using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UpdateWatch2.Agent.Certificates;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.Reboot;
using UpdateWatch2.Agent.SelfUpdate;
using UpdateWatch2.Agent.UpdateCheck;
using CheckerInstallOutcome = UpdateWatch2.Agent.UpdateCheck.InstallOutcome;
using WireInstallOutcome = UpdateWatch2.Agent.Communication.InstallOutcome;
using WireRebootOutcome = UpdateWatch2.Agent.Communication.RebootOutcome;

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
/// renewal instead of relying solely on the lead-time check above), and
/// whether an admin has remote-triggered a reboot of this agent's own
/// machine (distinct from an update install, which per CLAUDE.md's rule
/// never triggers a reboot itself — see <see cref="Reboot.IAgentRebooter"/>),
/// and —
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
/// present at this agent's own last startup. Also, before any of the
/// above, checks whether <see cref="AgentOptions.HostnameOverride"/> now
/// disagrees with this agent's already-issued certificate — see
/// <see cref="TryDetectHostnameOverrideMismatch"/> — and if so, skips the
/// alive/renewal calls for that tick entirely rather than letting them
/// fail against the server's own hostname/certificate-identity check.
/// </summary>
public class HeartbeatWorker(
    AgentOptions options,
    IServerClient serverClient,
    IAgentCertificateState certificateState,
    IClientCertificateStore certificateStore,
    FileCaTrustStore caTrustStore,
    SocketsHttpHandler sharedHttpHandler,
    IUpdateChecker updateChecker,
    IUpdateCheckTrigger updateCheckTrigger,
    IAgentSelfUpdater selfUpdater,
    SelfUpdateStagingCleaner selfUpdateStagingCleaner,
    IRegistrationWakeSignal wakeSignal,
    IAgentRebooter agentRebooter,
    IPreDownloadPolicyState preDownloadPolicyState,
    IAgentConfigStore configStore,
    ILogLevelState logLevelState,
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
            // AgentOptions.HostnameOverride changing (or being set for the
            // first time) on an agent that already holds a client
            // certificate is a real, deliberate footgun the server enforces
            // strictly: alive/renew/reboot-ack all reject a request whose
            // URL hostname doesn't match the identity baked into the
            // presented certificate's Subject at issuance. Detected LOCALLY
            // here — comparing the loaded certificate's own Subject against
            // the currently-effective hostname — rather than by reacting to
            // the resulting 401/403, which is indistinguishable at the HTTP
            // level from a genuine certificate rejection and would
            // otherwise silently trip this worker's own self-heal (dropping
            // the certificate and re-registering under the new hostname,
            // leaving the OLD Agent row orphaned server-side). At the
            // user's explicit request, this case is surfaced, not acted on
            // automatically — see AgentOptions.HostnameOverride's own doc
            // comment for the full reasoning and the manual recovery steps.
            if (TryDetectHostnameOverrideMismatch(out var certifiedHostname, out var effectiveHostname))
            {
                // Logged every tick it persists, not just once — matching
                // AllowUnauthenticatedPackages' own "warn on every relevant
                // call while the condition holds" convention — this is an
                // ongoing problem that needs an admin's own action to
                // resolve, not something this loop will recover from on its
                // own.
                logger.LogWarning(
                    "This agent's client certificate was issued for hostname '{CertifiedHostname}', but the " +
                    "currently configured hostname is '{EffectiveHostname}' (AgentOptions.HostnameOverride) — every " +
                    "authenticated server call would be rejected, so none are attempted this tick. To register " +
                    "fresh under the new hostname: delete the '{CertifiedHostname}' entry on the server and remove " +
                    "this agent's local client certificate.",
                    certifiedHostname, effectiveHostname, certifiedHostname);
            }
            else
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

                // Deliberately its own try/catch too — a failed renewal
                // attempt just gets retried next tick (RegistrationWorker's
                // own maintenance loop is the fallback if this agent's
                // certificate is ever lost outright, not this worker's job).
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
            }

            // Deliberately its own try/catch, independent of the alive
            // call above, and NOT gated by the hostname-mismatch check —
            // this hits the server's anonymous /api/version endpoint, so a
            // mismatched identity doesn't affect it either way.
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
        var rebootRequired = await CheckRebootRequiredAsync(ct);
        var result = await serverClient.SendAliveAsync(
            rebootRequired, options.LogLevel, options.UpdateCheckIntervalMinutes, options.UpdateCheckJitterSeconds,
            options.AliveIntervalMinutes, ct);
        if (result.Outcome != AliveOutcome.CertificateRejected)
        {
            _consecutiveCertificateRejections = 0;

            if (result.Outcome == AliveOutcome.Success)
            {
                // Unconditional, not just when true — so the server turning
                // this off also propagates within one heartbeat interval,
                // not only turning it on. UpdateCheckWorker reads this on
                // its own, much coarser cadence (see IPreDownloadPolicyState's
                // own doc comment for why it isn't itself polled here).
                // Every heartbeat response carries BOTH the Windows and the
                // Linux toggle regardless of which platform this agent
                // actually is (agent v1.0.16) — IPreDownloadPolicyState
                // itself stays a single platform-agnostic bool, so the
                // platform selection happens right here, once, the same
                // OperatingSystem.IsWindows() check OperatingSystemDescriber
                // already uses elsewhere in this codebase.
                preDownloadPolicyState.Update(
                    OperatingSystem.IsWindows() ? result.PreDownloadWindowsUpdatesEnabled : result.PreDownloadLinuxUpdatesEnabled);

                ApplyPushedSettings(result);

                if (result.InstallRequested)
                {
                    await HandleInstallRequestAsync(result.InstallUpdateIds, ct);
                }

                if (result.AgentUpdateAvailable is not null)
                {
                    await HandleSelfUpdateAsync(result.AgentUpdateAvailable, ct);
                }

                if (result.CertificateRotationPending)
                {
                    await HandleCertificateRotationRenewalAsync(ct);
                }

                // Last, deliberately: a successful reboot trigger ends
                // this very process shortly after, so anything else this
                // tick has to do (install, self-update, cert renewal) runs
                // first while there's still a process left to do it in.
                if (result.RebootRequested)
                {
                    await HandleRebootRequestAsync(ct);
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
    /// True when this agent already holds a client certificate, but that
    /// certificate's own Subject (its Common Name, set to the hostname it
    /// was issued for) no longer matches <see cref="AgentOptions.ResolveHostname"/>'s
    /// current result — i.e. <see cref="AgentOptions.HostnameOverride"/> was
    /// set or changed after this certificate was already issued. See
    /// <see cref="AgentOptions.HostnameOverride"/>'s own doc comment for why
    /// this is detected locally rather than by reacting to the server's
    /// resulting 401/403. False (nothing to detect) when there's no local
    /// certificate at all — that's RegistrationWorker's own, unrelated
    /// lost-certificate recovery case.
    /// </summary>
    private bool TryDetectHostnameOverrideMismatch(out string? certifiedHostname, out string effectiveHostname)
    {
        effectiveHostname = options.ResolveHostname();
        certifiedHostname = certificateStore.Load()?.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return certifiedHostname is not null && !string.Equals(certifiedHostname, effectiveHostname, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fresh, lightweight check of whether a reboot is currently
    /// required, run on every heartbeat tick rather than waiting for
    /// <see cref="UpdateCheckWorker"/>'s much coarser cadence — at the
    /// user's explicit request ("Der Check, ob ein Neustart nötig ist,
    /// sollte öfter stattfinden."). Cheap on every platform (see
    /// <see cref="UpdateCheck.IUpdateChecker.CheckRebootRequiredAsync"/>'s
    /// own doc comment) — what made this impractical before was only ever
    /// riding along behind the full, much slower update search, not the
    /// check itself. Its own try/catch, separate from <see cref="HandleAliveAsync"/>'s
    /// outer one in <see cref="ExecuteAsync"/>: a failure here must not
    /// prevent the alive call itself from going out — it just means no
    /// fresh value this tick, returned as null rather than a false
    /// negative (never risk overwriting the server's last-known-good
    /// <c>Agent.RebootRequired</c> with a wrong "not needed"). <see cref="UpdateCheck.IUpdateChecker.CheckRebootRequiredAsync"/>
    /// already catches its own exceptions internally (mirroring
    /// <c>CheckAsync</c>/<c>InstallAsync</c>/<c>PreDownloadAsync</c>), so
    /// this catch is belt-and-suspenders, not the primary safety net.
    /// </summary>
    private async Task<bool?> CheckRebootRequiredAsync(CancellationToken ct)
    {
        try
        {
            var result = await updateChecker.CheckRebootRequiredAsync(ct);
            return result.Success ? result.RebootRequired : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check whether a reboot is required");
            return null;
        }
    }

    /// <summary>
    /// Applies any admin-set per-agent override the server just sent back
    /// (<see cref="AliveResult.DesiredLogLevel"/> and its siblings) —
    /// unconditionally, whenever non-null and different from what's
    /// currently live, which is what implements "the server always wins on
    /// conflict" (CLAUDE.md, at the user's explicit request): even a value
    /// a human just hand-edited into the local registry/config file gets
    /// overwritten on the very next heartbeat. A null field means no
    /// override for that setting — nothing here touches it, leaving the
    /// local file authoritative. Mutates the already-injected
    /// <see cref="AgentOptions"/> singleton in place — the same instance
    /// <see cref="UpdateCheckWorker.NextDelay"/> reads fresh on every loop
    /// iteration, so an interval/jitter change is live-applied for free,
    /// and the same instance <see cref="ServerClient.SendAliveAsync"/> reads
    /// on every future heartbeat, so this agent's own next report reflects
    /// the change immediately. A LogLevel change additionally goes through
    /// <see cref="ILogLevelState"/> so it takes effect on this process's
    /// actual logging output right away — see <c>Program.cs</c>'s own
    /// comments for that mechanism. The persistence write (only, not the
    /// in-memory mutation, which can't fail) gets its own try/catch, non-
    /// fatal — a disk/registry write failure still leaves the live value
    /// applied, it just won't survive a restart until it succeeds on a
    /// later tick.
    /// </summary>
    private void ApplyPushedSettings(AliveResult result)
    {
        // A pushed alive-heartbeat-interval change (agent v1.0.15, at the
        // user's explicit request) is live-applied for free the same way
        // the interval/jitter fields below already are: this very worker's
        // ExecuteAsync loop reads options.AliveIntervalMinutes fresh for
        // its own Task.Delay right after this method returns on the same
        // tick, so no extra plumbing is needed to wire it up.
        var changed = false;

        if (result.DesiredLogLevel is not null && !string.Equals(result.DesiredLogLevel, options.LogLevel, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Server-pushed LogLevel change: {OldLogLevel} -> {NewLogLevel}", options.LogLevel, result.DesiredLogLevel);
            options.LogLevel = result.DesiredLogLevel;
            logLevelState.Update(result.DesiredLogLevel);
            changed = true;
        }

        if (result.DesiredUpdateCheckIntervalMinutes is not null && result.DesiredUpdateCheckIntervalMinutes != options.UpdateCheckIntervalMinutes)
        {
            options.UpdateCheckIntervalMinutes = result.DesiredUpdateCheckIntervalMinutes.Value;
            changed = true;
        }

        if (result.DesiredUpdateCheckJitterSeconds is not null && result.DesiredUpdateCheckJitterSeconds != options.UpdateCheckJitterSeconds)
        {
            options.UpdateCheckJitterSeconds = result.DesiredUpdateCheckJitterSeconds.Value;
            changed = true;
        }

        if (result.DesiredAliveIntervalMinutes is not null && result.DesiredAliveIntervalMinutes != options.AliveIntervalMinutes)
        {
            options.AliveIntervalMinutes = result.DesiredAliveIntervalMinutes.Value;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        try
        {
            logger.LogDebug("Writing server-pushed config change to the local config store (registry/config file).");
            configStore.Save(options);
            logger.LogDebug("Wrote server-pushed config change to the local config store.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Applied a server-pushed settings change live, but failed to persist it to the local config store.");
        }
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
    private async Task HandleInstallRequestAsync(IReadOnlyList<string>? updateIds, CancellationToken ct)
    {
        if (updateIds is null)
        {
            logger.LogInformation("Server requested an install of everything pending — invoking the update installer.");
        }
        else
        {
            logger.LogInformation("Server requested an install of {Count} selected update(s) — invoking the update installer.", updateIds.Count);
        }

        WireInstallOutcome outcome;
        string? errorDetail = null;
        try
        {
            var result = await updateChecker.InstallAsync(updateIds, ct);
            outcome = result.Outcome == CheckerInstallOutcome.Succeeded ? WireInstallOutcome.Succeeded : WireInstallOutcome.Failed;
            errorDetail = result.ErrorDetail;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Install failed");
            outcome = WireInstallOutcome.Failed;
            errorDetail = ex.Message;
        }

        if (outcome == WireInstallOutcome.Succeeded)
        {
            // Report the new update state right away — without this, the
            // admin UI's pending-updates list stays stale until
            // UpdateCheckWorker's own next jittered tick, up to
            // UpdateCheckIntervalMinutes (plus jitter) later. Own try/catch:
            // a failure here must not stop the ack below, and the next
            // scheduled UpdateCheckWorker tick is still the fallback either way.
            try
            {
                await updateCheckTrigger.CheckAndReportNowAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh the update list immediately after a successful install — will pick it up on the next scheduled check.");
            }
        }

        try
        {
            await serverClient.AcknowledgeInstallAsync(outcome, Truncate(errorDetail), ct);
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
    /// <see cref="HandleInstallRequestAsync"/> — but unlike that method,
    /// the acknowledgement here is sent BEFORE the machine actually goes
    /// down, not after: <see cref="IAgentRebooter.RequestReboot"/> only
    /// SCHEDULES the platform's reboot command and returns almost
    /// immediately (see that interface's own doc comment), but the whole
    /// point of a successful call is that this machine reboots shortly
    /// after — there is no "after" left in which to still reach the
    /// server once that has happened.
    /// </summary>
    private async Task HandleRebootRequestAsync(CancellationToken ct)
    {
        logger.LogInformation("Server requested a reboot of this agent's own machine.");

        WireRebootOutcome outcome;
        string? errorDetail = null;
        try
        {
            agentRebooter.RequestReboot();
            outcome = WireRebootOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule a machine reboot");
            outcome = WireRebootOutcome.Failed;
            errorDetail = ex.Message;
        }

        try
        {
            await serverClient.AcknowledgeRebootAsync(outcome, Truncate(errorDetail), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same accepted trade-off as HandleInstallRequestAsync's own
            // ack failure handling: the server keeps reporting the reboot
            // as pending until an acknowledgement lands, so either this
            // same process retries next tick (a Failed trigger) or — for a
            // Succeeded one — this agent's first heartbeat after coming
            // back up could see it again and reboot a second time.
            logger.LogWarning(ex, "Failed to acknowledge the reboot outcome to the server.");
        }
    }

    // A raw apt-get/dnf stderr blob or a chained exception message could in
    // principle be enormous (a very verbose package manager failure, or a
    // long inner-exception chain) — capped before it ever leaves this agent
    // so neither the wire payload nor the server's stored column grows
    // unboundedly from a single bad install attempt. Comfortably longer
    // than any realistic single-line apt-get/dnf error, which is the only
    // thing this has actually been observed to carry in production.
    private const int MaxErrorDetailLength = 2000;

    private static string? Truncate(string? detail) =>
        detail is null || detail.Length <= MaxErrorDetailLength
            ? detail
            : detail[..MaxErrorDetailLength] + "…";

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
            // Already gone by some other path (e.g. deleted outside this
            // agent's own code — manual cleanup on the host). Nothing left
            // in the local store to clean up, but sharedHttpHandler.SslOptions.ClientCertificates
            // may still be holding the stale in-memory certificate object
            // from whenever it was first attached — deleting a certificate
            // from disk/the OS store never retroactively affects an
            // already-loaded X509Certificate2 instance a handler is
            // holding (documented CLAUDE.md finding for the renewal path;
            // the same is true here). Clear it defensively so this agent
            // stops presenting it, and still wake RegistrationWorker so it
            // re-checks now rather than waiting out its own poll interval.
            sharedHttpHandler.SslOptions.ClientCertificates!.Clear();
            wakeSignal.RequestImmediateCheck();
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

        // Wakes RegistrationWorker immediately instead of it waiting out
        // its own CertificateMaintenanceIntervalSeconds poll (up to 15
        // minutes by default) — see IRegistrationWakeSignal's own doc
        // comment for the real report this fixed.
        wakeSignal.RequestImmediateCheck();
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
