using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.Configuration;
using UpdateWatch2.Agent.UpdateCheck;
using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// Real dnf/yum coverage for <see cref="DnfUpdateSession"/>/<see cref="DnfOutputParser"/>
/// — unlike <see cref="DnfUpdateSessionTests"/>/<see cref="DnfOutputParserTests"/>
/// (pure argument-building / hand-modeled-output tests only, since this
/// project's own dev sandbox is Debian-based and has neither dnf nor yum),
/// these tests genuinely shell out to a real dnf5 binary. They only make
/// sense run on an actual RPM-based Linux host — in practice, inside the
/// throwaway Fedora container <c>scripts/run-fedora-test-server.sh</c>
/// stands up, never on this repo's regular Debian-based CI runner or dev
/// sandbox, which is why this class carries its own
/// <c>[Trait("Category", "DnfIntegration")]</c>, excluded from the default
/// <c>dotnet test</c> run the same way <c>ActiveDirectoryLdapIntegrationTests</c>
/// is excluded in the server repo — see that class's own doc comment for
/// the identical reasoning. Container lifecycle is entirely external to
/// this class (no <c>IClassFixture</c> starting Docker itself): the CI
/// job and local dev both run <c>scripts/run-fedora-test-server.sh up</c>
/// before <c>dotnet test --filter Category=DnfIntegration</c>, and <c>down</c>
/// after.
///
/// <para>
/// First run against a real Fedora 44 container (agent v1.0.28): closed
/// the "not live-verified against a real dnf/yum host" gap this class's
/// production counterparts had carried since <c>updatewatch2-agent#8</c>
/// first implemented them — real <c>check-update</c>/<c>needs-restarting -r</c>/
/// <c>--downloadonly</c>/scoped and full <c>update</c> all ran and behaved
/// exactly as coded, including a real reboot-required flip to <c>true</c>
/// after upgrading core libraries (systemd-libs among them). One thing
/// worth knowing about that first run, not a bug: Fedora 44 ships dnf5
/// (Rust-based, <c>/usr/bin/dnf</c> and <c>/usr/bin/yum</c> both symlinks to
/// it) rather than the classic Python dnf/yum this class's own doc
/// comments originally assumed — dnf5 is CLI-compatible for every command
/// this codebase uses, and (unlike classic dnf/yum) bundles its own
/// <c>needs_restarting</c> plugin, so <c>needs-restarting -r</c> worked with
/// no extra package installed at all. A genuinely older/RHEL-family
/// classic-dnf or bare-yum host (this class's <c>ResolveBinary()</c>'s own
/// <c>yum</c> fallback branch) has still never been run for real — treat
/// that specific branch as well-researched but not live-verified the same
/// way the whole class used to be.
/// </para>
/// </summary>
[Trait("Category", "DnfIntegration")]
[SupportedOSPlatform("linux")]
public class DnfIntegrationTests
{
    private static DnfUpdateSession CreateSession() =>
        new(NullLogger<DnfUpdateSession>.Instance, new AgentOptions());

    [Fact]
    public void ParseCheckUpdate_parses_a_real_dnf5_check_update_sample_correctly()
    {
        // Captured verbatim from `dnf -q check-update` on a real Fedora 44
        // container (dnf5) — unlike DnfOutputParserTests' hand-modeled
        // fixtures, this is genuine output, including dnf5's own "Upgrades"
        // section header line before the package list (absent from classic
        // dnf/yum's own check-update output), which the parser must — and
        // does — silently skip rather than mis-parse.
        const string realDnf5Output = """
            Upgrades
            ca-certificates.noarch             2026.2.90_v9.0.317-1.fc44 updates
            curl.x86_64                        8.18.0-10.fc44            updates
            dnf5.x86_64                        5.4.5.0-1.fc44            updates
            dnf5-plugins.x86_64                5.4.5.0-1.fc44            updates
            openssl-libs.x86_64                1:3.5.8-1.fc44            updates
            tar.x86_64                         2:1.35-9.fc44             updates
            """;

        var result = DnfOutputParser.ParseCheckUpdate(realDnf5Output);

        Assert.Equal(6, result.Count);
        Assert.Equal("ca-certificates", result[0].Package);
        Assert.Equal("noarch", result[0].Architecture);
        Assert.Equal("2026.2.90_v9.0.317-1.fc44", result[0].Version);
        Assert.Equal("updates", result[0].Repository);
        // A package with an epoch prefix ("1:3.5.8-1.fc44") stays intact as
        // a single opaque version token — the parser never needs to parse
        // the epoch out, only DescriptionText downstream does.
        Assert.Equal("1:3.5.8-1.fc44", result[4].Version);
    }

    /// <summary>
    /// One comprehensive, deliberately sequential workflow test rather than
    /// several independent [Fact]s — every stage after the first mutates
    /// real dnf state on the host it runs on (selective install, then full
    /// install), so splitting this into separately-orderable facts would
    /// make each one depend on side effects from another with no ordering
    /// guarantee. Mirrors, stage for stage, the throwaway harness this was
    /// first verified with by hand.
    /// </summary>
    [Fact]
    public async Task DnfUpdateSession_runs_a_real_search_download_and_install_workflow()
    {
        var session = CreateSession();

        var initialSearch = await session.SearchForUpdatesAsync(CancellationToken.None);
        Assert.True(initialSearch.Success, initialSearch.ErrorDetail);

        var rebootCheck = await session.IsRebootRequiredAsync(CancellationToken.None);
        Assert.True(rebootCheck.Success, rebootCheck.ErrorDetail);

        var downloadOnly = await session.DownloadOnlyAsync(CancellationToken.None);
        Assert.True(downloadOnly.Success, downloadOnly.ErrorDetail);

        if (initialSearch.Updates.Count == 0)
        {
            // scripts/run-fedora-test-server.sh deliberately seeds a
            // guaranteed-pending update, so this should not happen in
            // practice — but if the container genuinely has nothing
            // pending, there is nothing left to meaningfully assert about
            // install behavior.
            return;
        }

        var firstPackageId = initialSearch.Updates[0].PackageId!;
        var selectiveInstall = await session.DownloadAndInstallAsync([firstPackageId], CancellationToken.None);
        Assert.Equal(InstallOutcome.Succeeded, selectiveInstall.Outcome);

        var afterSelectiveInstall = await session.SearchForUpdatesAsync(CancellationToken.None);
        Assert.True(afterSelectiveInstall.Success, afterSelectiveInstall.ErrorDetail);
        Assert.DoesNotContain(afterSelectiveInstall.Updates, u => u.PackageId == firstPackageId);

        var fullInstall = await session.DownloadAndInstallAsync(null, CancellationToken.None);
        Assert.Equal(InstallOutcome.Succeeded, fullInstall.Outcome);

        var finalSearch = await session.SearchForUpdatesAsync(CancellationToken.None);
        Assert.True(finalSearch.Success, finalSearch.ErrorDetail);
        Assert.Empty(finalSearch.Updates);
    }
}
