using Xunit;

namespace TargetSchedulerManager.App.Tests;

// TsDatabaseResolver.Stat is the timeout-guarded remote probe the sync layer's pull/skip rule runs on. A real
// temp file stands in for a reachable BIRDWATCHER db; a missing path for a down host.
public class TsDatabaseResolverTests
{
    [Fact]
    public void ExistingPath_ReturnsStat()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            TsDbStat? stat = TsDatabaseResolver.Stat(path, TimeSpan.FromSeconds(2));
            Assert.NotNull(stat);
            Assert.Equal(3, stat!.Length);
            Assert.Equal(File.GetLastWriteTimeUtc(path), stat.LastWriteUtc);
            Assert.False(stat.HasSidecar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SidecarBesideTheDb_IsReported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tsm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "db.sqlite");
        File.WriteAllText(path, "x");
        File.WriteAllText(path + "-wal", "");

        TsDbStat? stat = TsDatabaseResolver.Stat(path, TimeSpan.FromSeconds(2));
        Assert.NotNull(stat);
        Assert.True(stat!.HasSidecar);
    }

    [Fact]
    public void MissingPath_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.sqlite");
        Assert.Null(TsDatabaseResolver.Stat(path, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void UnusablePath_NeverThrows_ReturnsNull()
    {
        // The probe must never bubble an exception onto the startup path — a bad path is simply "not reachable".
        Assert.Null(TsDatabaseResolver.Stat("\\\\?\\bogus|path", TimeSpan.FromMilliseconds(500)));
    }

    // ---- the await-friendly form (review N8) — same contract, no parked thread ----------------------------

    [Fact]
    public async Task StatAsync_ExistingPath_ReturnsStat()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            TsDbStat? stat = await TsDatabaseResolver.StatAsync(path, TimeSpan.FromSeconds(2));
            Assert.NotNull(stat);
            Assert.Equal(3, stat!.Length);
            Assert.False(stat.HasSidecar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StatAsync_MissingPath_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.sqlite");
        Assert.Null(await TsDatabaseResolver.StatAsync(path, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StatAsync_UnusablePath_NeverThrows_ReturnsNull()
    {
        Assert.Null(await TsDatabaseResolver.StatAsync("\\\\?\\bogus|path", TimeSpan.FromMilliseconds(500)));
    }
}
