namespace UpdateWatch2.Agent.UpdateCheck;

public record DetectedUpdate(string Title, string? PackageId, string? Description);

public record UpdateCheckResult(IReadOnlyList<DetectedUpdate> Updates, bool RebootRequired);

/// <summary>
/// Outcome of <see cref="IUpdateChecker.InstallAsync"/> — kept a separate
/// type from <see cref="Communication.InstallOutcome"/> (the wire-facing
/// equivalent) even though the two currently have identical shape, the
/// same layering this file's own DetectedUpdate/ReportedUpdate split
/// already uses (checker-facing DTOs mapped to wire DTOs at the boundary,
/// HeartbeatWorker here, rather than shared directly).
/// </summary>
public enum InstallOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Return value of <see cref="IUpdateChecker.InstallAsync"/> — the outcome
/// plus, when it's <see cref="InstallOutcome.Failed"/>, a human-readable
/// reason (the OS-level tool's own stderr/exit code, or a caught
/// exception's message). Added after a real production failure
/// ("apt-get ... exited with code 100: E: There were unauthenticated
/// packages...") took a live journalctl session with a raised log level to
/// even see — before this, "Failed" was the only thing the admin UI could
/// ever show, forcing exactly that kind of manual log-diving every time.
/// <see cref="ErrorDetail"/> is null on success, and may still be null on
/// failure if nothing more specific than "it failed" is available (e.g. a
/// non-Exception-derived failure path). Kept a separate type from
/// <see cref="Communication.InstallAckRequest"/> (the wire-facing
/// equivalent), matching this file's own DetectedUpdate/ReportedUpdate
/// layering — mapped at the boundary (HeartbeatWorker) rather than shared
/// directly.
/// </summary>
public record InstallResult(InstallOutcome Outcome, string? ErrorDetail = null);
