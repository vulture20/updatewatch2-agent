using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// Real Windows Update Agent (WUApiLib) integration via late-bound COM
/// (<c>dynamic</c> + <see cref="Type.GetTypeFromProgID(string)"/>), not a
/// compile-time typelib reference (no <c>tlbimp</c>-generated interop
/// assembly, no COM reference in the csproj) — this project is built and
/// tested on Linux (see CLAUDE.md's Commands sections and the CI workflow,
/// both ubuntu-based), where generating one isn't practical. Late binding
/// compiles on any platform and only actually touches COM the moment a
/// method here runs, which only ever happens on a real Windows host.
///
/// <para>
/// <b>Honesty note</b>, matching the standard this codebase holds every
/// other non-obvious Windows/COM/TLS behavior to (see e.g. the mTLS and
/// certificate-renewal notes under CLAUDE.md's "Commands (agent)"): this
/// class has been written against the published WUApiLib object model and
/// compiles, but — unlike almost everything else in this codebase — it has
/// NOT been live-verified against a real Windows Update Agent, because no
/// Windows host was available in the environment this was written in.
/// Re-run a real search-then-install cycle on an actual Windows machine
/// before trusting this in production; treat it as a well-researched first
/// implementation, not a confirmed-working one.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class WuaUpdateSession(ILogger<WuaUpdateSession> logger) : IWindowsUpdateSession
{
    // IsInstalled=0 and IsHidden=0: not yet installed, not hidden by an
    // admin. Type='Software' deliberately excludes driver updates (the
    // other value this criteria language accepts is 'Driver') — Windows
    // Update's own UI treats driver updates as a separate, more cautious
    // category for the same reason: a driver pushed through an unattended
    // channel is a disproportionately common source of regressions
    // compared to a software/security update.
    private const string SearchCriteria = "IsInstalled=0 and IsHidden=0 and Type='Software'";

    public UpdateCheckResult SearchForUpdates(CancellationToken ct)
    {
        dynamic searcher = CreateSession().CreateUpdateSearcher();
        ct.ThrowIfCancellationRequested();
        dynamic found = searcher.Search(SearchCriteria).Updates;

        var updates = new List<DetectedUpdate>();
        int count = found.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic update = found.Item(i);
            updates.Add(new DetectedUpdate(
                Title: (string)update.Title,
                PackageId: FirstKbArticleId(update),
                Description: (string)update.Description));
        }

        return new UpdateCheckResult(updates, RebootRequired: IsRebootRequired());
    }

    public InstallOutcome DownloadAndInstall(CancellationToken ct)
    {
        dynamic session = CreateSession();
        dynamic searcher = session.CreateUpdateSearcher();
        ct.ThrowIfCancellationRequested();
        dynamic pending = searcher.Search(SearchCriteria).Updates;

        int pendingCount = pending.Count;
        if (pendingCount == 0)
        {
            logger.LogInformation("No pending Windows updates to install.");
            return InstallOutcome.Succeeded;
        }

        dynamic toDownload = NewUpdateCollection();
        for (var i = 0; i < pendingCount; i++)
        {
            dynamic update = pending.Item(i);
            if (!(bool)update.EulaAccepted)
            {
                update.AcceptEula();
            }

            toDownload.Add(update);
        }

        ct.ThrowIfCancellationRequested();
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = toDownload;
        dynamic downloadResult = downloader.Download();

        dynamic toInstall = NewUpdateCollection();
        for (var i = 0; i < pendingCount; i++)
        {
            if (IsSuccessCode((int)downloadResult.GetUpdateResult(i).ResultCode))
            {
                toInstall.Add(pending.Item(i));
            }
        }

        int downloadedCount = toInstall.Count;
        if (downloadedCount == 0)
        {
            logger.LogWarning("Windows Update download produced no successfully downloaded updates out of {PendingCount} pending.", pendingCount);
            return InstallOutcome.Failed;
        }

        ct.ThrowIfCancellationRequested();
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = toInstall;
        installer.AllowSourcePrompts = false;
        // installer.RebootRequiredForCompletion / installResult.RebootRequired
        // are deliberately never acted on here — CLAUDE.md's "update
        // installation never triggers a reboot itself" rule. The next
        // SearchForUpdates call reports the same signal independently via
        // IsRebootRequired(), which is what CheckAsync's regular reporting
        // cycle surfaces to the server.
        dynamic installResult = installer.Install();

        int installCode = (int)installResult.ResultCode;
        if (!IsSuccessCode(installCode))
        {
            logger.LogWarning("Windows Update install finished with result code {ResultCode} for {Count} update(s).", installCode, downloadedCount);
            return InstallOutcome.Failed;
        }

        logger.LogInformation("Windows Update install succeeded for {Count} update(s) (result code {ResultCode}).", downloadedCount, installCode);
        return InstallOutcome.Succeeded;
    }

    // OperationResultCode (WUApiLib): 0 orcNotStarted, 1 orcInProgress,
    // 2 orcSucceeded, 3 orcSucceededWithErrors, 4 orcFailed, 5 orcAborted.
    // SucceededWithErrors counts as success here — some but not all
    // updates in the batch had a problem, which is still forward progress
    // and not worth reporting as an outright failed install.
    private static bool IsSuccessCode(int resultCode) => resultCode is 2 or 3;

    private static bool IsRebootRequired()
    {
        dynamic systemInfo = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo")!)!;
        return (bool)systemInfo.RebootRequired;
    }

    private static dynamic CreateSession() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;

    private static dynamic NewUpdateCollection() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;

    // KBArticleIDs is a collection because one update can reference
    // several KB numbers; this project's DetectedUpdate.PackageId is a
    // single string, so only the first is kept — good enough for the
    // admin-facing display this ultimately feeds (AgentDetailPage's update
    // list), which was never designed for more than one ID per row.
    private static string? FirstKbArticleId(dynamic update)
    {
        dynamic kbIds = update.KBArticleIDs;
        int count = kbIds.Count;
        return count > 0 ? "KB" + (string)kbIds.Item(0) : null;
    }
}
