namespace UpdateWatch2.Agent.Configuration;

/// <summary>
/// Local agent configuration — server address/port and the update-check /
/// alive-heartbeat cadence. Stored in the Windows registry (set by the NSIS
/// installer) or, on Linux, an equivalent config file; see
/// <see cref="IAgentConfigStore"/>. The agent's identity (hostname) is not
/// part of this — it's read from the OS at runtime, per CLAUDE.md
/// ("Agents are identified by hostname").
/// </summary>
public class AgentOptions
{
    public string ServerAddress { get; set; } = "";

    public int ServerPort { get; set; } = 8796;

    /// <summary>Base interval between update checks.</summary>
    public int UpdateCheckIntervalMinutes { get; set; } = 240;

    /// <summary>
    /// Random jitter (0..N seconds) added to <see cref="UpdateCheckIntervalMinutes"/>
    /// so many agents don't hit the server at the same moment.
    /// </summary>
    public int UpdateCheckJitterSeconds { get; set; } = 300;

    public int AliveIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// DEBUG/INFO/WARNING/ERROR — same values as the server's
    /// UPDATEWATCH2_LOGLEVEL. Can be set locally or pushed centrally from
    /// the server UI (not implemented yet).
    /// </summary>
    public string LogLevel { get; set; } = "INFO";

    /// <summary>
    /// Interval between registration polls while waiting for admin
    /// approval and certificate issuance (updatewatch2-agent#1) — separate
    /// from <see cref="AliveIntervalMinutes"/> since a human is typically
    /// actively watching during onboarding, so a multi-minute wait would
    /// feel broken.
    /// </summary>
    public int RegistrationRetryIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// The opaque, per-agent registration token received on first contact
    /// and re-presented on every registration poll until a certificate is
    /// issued — see RegistrationWorker and the server's
    /// AgentRegistrationService for the full state machine. Cleared once a
    /// certificate has been received (no longer needed at that point).
    /// </summary>
    public string? RegistrationToken { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of this agent's issued client certificate, once
    /// received. On Windows the certificate itself lives in the machine
    /// certificate store (see Certificates.Windows.WindowsClientCertificateStore)
    /// and is looked up by this thumbprint; on Linux it's a file path
    /// instead, so this field is unused there.
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }

    /// <summary>
    /// How long before its client certificate's NotAfter this agent
    /// proactively requests renewal (updatewatch2-server#7). Checked
    /// against the certificate's own NotAfter, which this agent already
    /// holds locally — deliberately not a value the server sends back, to
    /// avoid a protocol/DTO change just for this.
    /// </summary>
    public int CertificateRenewalLeadTimeDays { get; set; } = 60;

    /// <summary>
    /// The upper bound on how often <see cref="UpdateWatch2.Agent.RegistrationWorker"/>'s
    /// persistent maintenance loop re-checks its local certificate once one
    /// is already attached (updatewatch2-agent#3) — deliberately coarser
    /// than <see cref="RegistrationRetryIntervalSeconds"/>, which stays
    /// reserved for actively onboarding/recovering, not steady-state
    /// idling. Only an upper bound, not the actual recovery latency,
    /// though: <see cref="UpdateWatch2.Agent.HeartbeatWorker"/>'s self-heal
    /// (updatewatch2-server#11/updatewatch2-agent#5) wakes this loop
    /// immediately via <see cref="Certificates.IRegistrationWakeSignal"/>
    /// the moment it drops a rejected certificate, rather than leaving
    /// recovery to wait out this full interval — added after a real user
    /// report that recovery sometimes appeared to simply never happen; it
    /// did, just slowly, with nothing logged in the meantime. In seconds,
    /// not minutes (unlike most of this class' other intervals), so a
    /// short value is representable for tests without a whole-minute floor.
    /// </summary>
    public int CertificateMaintenanceIntervalSeconds { get; set; } = 900;

    /// <summary>
    /// How long a downloaded self-update package (updatewatch2-agent#14)
    /// is kept in the local staging directory before
    /// <see cref="SelfUpdate.SelfUpdateStagingCleaner"/> deletes it —
    /// <see cref="SelfUpdate.AgentSelfUpdateService"/> itself never cleans
    /// up a package it actually applied (successfully or not), only one
    /// that failed its integrity check, so every self-update this agent
    /// has ever gone through would otherwise leave its downloaded
    /// installer/.deb/.rpm behind indefinitely. The single
    /// most-recently-downloaded package is always kept regardless of this
    /// value, so an admin can always find at least one real example of
    /// what this agent last tried to install.
    /// </summary>
    public int SelfUpdateStagingRetentionDays { get; set; } = 90;
}
