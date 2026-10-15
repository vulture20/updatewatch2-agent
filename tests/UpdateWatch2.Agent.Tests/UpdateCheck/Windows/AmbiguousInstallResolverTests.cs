using UpdateWatch2.Agent.UpdateCheck;
using UpdateWatch2.Agent.UpdateCheck.Windows;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Windows;

/// <summary>
/// Covers <see cref="AmbiguousInstallResolver"/> — the pure decision logic
/// extracted out of <c>WuaUpdateSession.DownloadAndInstall</c>'s own
/// ambiguous-selection branch (see that type's doc comment for the real
/// production bug this fixes: a scoped install silently reported
/// "Succeeded" whenever none of the requested KBs matched the current
/// pending search, even when they'd simply fallen off WUA's offer list
/// without ever actually being installed).
/// </summary>
public class AmbiguousInstallResolverTests
{
    [Fact]
    public void Resolve_succeeds_when_every_requested_kb_is_confirmed_installed()
    {
        var result = AmbiguousInstallResolver.Resolve(["KB1", "KB2"], new HashSet<string> { "KB1", "KB2", "KB3" });

        Assert.Equal(InstallOutcome.Succeeded, result.Outcome);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public void Resolve_fails_when_a_requested_kb_cannot_be_confirmed_installed()
    {
        // The real-world case: KB1 was superseded, so it's neither in the
        // "not yet installed" search (the caller's own trigger for calling
        // this at all) NOR in the "already installed" one passed in here.
        var result = AmbiguousInstallResolver.Resolve(["KB1", "KB2"], new HashSet<string> { "KB2" });

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Contains("KB1", result.ErrorDetail);
        Assert.DoesNotContain("KB2", result.ErrorDetail);
    }

    [Fact]
    public void Resolve_names_every_unresolved_kb_when_several_cannot_be_confirmed()
    {
        var result = AmbiguousInstallResolver.Resolve(["KB1", "KB2", "KB3"], new HashSet<string>());

        Assert.Equal(InstallOutcome.Failed, result.Outcome);
        Assert.Contains("KB1", result.ErrorDetail);
        Assert.Contains("KB2", result.ErrorDetail);
        Assert.Contains("KB3", result.ErrorDetail);
        Assert.Contains("3 of 3", result.ErrorDetail);
    }

    [Fact]
    public void Resolve_succeeds_with_no_error_detail_for_an_empty_installed_set_when_nothing_was_requested()
    {
        var result = AmbiguousInstallResolver.Resolve([], new HashSet<string>());

        Assert.Equal(InstallOutcome.Succeeded, result.Outcome);
        Assert.Null(result.ErrorDetail);
    }
}
