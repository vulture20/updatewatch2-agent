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
/// no extra package installed at all.
/// </para>
///
/// <para>
/// Both remaining RPM-family variants have since been confirmed too
/// (agent v1.0.29, at the user's explicit request): this exact class ran
/// unmodified — no code change needed — against a real Rocky Linux 9
/// container (classic <c>dnf4</c>, the "not dnf5" distinction the paragraph
/// above left open) and passed identically, and Rocky is now a permanent
/// second leg in the CI job's matrix alongside Fedora, via
/// <c>UPDATEWATCH2_TEST_RPM_IMAGE</c>. The last variant — a host with no
/// <c>dnf</c> binary at all, reaching <see cref="DnfUpdateSession.ResolveBinary"/>'s
/// literal <c>yum</c> fallback — was confirmed against a real CentOS 7
/// container, but only at the raw CLI level, not through this class: .NET
/// 10 cannot run on CentOS 7's stock <c>libstdc++</c> at all (a concrete
/// <c>GLIBCXX_3.4.20 not found</c> failure), and CentOS 7 is EOL anyway
/// (frozen <c>vault.centos.org</c> repos), so it deliberately isn't part
/// of the permanent CI matrix — see <see cref="ParseCheckUpdate_parses_a_real_legacy_yum_check_update_sample_correctly"/>
/// for what real output that pass did capture.
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

    [Fact]
    public void ParseCheckUpdate_parses_a_real_legacy_yum_check_update_sample_correctly()
    {
        // Captured verbatim from `yum -q check-update` on a real CentOS 7
        // container (bare yum, no dnf binary at all — ResolveBinary()'s
        // literal "yum" fallback branch). Even plainer than dnf5's own
        // shape: no section header at all, just a genuine leading blank
        // line, which the parser already handled correctly before this.
        const string realYumOutput = "\n" + """
            bash.x86_64                         4.2.46-35.el7_9                       updates
            ca-certificates.noarch              2023.2.60_v7.0.306-72.el7_9           updates
            device-mapper.x86_64                7:1.02.170-6.el7_9.5                  updates
            tzdata.noarch                       2024a-1.el7                           updates
            """;

        var result = DnfOutputParser.ParseCheckUpdate(realYumOutput);

        Assert.Equal(4, result.Count);
        Assert.Equal("bash", result[0].Package);
        Assert.Equal("x86_64", result[0].Architecture);
        Assert.Equal("4.2.46-35.el7_9", result[0].Version);
        Assert.Equal("updates", result[0].Repository);
        Assert.Equal("7:1.02.170-6.el7_9.5", result[2].Version);
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
            // A freshly pulled Fedora/Rocky image reliably has some real
            // pending updates already (container images lag behind each
            // distro's own continuously published updates) — confirmed in
            // practice on both, so scripts/run-fedora-test-server.sh
            // doesn't bother deliberately seeding one. If a container
            // genuinely has nothing pending, though, there is nothing left
            // to meaningfully assert about install behavior.
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
