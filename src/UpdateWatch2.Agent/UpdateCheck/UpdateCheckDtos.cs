namespace UpdateWatch2.Agent.UpdateCheck;

public record DetectedUpdate(string Title, string? PackageId, string? Description);

public record UpdateCheckResult(IReadOnlyList<DetectedUpdate> Updates, bool RebootRequired);
