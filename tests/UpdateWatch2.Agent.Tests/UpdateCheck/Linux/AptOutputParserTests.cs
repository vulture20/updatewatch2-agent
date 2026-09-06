using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// The fixtures below are captured verbatim (via
/// <c>LC_ALL=C apt list --upgradable</c>) from this project's own dev
/// sandbox (Debian 13 trixie) — real output, not invented — see
/// <see cref="AptOutputParser"/>'s own doc comment.
/// </summary>
public class AptOutputParserTests
{
    [Fact]
    public void ParseUpgradable_extracts_package_name_and_versions_from_a_real_line()
    {
        const string line = "bubblewrap/stable-security 0.12.0-1~deb13u1 amd64 [upgradable from: 0.11.0-2+deb13u1]";

        var result = AptOutputParser.ParseUpgradable(line);

        var package = Assert.Single(result);
        Assert.Equal("bubblewrap", package.Package);
        Assert.Equal("0.12.0-1~deb13u1", package.NewVersion);
        Assert.Equal("0.11.0-2+deb13u1", package.OldVersion);
    }

    [Fact]
    public void ParseUpgradable_handles_a_versioned_epoch_alongside_the_warning_and_listing_header_lines()
    {
        const string output = "WARNING: apt does not have a stable CLI interface. Use with caution in scripts.\n" +
                               "\n" +
                               "Listing...\n" +
                               "docker-ce-cli/trixie 5:29.8.0-1~debian.13~trixie amd64 [upgradable from: 5:29.7.2-1~debian.13~trixie]\n";

        var result = AptOutputParser.ParseUpgradable(output);

        var package = Assert.Single(result);
        Assert.Equal("docker-ce-cli", package.Package);
        Assert.Equal("5:29.8.0-1~debian.13~trixie", package.NewVersion);
        Assert.Equal("5:29.7.2-1~debian.13~trixie", package.OldVersion);
    }

    [Fact]
    public void ParseUpgradable_extracts_every_package_from_multiple_lines()
    {
        const string output = "bubblewrap/stable-security 0.12.0-1~deb13u1 amd64 [upgradable from: 0.11.0-2+deb13u1]\n" +
                               "gitea/gitea 1.27.3+1 amd64 [upgradable from: 1.27.2+1]\n";

        var result = AptOutputParser.ParseUpgradable(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("bubblewrap", result[0].Package);
        Assert.Equal("gitea", result[1].Package);
    }

    [Fact]
    public void ParseUpgradable_returns_nothing_for_empty_output()
    {
        Assert.Empty(AptOutputParser.ParseUpgradable(""));
    }
}
