namespace UpdateWatch2.Agent.UpdateCheck;

public record DetectedUpdate(string Title, string? PackageId, string? Description);

/// <summary>
/// <see cref="Success"/> defaults to true so every existing positional call
/// site (<c>new UpdateCheckResult(updates, rebootRequired)</c>) that
/// genuinely found real data keeps compiling and behaving unchanged — only
/// the failure paths need to opt into <see cref="Failed"/> explicitly.
/// Added after a real production report: a search failure (e.g. WUApiLib's
/// own transient "0x8024401C" HTTP-request-timeout right after a reboot,
/// while the network wasn't fully back up yet) used to be reported to the
/// server identically to "genuinely found zero updates" — <see cref="UpdateCheckWorker"/>
/// had no way to tell the difference, so it dutifully reported an empty,
/// misleadingly "all clear" result, silently wiping out whatever real
/// pending updates the server already knew about and clearing
/// RebootRequired even if a reboot genuinely was still needed. <see cref="Success"/>
/// lets a caller skip reporting entirely on a failed check instead,
/// leaving the server's last-known-good state untouched until a check
/// actually succeeds again.
/// </summary>
public record UpdateCheckResult(IReadOnlyList<DetectedUpdate> Updates, bool RebootRequired, bool Success = true, string? ErrorDetail = null)
{
    /// <summary>The check itself failed — Updates/RebootRequired are meaningless placeholders, not "genuinely found nothing."</summary>
    public static UpdateCheckResult Failed(string? errorDetail = null) =>
        new([], RebootRequired: false, Success: false, ErrorDetail: errorDetail);
}

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
