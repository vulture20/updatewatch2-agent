using System.Diagnostics;
using System.Runtime.Versioning;

namespace UpdateWatch2.Agent.Restart.Windows;

/// <summary>
/// Restarts the <c>UpdateWatch2 Agent</c> Windows service via a detached
/// helper batch script, not a plain <c>sc.exe</c> call from this process
/// directly — stopping this service via SCM terminates this very process
/// before it could run a second (<c>start</c>) command itself, so the
/// stop-wait-start sequence has to live in a process that outlives this
/// one. A tiny self-deleting <c>.bat</c> file sidesteps the alternative
/// (a single <c>cmd.exe /c "... &amp; ..."</c> command line with the
/// service name's embedded space needing its own nested quoting) rather
/// than fighting cmd's quoting rules for an embedded quoted argument.
///
/// <para>
/// <b>NOT live-verified on Windows</b> — same honesty caveat this
/// codebase already carries for every other Windows-only class
/// (<c>WuaUpdateSession</c>, <c>WindowsInstallerApplier</c>): no Windows
/// host was available when this was written. The Linux equivalent
/// (<see cref="Linux.LinuxAgentRestarter"/>) was live-verified for real —
/// see its own doc comment.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAgentRestarter(ILogger<WindowsAgentRestarter> logger) : IAgentRestarter
{
    // Must stay byte-for-byte identical to Program.cs's
    // AddWindowsService(o => o.ServiceName = ...) and installer/nsis/setup.nsi's
    // SERVICE_NAME — the same three-way pairing setup.nsi's own comment
    // already calls out for that pair.
    private const string ServiceName = "UpdateWatch2 Agent";

    /// <summary>
    /// The helper script's content — pulled out as its own testable pure
    /// function (mirroring this codebase's <c>BuildInstallArgs</c>/
    /// <c>BuildInstallCommand</c> convention on the Linux/apt/dnf side) so
    /// the actual command sequence has real test coverage even though
    /// launching it does not. The short delays give <c>sc stop</c> — which
    /// only REQUESTS a stop and returns immediately, it doesn't wait for
    /// the service to actually finish stopping — time to complete before
    /// <c>sc start</c> runs; the trailing <c>del</c> of the script's own
    /// path (<c>%~f0</c>) leaves nothing behind in the temp directory.
    /// </summary>
    public static string BuildRestartScript(string serviceName) =>
        "@echo off\r\n" +
        "timeout /t 2 /nobreak >nul\r\n" +
        $"sc stop \"{serviceName}\"\r\n" +
        "timeout /t 3 /nobreak >nul\r\n" +
        $"sc start \"{serviceName}\"\r\n" +
        "del \"%~f0\"\r\n";

    public void RequestRestart()
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"uw2-agent-restart-{Guid.NewGuid():N}.bat");
            File.WriteAllText(scriptPath, BuildRestartScript(ServiceName));

            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(scriptPath);

            // Deliberately not awaited/disposed-and-waited — see this
            // class's and IAgentRestarter's own doc comments: stopping
            // this service is about to terminate this process, and the
            // detached script must keep running independently of that.
            Process.Start(startInfo);

            logger.LogInformation("Launched a detached restart of the '{ServiceName}' service.", ServiceName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch the detached service restart.");
            throw;
        }
    }
}
