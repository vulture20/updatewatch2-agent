using System.Security.Cryptography;
using UpdateWatch2.Agent.Communication;

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
/// <param name="stagingDirectory">Where a downloaded artifact is written before being handed to <paramref name="applier"/>. Created if missing.</param>
public class AgentSelfUpdateService(
    AgentUpdateAssetKind assetKind,
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
                "Agent release {Version} has no asset for this platform ({AssetKind}) — nothing to self-update to yet.",
                offer.Version, assetKind);
            return SelfUpdateOutcome.NotApplicable;
        }

        // Defense in depth: a legitimate offer's DownloadUrl is always a
        // same-server-relative path (AgentUpdateAssetOffer's own doc
        // comment) — never trust one naming a foreign host, which
        // HttpClient would otherwise happily fetch from, bypassing the
        // pinned-server TLS relationship entirely. Checking Uri.IsAbsoluteUri
        // alone is NOT enough: .NET parses a leading-slash relative path
        // like "/api/agent/updates/x.exe" as a well-formed absolute "file"
        // URI with an EMPTY Host, so a real foreign-host URL (which always
        // has a non-empty Host) is what actually needs rejecting here.
        if (Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var parsedUrl) && !string.IsNullOrEmpty(parsedUrl.Host))
        {
            logger.LogError("Agent update {Version}'s offered download URL is not a relative, same-server path — refusing to fetch it.", offer.Version);
            return SelfUpdateOutcome.DownloadFailed;
        }

        Directory.CreateDirectory(stagingDirectory);

        // Security review finding: the naive Split('/').Last() below runs
        // BEFORE UnescapeDataString, so a "/" or ".." percent-encoded
        // inside the offered filename (e.g. "%2Fetc%2Fsystemd%2F...")
        // survives the split untouched and only becomes a real path
        // separator afterward — and Path.Combine discards stagingDirectory
        // entirely if the resulting "filename" turns out to be rooted.
        // Path.GetFileName strips any directory component AFTER decoding,
        // so this can never resolve outside stagingDirectory regardless of
        // what the server-reported DownloadUrl's last segment decodes to —
        // defense in depth even though the server is expected to always
        // hand back one of its own, already-sanitized filenames.
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
            await serverClient.DownloadFileAsync(asset.DownloadUrl, destinationPath, ct);
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

    private AgentUpdateAssetOffer? SelectAsset(AgentUpdateOffer offer) => assetKind switch
    {
        AgentUpdateAssetKind.WindowsInstaller => offer.WindowsInstaller,
        AgentUpdateAssetKind.LinuxDeb => offer.LinuxDeb,
        AgentUpdateAssetKind.LinuxRpm => offer.LinuxRpm,
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
