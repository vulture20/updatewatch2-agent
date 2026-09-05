namespace UpdateWatch2.Agent.UpdateCheck.Windows;

/// <summary>
/// Thin seam over the real Windows Update Agent (WUApiLib) COM API, so
/// <see cref="WindowsUpdateChecker"/>'s own logic — mapping a search
/// result, deciding what to acknowledge, translating an exception into a
/// failure — can be unit-tested with a hand-written fake (this codebase's
/// usual fakes-not-mocks convention, see <c>WorkerTests.FakeUpdateChecker</c>
/// for the same pattern one layer up) without needing a real Windows
/// Update Agent, or even a Windows host, to run the tests. CI runs on
/// Linux (ubuntu-latest, see .github/workflows), so the one implementation
/// of this interface that actually talks to COM — <see cref="WuaUpdateSession"/> —
/// is never exercised there; splitting the seam here is what lets
/// <see cref="WindowsUpdateChecker"/> itself stay untested-on-Windows-only
/// and get real CI coverage instead.
/// </summary>
public interface IWindowsUpdateSession
{
    /// <summary>Searches for available, not-yet-installed updates and reports whether a reboot is currently pending.</summary>
    UpdateCheckResult SearchForUpdates(CancellationToken ct);

    /// <summary>
    /// Searches, downloads, and installs whatever is currently applicable.
    /// Must never trigger a reboot itself, even if installation leaves one
    /// pending — see <see cref="WuaUpdateSession"/> for how that's honored.
    /// </summary>
    InstallOutcome DownloadAndInstall(CancellationToken ct);
}
