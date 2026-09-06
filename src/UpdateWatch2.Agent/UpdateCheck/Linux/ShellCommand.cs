using System.Diagnostics;

namespace UpdateWatch2.Agent.UpdateCheck.Linux;

/// <summary>
/// Runs an external command and captures its output — the small piece of
/// process-invocation plumbing both <see cref="AptUpdateSession"/> and
/// <see cref="DnfUpdateSession"/> need. Not itself platform-gated
/// (<see cref="Process"/> is a portable API), but only ever called from
/// the Linux-only session classes.
///
/// <para>
/// Always forces <c>LC_ALL=C</c>/<c>LANG=C</c> on the child process —
/// confirmed live in this project's own dev sandbox (Debian 13 trixie),
/// not assumed: running <c>apt list --upgradable</c> under the sandbox's
/// ambient locale printed German text ("Auflistung…", "aktualisierbar
/// von:") instead of the English "Listing…"/"upgradable from:" every
/// parser in this namespace is written against. Forcing the C locale on
/// just this child process (not process-wide, which would affect the
/// agent's own log messages) is the same fix apt's and dnf's own man
/// pages recommend for scripting against their output, and is what makes
/// <see cref="AptOutputParser"/>/<see cref="DnfOutputParser"/> reliable
/// regardless of whatever locale the host machine happens to be
/// configured with.
/// </para>
/// </summary>
internal static class ShellCommand
{
    public readonly record struct Result(int ExitCode, string StandardOutput, string StandardError);

    public static async Task<Result> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";
        if (extraEnvironment is not null)
        {
            foreach (var (key, value) in extraEnvironment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new Result(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
