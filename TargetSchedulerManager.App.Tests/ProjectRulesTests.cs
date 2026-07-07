using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// TS's one project cross-field save rule, replicated as a warn (never a block): never-selected when the
// meridian window is in use and minimum time exceeds twice it. Values arrive as raw db boxes (long/double/null).
public class ProjectRulesTests
{
    [Theory]
    [InlineData(150L, 60L, true)]     // 150 > 120 — TS's refusal case
    [InlineData(121L, 60L, true)]     // just over the boundary
    [InlineData(120L, 60L, false)]    // exactly 2× is allowed (TS uses strict >)
    [InlineData(150L, 90L, false)]    // 150 ≤ 180 — the fixed pair
    [InlineData(150L, 0L, false)]     // meridian window off → rule inert
    [InlineData(0L, 60L, false)]
    public void PairRule_MatchesTsSaveBehavior(long minTime, long window, bool expected) =>
        Assert.Equal(expected, ProjectRules.IsNeverSelected(minTime, window));

    [Fact]
    public void NullOrNonNumeric_NeverWarns()
    {
        Assert.False(ProjectRules.IsNeverSelected(null, 60L));       // nullable column unset
        Assert.False(ProjectRules.IsNeverSelected(150L, null));
        Assert.False(ProjectRules.IsNeverSelected("—", 60L));        // courtesy, not a contract gate
    }

    [Fact]
    public void DoubleBoxes_WorkLikeLongs()
    {
        Assert.True(ProjectRules.IsNeverSelected(150.5, 60.0));
        Assert.False(ProjectRules.IsNeverSelected(119.9, 60.0));
    }
}
