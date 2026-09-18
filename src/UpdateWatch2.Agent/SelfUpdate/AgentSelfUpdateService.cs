using System.Security.Cryptography;
using UpdateWatch2.Agent.Communication;
using UpdateWatch2.Agent.Protocol;

namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// The testable half of self-update (updatewatch2-agent#14) — decides
/// whether an offer is actually worth acting on, downloads and SHA-256-
/// verifies the right asset for this platform, and only then hands off to
/// the untestable, OS-specific <see cref="IPlatformUpdateApplier"/>. See
/// <see cref="IAgentSelfUpdater"/>'s doc comment for why this split
/// mirrors <c>WindowsUpdateChecker</c>/<c>LinuxUpdateChecker</c>'s existing
/// shape.
/// </summary>
/// <param name="assetKind">Which of an offer's asset slots applies to this platform — see <see cref="AgentUpdateAssetKind"/>.</param>
/// <param name="assetArch">This agent's own architecture — combined with <paramref name="assetKind"/> to pick exactly one of the offer's six slots. See <see cref="AgentUpdateAssetArch"/>.</param>
/// <param name="stagingDirectory">Where a downloaded artifact is written before being handed to <paramref name="applier"/>. Created if missing.</param>
public class AgentSelfUpdateService(
    AgentUpdateAssetKind assetKind,
    AgentUpdateAssetArch assetArch,
    string stagingDirectory,
    IServerClient serverClient,
    IPlatformUpdateApplier applier,
    ILogger<AgentSelfUpdateService> logger) : IAgentSelfUpdater
{
    public async Task<SelfUpdateOutcome> ApplyAsync(AgentUpdateOffer? offer, CancellationToken ct = default)
    {
        if (offer is null)
        {
            return SelfUpdateOutcome.NotApplicable;
        }

        // Defense in depth, not redundant paranoia: the server already
        // gates this in IAgentUpdateService.GetOfferForAsync, but this
        // agent deciding for itself means a future server bug (or a
        // stale/malformed offer somehow reaching this far) can never make
        // it downgrade or reinstall its own current version.
        if (!IsNewerThanCurrentVersion(offer.Version))
        {
            logger.LogWarning(
                "Server offered agent version {OfferedVersion}, which is not newer than this agent's own {CurrentVersion} — ignoring.",
                offer.Version, AgentVersion.Current);
            return SelfUpdateOutcome.NotApplicable;
        }

        var asset = SelectAsset(offer);
        if (asset is null)
        {
            logger.LogWarning(
                "Agent release {Version} has no asset for this platform ({AssetKind}, {AssetArch}) — nothing to self-update to yet.",
                offer.Version, assetKind, assetArch);
            return SelfUpdateOutcome.NotApplicable;
        }

        Directory.CreateDirectory(stagingDirectory);

        // Security review finding, hardened further after an automated
        // follow-up review found the first fix's approach was still
        // fragile: the naive Split('/').Last() this used to start from ran
        // BEFORE UnescapeDataString, so a "/" or ".." percent-encoded
        // inside the offered filename (e.g. "%2Fetc%2Fsystemd%2F...")
        // survived the split untouched and only became a real path
        // separator afterward — and Path.Combine discards stagingDirectory
        // entirely if the resulting "filename" turns out to be rooted.
        // Path.GetFileName strips any directory component AFTER decoding,
        // so the LOCAL write path can never escape stagingDirectory
        // regardless of what the offered filename decodes to.
        //
        // The REMOTE fetch target needed the same treatment, and a
        // blacklist-style check on the original DownloadUrl string (reject
        // it if it looks like an absolute URL with a host) turned out to
        // be the wrong shape of fix — it's inherently a game of finding
        // every URL-parsing edge case (a protocol-relative "//host/path"
        // reference resolves against a base URI by replacing just the
        // authority and keeping the scheme, confirmed by hand; backslash
        // variants some parsers normalize to "/"; ...) rather than making
        // the bypass structurally impossible. So this now never fetches
        // asset.DownloadUrl itself at all: AgentApiRoutes.UpdateDownload
        // rebuilds the exact same route template from ONLY the
        // already-sanitized bare filename below — the fetch target is
        // same-origin-relative by construction, with no check left to
        // bypass.
        var rawFileName = Uri.UnescapeDataString(asset.DownloadUrl.Split('/').Last());
        var fileName = Path.GetFileName(rawFileName);
        if (string.IsNullOrEmpty(fileName))
        {
            logger.LogError("Agent update {Version}'s offered download URL has no usable filename — refusing to apply it.", offer.Version);
            return SelfUpdateOutcome.DownloadFailed;
        }

        var destinationPath = Path.Combine(stagingDirectory, fileName);
        var fullStagingDirectory = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(destinationPath).StartsWith(fullStagingDirectory, StringComparison.Ordinal))
        {
            logger.LogError("Agent update {Version}'s resolved download path escaped the staging directory — refusing to apply it.", offer.Version);
            return SelfUpdateOutcome.DownloadFailed;
        }

        try
        {
            await serverClient.DownloadFileAsync(AgentApiRoutes.UpdateDownload(fileName), destinationPath, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning(ex, "Failed to download agent update {Version}.", offer.Version);
            return SelfUpdateOutcome.DownloadFailed;
        }

        if (!MatchesExpectedChecksum(destinationPath, asset.Sha256))
        {
            logger.LogError(
                "Downloaded agent update {Version} failed its SHA-256 integrity check — refusing to apply it.",
                offer.Version);
            TryDelete(destinationPath);
            return SelfUpdateOutcome.IntegrityCheckFailed;
        }

        logger.LogInformation("Downloaded and verified agent update {Version} — applying it.", offer.Version);
        try
        {
            var applied = await applier.ApplyAsync(destinationPath, ct);
            return applied ? SelfUpdateOutcome.Applied : SelfUpdateOutcome.ApplyFailed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to apply agent update {Version}.", offer.Version);
            return SelfUpdateOutcome.ApplyFailed;
        }
    }

    private AgentUpdateAssetOffer? SelectAsset(AgentUpdateOffer offer) => (assetKind, assetArch) switch
    {
        (AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.X64) => offer.WindowsInstallerX64,
        (AgentUpdateAssetKind.WindowsInstaller, AgentUpdateAssetArch.Arm64) => offer.WindowsInstallerArm64,
        (AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.X64) => offer.LinuxDebX64,
        (AgentUpdateAssetKind.LinuxDeb, AgentUpdateAssetArch.Arm64) => offer.LinuxDebArm64,
        (AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.X64) => offer.LinuxRpmX64,
        (AgentUpdateAssetKind.LinuxRpm, AgentUpdateAssetArch.Arm64) => offer.LinuxRpmArm64,
        _ => null,
    };

    private static bool IsNewerThanCurrentVersion(string offeredVersion) =>
        Version.TryParse(offeredVersion, out var offered)
        && Version.TryParse(AgentVersion.Current, out var current)
        && offered > current;

    private static bool MatchesExpectedChecksum(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a leftover rejected download in the
            // staging directory is harmless (never applied, overwritten by
            // the next attempt) and not worth failing this call over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
