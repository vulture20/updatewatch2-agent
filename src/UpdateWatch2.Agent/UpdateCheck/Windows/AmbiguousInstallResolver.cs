namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// Decides the outcome for the one ambiguous case in
/// <see cref="WuaUpdateSession.DownloadAndInstall"/>: a scoped install
/// (specific KB article IDs requested) where none of them matched the
/// current "not yet installed" WUA search. Extracted as its own pure,
/// testable type — mirroring <c>Linux.AptUpdateSession.BuildInstallArgs</c>'s
/// own convention of pulling the one piece of a COM-/shell-touching,
/// untestable-on-Linux-CI class that doesn't actually need live OS access
/// out into something <c>dotnet test</c> can cover directly.
///
/// <para>
/// <b>The bug this fixes</b>: the original code treated "nothing in the
/// pending search matched the requested KB(s)" as unconditional success.
/// In practice that match can fail to mean "already installed" — the far
/// more common real-world trigger on Windows Server is supersedence (a
/// newer cumulative update replaces the one actually requested, so WUA
/// simply stops offering the old KB number) or a timing gap with Windows'
/// own native Automatic Updates client independently grabbing it in the
/// meantime. Either way, the requested update was never actually installed
/// through this call — but the server trusts a reported <c>Succeeded</c>
/// unconditionally and deletes the corresponding pending entries
/// immediately (<c>UpdateService.AcknowledgeInstallAsync</c> →
/// <c>RemoveJustInstalledItemsAsync</c>, server-side), with no independent
/// re-verification. If the next regular search also doesn't happen to
/// re-offer that exact KB (which supersedence specifically guarantees), the
/// update silently disappears from the admin UI for good — reported in
/// production against real Windows servers, "updates that supposedly
/// installed successfully never came back".
/// </para>
///
/// <para>
/// The fix: before claiming success, a second, explicit "IsInstalled=1"
/// search confirms whether the specific requested KBs are now genuinely
/// installed. Only when every one of them is actually found there is this
/// <see cref="Succeeded"/> — anything else is honestly reported
/// <see cref="InstallOutcome.Failed"/>, naming exactly which KB(s) couldn't
/// be confirmed, so the server neither deletes them nor shows a false
/// "installed" status; a plain Failed also means no immediate re-check is
/// triggered (the existing "only after a Succeeded ack" rule), leaving it
/// to the next regular scheduled scan — the same honest-uncertainty
/// handling this codebase already applies to e.g. a failed update search
/// (<see cref="UpdateCheckResult.Failed"/>) or an inconclusive Linux
/// self-update apply (<c>LinuxPackageApplier</c>'s 128–192 exit-code
/// range).
/// </para>
/// </summary>
public static class AmbiguousInstallResolver
{
    public static InstallResult Resolve(IReadOnlyList<string> requestedPackageIds, IReadOnlySet<string> currentlyInstalledKbIds)
    {
        var unresolved = requestedPackageIds.Where(id => !currentlyInstalledKbIds.Contains(id)).ToList();
        if (unresolved.Count == 0)
        {
            return new InstallResult(InstallOutcome.Succeeded);
        }

        return new InstallResult(
            InstallOutcome.Failed,
            $"{unresolved.Count} of {requestedPackageIds.Count} requested Windows update(s) are no longer offered by Windows Update and could not be confirmed as already installed (likely superseded by a newer update, or applied independently outside this agent): {string.Join(", ", unresolved)}");
    }
}
