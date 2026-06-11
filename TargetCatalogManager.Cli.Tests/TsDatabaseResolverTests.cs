using Xunit;

namespace TargetCatalogManager.Cli.Tests;

// TsDatabaseResolver lives in Shared\ (compiled into the CLI head; internal, visible here via InternalsVisibleTo).
// A real temp file stands in for a reachable BIRDWATCHER db; a missing path stands in for an unreachable host.
public class TsDatabaseResolverTests
{
    [Fact]
    public void NetworkReachable_PrefersLive()
    {
        string net = Path.GetTempFileName();
        string local = Path.GetTempFileName();
        try
        {
            TsDatabaseChoice c = TsDatabaseResolver.Resolve(net, local, TimeSpan.FromSeconds(2));
            Assert.True(c.IsLive);
            Assert.Equal(net, c.Path);
        }
        finally
        {
            File.Delete(net);
            File.Delete(local);
        }
    }

    [Fact]
    public void NetworkMissing_FallsBackToLocal()
    {
        string net = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.sqlite");
        string local = Path.GetTempFileName();
        try
        {
            TsDatabaseChoice c = TsDatabaseResolver.Resolve(net, local, TimeSpan.FromSeconds(2));
            Assert.False(c.IsLive);
            Assert.Equal(local, c.Path);
        }
        finally
        {
            File.Delete(local);
        }
    }

    [Fact]
    public void Resolve_NeverThrows_OnUnusablePath_FallsBackToLocal()
    {
        // The resolver must never bubble an exception onto the startup path — a bad path resolves to local.
        string local = Path.GetTempFileName();
        try
        {
            TsDatabaseChoice c = TsDatabaseResolver.Resolve("\\\\?\\bogus|path", local, TimeSpan.FromMilliseconds(500));
            Assert.False(c.IsLive);
            Assert.Equal(local, c.Path);
        }
        finally
        {
            File.Delete(local);
        }
    }
}
