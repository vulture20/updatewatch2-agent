using UpdateWatch2.Agent.UpdateCheck.Linux;

namespace UpdateWatch2.Agent.Tests.UpdateCheck.Linux;

public class LinuxPackageManagerDetectorTests
{
    [Fact]
    public void Detect_prefers_apt_when_apt_get_exists_alongside_dnf()
    {
        var result = LinuxPackageManagerDetector.Detect(path => path is "/usr/bin/apt-get" or "/usr/bin/dnf");

        Assert.Equal(LinuxPackageManagerKind.Apt, result);
    }

    [Fact]
    public void Detect_returns_dnf_when_only_dnf_exists()
    {
        var result = LinuxPackageManagerDetector.Detect(path => path == "/usr/bin/dnf");

        Assert.Equal(LinuxPackageManagerKind.Dnf, result);
    }

    [Fact]
    public void Detect_returns_dnf_when_only_a_yum_binary_exists()
    {
        var result = LinuxPackageManagerDetector.Detect(path => path == "/usr/bin/yum");

        Assert.Equal(LinuxPackageManagerKind.Dnf, result);
    }

    [Fact]
    public void Detect_returns_none_when_no_known_package_manager_binary_exists()
    {
        var result = LinuxPackageManagerDetector.Detect(_ => false);

        Assert.Equal(LinuxPackageManagerKind.None, result);
    }
}
