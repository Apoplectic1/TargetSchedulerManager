using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The LIVE/LOCAL state machine in isolation, with the liveness probe injected (no SMB, no DevDefaults paths).
public class TsSourceTests
{
    private static TsSource New(Func<bool> probe) => new("LIVE", "LOCAL", probe);

    [Fact]
    public void FirstResolve_ProbeReachable_SelectsLive()
    {
        TsSource s = New(() => true);
        Assert.Equal("LIVE", s.ResolvePathForLoad());
        Assert.True(s.IsLive);
        Assert.True(s.LiveEnabled);
    }

    [Fact]
    public void FirstResolve_ProbeDown_FallsToLocal_AndDisablesLive()
    {
        TsSource s = New(() => false);
        Assert.Equal("LOCAL", s.ResolvePathForLoad());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);   // sticky-disabled
    }

    [Fact]
    public void SecondResolve_WasLive_ProbeNowDown_FallsToLocal()
    {
        bool reachable = true;
        TsSource s = New(() => reachable);
        s.ResolvePathForLoad();                 // → Live
        reachable = false;
        Assert.Equal("LOCAL", s.ResolvePathForLoad());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);
    }

    [Fact]
    public void TrySelectMode_LiveWhenStickyDisabled_IsIgnored()
    {
        TsSource s = New(() => false);
        s.ResolvePathForLoad();                 // Local + LiveEnabled false
        Assert.False(s.TrySelectMode(TsMode.Live));
        Assert.False(s.IsLive);
    }

    [Fact]
    public void NotifyLiveWriteFailed_LiveAndNowUnreachable_DropsToLocal()
    {
        bool reachable = true;
        TsSource s = New(() => reachable);
        s.ResolvePathForLoad();                 // → Live
        reachable = false;
        Assert.True(s.NotifyLiveWriteFailed());
        Assert.False(s.IsLive);
        Assert.False(s.LiveEnabled);
    }

    [Fact]
    public void NotifyLiveWriteFailed_LiveButStillReachable_NotADrop()
    {
        TsSource s = New(() => true);
        s.ResolvePathForLoad();                 // → Live
        Assert.False(s.NotifyLiveWriteFailed()); // some other fault, not a drop
        Assert.True(s.IsLive);
    }

    [Fact]
    public void TrySelectMode_RealChange_ReturnsTrue_SameMode_ReturnsFalse()
    {
        TsSource s = New(() => true);
        s.ResolvePathForLoad();                       // → Live, LiveEnabled
        Assert.True(s.TrySelectMode(TsMode.Local));   // a real Live→Local switch
        Assert.False(s.IsLive);
        Assert.False(s.TrySelectMode(TsMode.Local));  // already Local → no change
    }
}
