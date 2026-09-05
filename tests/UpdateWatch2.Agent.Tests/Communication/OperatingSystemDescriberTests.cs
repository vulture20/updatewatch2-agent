using UpdateWatch2.Agent.Communication;

namespace UpdateWatch2.Agent.Tests.Communication;

/// <summary>
/// Covers OperatingSystemDescriber.BuildFriendlyName — the pure decision
/// logic, kept separate from the actual Windows registry read specifically
/// so it's testable here, on a suite with no Windows host to run the
/// [SupportedOSPlatform("windows")] half against.
/// </summary>
public class OperatingSystemDescriberTests
{
    [Fact]
    public void Reports_Windows_11_by_build_number_even_when_ProductName_still_says_Windows_10()
    {
        // The actual bug this exists to work around: real Windows 11
        // installs keep a ProductName registry value that still reads
        // "Windows 10 ..." — Microsoft never updated it after the
        // rebrand. Trusting ProductName directly would silently mislabel
        // every genuine Windows 11 machine as Windows 10.
        var result = OperatingSystemDescriber.BuildFriendlyName(
            installationType: "Client", productName: "Windows 10 Pro", displayVersion: "25H2", buildNumber: 26200);

        Assert.Equal("Windows 11 25H2", result);
    }

    [Fact]
    public void Reports_Windows_10_below_the_Windows_11_build_threshold()
    {
        var result = OperatingSystemDescriber.BuildFriendlyName(
            installationType: "Client", productName: "Windows 10 Pro", displayVersion: "22H2", buildNumber: 19045);

        Assert.Equal("Windows 10 22H2", result);
    }

    [Fact]
    public void Falls_back_to_the_bare_edition_name_when_DisplayVersion_is_missing()
    {
        var result = OperatingSystemDescriber.BuildFriendlyName(
            installationType: "Client", productName: "Windows 10 Pro", displayVersion: null, buildNumber: 22631);

        Assert.Equal("Windows 11", result);
    }

    [Fact]
    public void Trusts_ProductName_as_is_for_server_installs()
    {
        // Server ProductName isn't affected by the client-only Windows
        // 10/11 mislabeling bug — no rebrand happened there to bungle.
        var result = OperatingSystemDescriber.BuildFriendlyName(
            installationType: "Server", productName: "Windows Server 2025 Standard", displayVersion: "24H2", buildNumber: 26100);

        Assert.Equal("Windows Server 2025 Standard", result);
    }

    [Fact]
    public void Installation_type_comparison_is_case_insensitive()
    {
        var result = OperatingSystemDescriber.BuildFriendlyName(
            installationType: "server", productName: "Windows Server 2022 Datacenter", displayVersion: null, buildNumber: 20348);

        Assert.Equal("Windows Server 2022 Datacenter", result);
    }

    [Fact]
    public void Describe_returns_the_raw_OSDescription_unchanged_on_non_Windows_platforms()
    {
        // This suite runs on Linux — a real, live check of the non-Windows
        // branch, not just a theoretical one.
        Assert.False(OperatingSystem.IsWindows());

        var result = OperatingSystemDescriber.Describe();

        Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.OSDescription, result);
    }
}
