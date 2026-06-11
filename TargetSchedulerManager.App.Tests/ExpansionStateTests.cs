using TargetSchedulerManager.App.ViewModels;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>The grid's expansion memory in isolation (extracted from MainViewModel, §7.5).</summary>
public class ExpansionStateTests
{
    [Fact]
    public void EverythingCollapsedByDefault()
    {
        ExpansionState e = new();
        Assert.False(e.IsTargetExpanded("A"));
        Assert.False(e.IsPanelExpanded("A|p1"));
        Assert.False(e.IsRollupExpanded("A|p1|H|Light"));
    }

    [Fact]
    public void SetTarget_TogglesMembership()
    {
        ExpansionState e = new();
        e.SetTarget("A", expanded: true);
        Assert.True(e.IsTargetExpanded("A"));
        e.SetTarget("A", expanded: false);
        Assert.False(e.IsTargetExpanded("A"));
    }

    [Fact]
    public void Keys_AreCaseInsensitive()
    {
        ExpansionState e = new();
        e.SetTarget("M 81", expanded: true);
        Assert.True(e.IsTargetExpanded("m 81"));
    }

    [Fact]
    public void ExpandTargets_MarksEveryGivenTarget()
    {
        ExpansionState e = new();
        e.ExpandTargets(["A", "B"]);
        Assert.True(e.IsTargetExpanded("A"));
        Assert.True(e.IsTargetExpanded("B"));
    }

    [Fact]
    public void CollapseAllTargets_ClearsTargets_ButKeepsPanelAndRollupMemory()
    {
        ExpansionState e = new();
        e.SetTarget("A", expanded: true);
        e.SetPanel("A|p1", expanded: true);
        e.SetRollup("A|p1|H|Light", expanded: true);

        e.CollapseAllTargets();

        Assert.False(e.IsTargetExpanded("A"));            // targets collapsed
        Assert.True(e.IsPanelExpanded("A|p1"));           // nested memory survives a re-expand
        Assert.True(e.IsRollupExpanded("A|p1|H|Light"));
    }

    [Fact]
    public void Planes_AreIndependent()
    {
        ExpansionState e = new();
        e.SetPanel("A|p1", expanded: true);
        Assert.False(e.IsTargetExpanded("A|p1"));         // a panel key is not a target key
        Assert.False(e.IsRollupExpanded("A|p1"));
    }
}
