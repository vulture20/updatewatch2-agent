using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

/// <summary>
/// Unlike <c>AptOutputParserTests</c>, these fixtures are modeled on
/// dnf/yum's documented output format, not captured from a real host —
/// see <see cref="DnfOutputParser"/>'s own honesty caveat.
/// </summary>
public class DnfOutputParserTests
{
    [Fact]
    public void ParseCheckUpdate_extracts_package_name_architecture_version_and_repo()
    {
        const string output = "Last metadata expiration check: 0:12:34 ago on Mon 01 Jan 2036 00:00:00 UTC.\n" +
                               "bash.x86_64                              5.2.15-1.fc39                     updates\n" +
                               "kernel.x86_64                            6.5.6-300.fc39                    updates\n";

        var result = DnfOutputParser.ParseCheckUpdate(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("bash", result[0].Package);
        Assert.Equal("x86_64", result[0].Architecture);
        Assert.Equal("5.2.15-1.fc39", result[0].Version);
        Assert.Equal("updates", result[0].Repository);
        Assert.Equal("kernel", result[1].Package);
    }

    [Fact]
    public void ParseCheckUpdate_skips_blank_lines_and_the_metadata_banner()
    {
        const string output = "\nLast metadata expiration check: 0:12:34 ago on Mon 01 Jan 2036 00:00:00 UTC.\n\n" +
                               "bash.x86_64                              5.2.15-1.fc39                     updates\n";

        var result = DnfOutputParser.ParseCheckUpdate(output);

        Assert.Single(result);
    }

    [Fact]
    public void ParseCheckUpdate_returns_nothing_for_empty_output()
    {
        Assert.Empty(DnfOutputParser.ParseCheckUpdate(""));
    }
}
