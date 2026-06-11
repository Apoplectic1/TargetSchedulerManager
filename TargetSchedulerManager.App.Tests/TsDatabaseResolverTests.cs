using Xunit;

namespace TargetSchedulerManager.App.Tests;

// TsDatabaseResolver.IsReachable is the probe the LIVE/LOCAL radios rely on (internal, visible via
// InternalsVisibleTo). A real temp file stands in for a reachable BIRDWATCHER db; a missing path for a down host.
public class TsDatabaseResolverTests
{
    [Fact]
    public void ExistingPath_IsReachable()
    {
        string path = Path.GetTempFileName();
        try
        {
            Assert.True(TsDatabaseResolver.IsReachable(path, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingPath_IsNotReachable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.sqlite");
        Assert.False(TsDatabaseResolver.IsReachable(path, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void UnusablePath_NeverThrows_IsNotReachable()
    {
        // The probe must never bubble an exception onto the startup path — a bad path is simply "not reachable".
        Assert.False(TsDatabaseResolver.IsReachable("\\\\?\\bogus|path", TimeSpan.FromMilliseconds(500)));
    }
}
