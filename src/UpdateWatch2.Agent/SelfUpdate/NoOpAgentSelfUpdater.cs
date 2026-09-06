using UpdateWatch2.Agent.Communication;

namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// Used when this Linux host has neither <c>apt</c> nor <c>dnf</c>/<c>yum</c>
/// (<c>LinuxPackageManagerDetector.Detect()</c> returns <c>None</c>) — the
/// same situation <c>NoOpUpdateChecker</c> already covers for OS-update
/// checking. Without a known package manager there's no safe way to know
/// which package format (if either) this host could even install, so
/// self-update is simply never attempted rather than guessed at.
/// </summary>
public class NoOpAgentSelfUpdater(ILogger<NoOpAgentSelfUpdater> logger) : IAgentSelfUpdater
{
    private bool _hasWarnedOnce;

    public Task<SelfUpdateOutcome> ApplyAsync(AgentUpdateOffer? offer, CancellationToken ct = default)
    {
        if (offer is not null && !_hasWarnedOnce)
        {
            // Once, not every tick — an admin watching this agent's log
            // already knows nothing will happen after the first warning;
            // repeating it every heartbeat would just be noise.
            _hasWarnedOnce = true;
            logger.LogWarning(
                "Server offered agent version {Version}, but this host has no known package manager to self-update with — ignoring.",
                offer.Version);
        }

        return Task.FromResult(SelfUpdateOutcome.NotApplicable);
    }
}
