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
