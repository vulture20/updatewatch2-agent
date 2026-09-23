namespace UpdateWatch2.Agent.Configuration;

/// <summary>
/// Local agent configuration — server address/port and the update-check /
/// alive-heartbeat cadence. Stored in the Windows registry (set by the NSIS
/// installer) or, on Linux, an equivalent config file; see
/// <see cref="IAgentConfigStore"/>. The agent's identity (hostname) is read
/// from the OS at runtime by default, per CLAUDE.md ("Agents are identified
/// by hostname") — <see cref="HostnameOverride"/> is the one deliberate
/// exception to that; see <see cref="ResolveHostname"/>.
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
    /// UPDATEWATCH2_LOGLEVEL. Set locally by default; an admin can also
    /// push a per-agent override from the server (a per-agent Settings
    /// dialog, not the fleet-wide admin settings page), which is enforced
    /// live and persisted back into this value on the next heartbeat — see
    /// <see cref="HeartbeatWorker"/>'s <c>ApplyPushedSettings</c> and
    /// <see cref="ILogLevelState"/>.
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
    /// Linux only (no-op on Windows, which has no equivalent concept):
    /// when true, passes <c>apt-get</c>'s <c>--allow-unauthenticated</c>
    /// (<see cref="UpdateCheck.Linux.AptUpdateSession"/>) or <c>dnf</c>/
    /// <c>yum</c>'s <c>--nogpgcheck</c> (<see cref="UpdateCheck.Linux.DnfUpdateSession"/>)
    /// on every install/pre-download command, letting this agent proceed
    /// with packages from a repository whose signature can't be verified
    /// (e.g. a missing or not-yet-imported GPG key) instead of failing
    /// the whole transaction. Security-relevant — bypassing package
    /// authentication is a real trust decision, not a cosmetic one — so
    /// this defaults to <c>false</c> and is local-only, deliberately not
    /// something the server can push (unlike <see cref="LogLevel"/>/the
    /// update-check cadence): an admin who wants this on a given host has
    /// to opt in by hand, in that host's own config file. Added after a
    /// real production incident where a genuinely untrusted/unsigned repo
    /// on one host made every triggered install fail with apt's own
    /// "unauthenticated packages" error — the correct fix there was
    /// importing the repository's GPG key, not this flag, which exists
    /// for the rarer case where an admin has already made that trust
    /// decision deliberately (e.g. a local/air-gapped repository that
    /// will never be signed) and wants this agent to stop refusing it.
    /// </summary>
    public bool AllowUnauthenticatedPackages { get; set; }

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

    /// <summary>
    /// Overrides the hostname this agent reports to — and is thereafter
    /// identified by on — the server, in place of the OS-reported
    /// <see cref="Environment.MachineName"/>, at the user's explicit
    /// request ("Der an den Server gemeldete und damit auch genutzte
    /// Hostname des Agents sollte über einen Parameter... überschreibbar
    /// sein."). Null or blank means "no override" — see
    /// <see cref="ResolveHostname"/>, the only place this is read.
    ///
    /// Local-only, never pushed by the server (same discipline as
    /// <see cref="AllowUnauthenticatedPackages"/>) — the server has no way
    /// to know what a machine's real/desired name should be, only what an
    /// admin tells this specific agent by hand.
    ///
    /// Deliberately does NOT affect <see cref="Communication.ServerClient"/>'s
    /// separately self-reported <c>DnsName</c> metadata field (resolved via
    /// <c>Dns.GetHostEntry</c>, purely informational, shown in the admin
    /// UI) — this only overrides the identity/routing hostname (CLAUDE.md
    /// "Agents are identified by hostname"), a genuinely different concept
    /// from "what does this agent report about itself."
    ///
    /// Changing this on an agent that already holds a client certificate is
    /// a real, deliberate footgun, not something silently handled: the
    /// server's <c>alive</c>/<c>renew</c>/<c>reboot-ack</c> endpoints all
    /// reject a request whose URL hostname doesn't match the identity baked
    /// into the presented certificate's Subject (<c>CN=&lt;hostname&gt;</c>
    /// at issuance). <see cref="HeartbeatWorker"/> detects this LOCALLY —
    /// comparing the loaded certificate's own Subject against the
    /// currently-effective hostname, before ever attempting an
    /// authenticated call — logs a clear warning every tick it persists,
    /// and skips the heartbeat/renewal for that tick, deliberately WITHOUT
    /// triggering this codebase's own self-heal mechanism (which would
    /// otherwise silently drop the certificate and re-register under the
    /// new hostname, leaving the OLD Agent row orphaned on the server, at
    /// the user's explicit request that this be surfaced rather than acted
    /// on automatically). An admin who wants to actually rename an
    /// already-onboarded agent must delete its old server-side entry and
    /// clear this agent's local client certificate by hand, which lets
    /// <see cref="UpdateWatch2.Agent.RegistrationWorker"/>'s existing
    /// lost-certificate recovery path register it fresh under the new name.
    /// </summary>
    public string? HostnameOverride { get; set; }

    /// <summary>
    /// The hostname this agent actually reports to, and is identified by
    /// on, the server — <see cref="HostnameOverride"/> when set to a
    /// non-blank value, otherwise the OS-reported
    /// <see cref="Environment.MachineName"/>. The one place
    /// <see cref="HostnameOverride"/> is read.
    /// </summary>
    public string ResolveHostname() => string.IsNullOrWhiteSpace(HostnameOverride) ? Environment.MachineName : HostnameOverride;
}
