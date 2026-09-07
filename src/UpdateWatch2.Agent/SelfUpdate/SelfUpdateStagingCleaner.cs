namespace UpdateWatch2.Agent.SelfUpdate;

/// <summary>
/// Periodically deletes old downloaded self-update packages from the
/// staging directory <see cref="AgentSelfUpdateService"/> downloads into —
/// that class only ever cleans up a download that failed its integrity
/// check (see <c>MatchesExpectedChecksum</c>), never one it actually
/// applied, successfully or not, so every self-update this agent has ever
/// gone through would otherwise leave its installer/<c>.deb</c>/<c>.rpm</c>
/// behind indefinitely.
///
/// <para>
/// Deliberately platform-agnostic — unlike <see cref="IPlatformUpdateApplier"/>,
/// this is plain file-age bookkeeping with nothing OS-specific about it,
/// so (unlike that interface) there's no separate Windows/Linux
/// implementation and no untestable half: this class has real unit test
/// coverage against a temp directory.
/// </para>
///
/// <para>
/// Always keeps the single most-recently-modified file in the directory,
/// no matter how old it is, even if that means keeping a file older than
/// <paramref name="retentionPeriod"/> — an admin poking around this
/// directory should always find at least one real example of what this
/// agent last tried to install, including on a host that hasn't
/// self-updated in a very long time (per the user's own explicit
/// requirement when this was requested, not an incidental side effect).
/// </para>
/// </summary>
public class SelfUpdateStagingCleaner(string stagingDirectory, ILogger<SelfUpdateStagingCleaner> logger)
{
    public void CleanupOldFiles(TimeSpan retentionPeriod)
    {
        if (!Directory.Exists(stagingDirectory))
        {
            return;
        }

        var files = new DirectoryInfo(stagingDirectory).GetFiles();
        if (files.Length <= 1)
        {
            // Nothing to ever delete without going below "keep at least
            // one" — covers both "empty directory" and "exactly the one
            // file we'd keep anyway" without a separate check.
            return;
        }

        var mostRecent = files.OrderByDescending(f => f.LastWriteTimeUtc).First();
        var cutoffUtc = DateTime.UtcNow - retentionPeriod;

        foreach (var file in files)
        {
            if (file == mostRecent || file.LastWriteTimeUtc >= cutoffUtc)
            {
                continue;
            }

            try
            {
                file.Delete();
                logger.LogInformation(
                    "Deleted old self-update package {FileName} (last modified {LastWriteTimeUtc:u}).",
                    file.Name, file.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort — a file still locked by something else (or a
                // permissions hiccup) just gets retried on the next check;
                // not worth failing the whole cleanup pass over one file.
                logger.LogWarning(ex, "Failed to delete old self-update package {FileName} — will retry later.", file.Name);
            }
        }
    }
}
