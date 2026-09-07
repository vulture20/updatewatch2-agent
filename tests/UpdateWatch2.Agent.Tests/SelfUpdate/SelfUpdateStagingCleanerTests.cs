using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.SelfUpdate;

namespace UpdateWatch2.Agent.Tests.SelfUpdate;

public class SelfUpdateStagingCleanerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uw2-selfupdate-cleaner-tests-{Guid.NewGuid()}");

    public SelfUpdateStagingCleanerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Does_nothing_when_the_staging_directory_does_not_exist()
    {
        Directory.Delete(_directory);
        var cleaner = new SelfUpdateStagingCleaner(_directory, NullLogger<SelfUpdateStagingCleaner>.Instance);

        // Must not throw even though the directory is entirely absent —
        // e.g. a host that has never once received a self-update offer.
        cleaner.CleanupOldFiles(TimeSpan.FromDays(90));
    }

    [Fact]
    public void Does_nothing_when_only_one_file_exists_no_matter_its_age()
    {
        var path = WriteFile("only-one.deb", DateTime.UtcNow.AddDays(-400));
        var cleaner = new SelfUpdateStagingCleaner(_directory, NullLogger<SelfUpdateStagingCleaner>.Instance);

        cleaner.CleanupOldFiles(TimeSpan.FromDays(90));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Deletes_files_older_than_the_retention_period_but_always_keeps_the_most_recent_one()
    {
        var oldest = WriteFile("agent-0.10.0.deb", DateTime.UtcNow.AddDays(-200));
        var alsoOld = WriteFile("agent-0.11.0.deb", DateTime.UtcNow.AddDays(-100));
        var withinRetention = WriteFile("agent-0.12.0.deb", DateTime.UtcNow.AddDays(-10));
        var mostRecent = WriteFile("agent-0.12.4.deb", DateTime.UtcNow);
        var cleaner = new SelfUpdateStagingCleaner(_directory, NullLogger<SelfUpdateStagingCleaner>.Instance);

        cleaner.CleanupOldFiles(TimeSpan.FromDays(90));

        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(alsoOld));
        Assert.True(File.Exists(withinRetention));
        Assert.True(File.Exists(mostRecent));
    }

    [Fact]
    public void Keeps_the_most_recent_file_even_if_it_is_itself_older_than_the_retention_period()
    {
        // The one case this whole feature was explicitly built for: a host
        // that hasn't self-updated in a very long time must still have at
        // least one real example of what it last tried to install.
        var onlyFile = WriteFile("agent-0.9.0.deb", DateTime.UtcNow.AddDays(-500));
        var cleaner = new SelfUpdateStagingCleaner(_directory, NullLogger<SelfUpdateStagingCleaner>.Instance);

        cleaner.CleanupOldFiles(TimeSpan.FromDays(90));

        Assert.True(File.Exists(onlyFile));
    }

    private string WriteFile(string name, DateTime lastWriteTimeUtc)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "placeholder");
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
