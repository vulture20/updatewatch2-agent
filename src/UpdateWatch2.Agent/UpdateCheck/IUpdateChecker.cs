namespace UpdateWatch2.Agent.UpdateCheck;

/// <summary>
/// Searches for available updates and determines whether a reboot is
/// required — kept as a separate signal from installation, per CLAUDE.md
/// ("Der Agent ermittelt bzw. meldet zusätzlich, ob ein Neustart des
/// Systems erforderlich ist – getrennt von der eigentlichen
/// Installation.").
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}
