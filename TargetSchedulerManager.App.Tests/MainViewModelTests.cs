using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>Filter → group → flatten pipeline and the in-place toggle editing, via the internal row seam.</summary>
public class MainViewModelTests
{
    [Fact]
    public void SetRows_GroupsCollapsedByDefault()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A"), Make.Ts(target: "A", filter: "O"), Make.Leaf(target: "B"));

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.IsType<TargetGroupRow>(r));
        Assert.All(vm.Rows.Cast<TargetGroupRow>(), g => Assert.False(g.IsExpanded));
    }

    [Fact]
    public void ToggleGroup_InsertsAndRemovesChildrenInPlace()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A"), Make.Ts(target: "A", filter: "O"), Make.Leaf(target: "B"));
        TargetGroupRow a = (TargetGroupRow)vm.Rows[0];

        vm.ToggleGroup(a);
        Assert.True(a.IsExpanded);
        Assert.Equal(4, vm.Rows.Count);                      // A header + 2 leaves + B header
        Assert.IsType<ReconciliationRow>(vm.Rows[1]);
        Assert.IsType<TargetGroupRow>(vm.Rows[3]);

        vm.ToggleGroup(a);
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void Expansion_SurvivesFilterRoundTrip()
    {
        MainViewModel vm = Vm(Make.Leaf(target: "A"), Make.Leaf(target: "B"));
        vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);

        vm.SearchText = "B";                                  // A disappears entirely
        Assert.Single(vm.Rows.OfType<TargetGroupRow>());

        vm.SearchText = "";                                   // A returns, still expanded
        TargetGroupRow a = vm.Rows.OfType<TargetGroupRow>().Single(g => g.Target == "A");
        Assert.True(a.IsExpanded);
        Assert.IsType<ReconciliationRow>(vm.Rows[vm.Rows.IndexOf(a) + 1]);
    }

    [Fact]
    public void Search_HeaderAggregatesCoverOnlySurvivingLeaves()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A", filter: "H", disk: 4),
            Make.Leaf(target: "A", filter: "O", disk: 9));

        vm.SearchText = "H";
        TargetGroupRow a = vm.Rows.OfType<TargetGroupRow>().Single();
        Assert.Single(a.Children);
        Assert.Equal(4, a.Disk);                              // not 13 — sums match what's beneath
    }

    [Fact]
    public void SourceFilter_ClassifiesWholeTargets()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A", source: RowSource.Both),
            Make.Ts(target: "B", source: RowSource.TsOnly));

        vm.SourceFilterIndex = 2;                             // TS-only
        Assert.Equal("B", vm.Rows.OfType<TargetGroupRow>().Single().Target);
    }

    [Fact]
    public void FlaggedOnly_KeepsFlaggedLeaves()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A"),
            Make.Leaf(target: "B", flagged: true, badge: "name≠"));

        vm.FlaggedOnly = true;
        Assert.Equal("B", vm.Rows.OfType<TargetGroupRow>().Single().Target);
    }

    [Fact]
    public void MosaicLeaves_GroupIntoPanels_TogglePanelShowsLeaves()
    {
        const string k1 = "Mosaic - X/Panel 01of16", k2 = "Mosaic - X/Panel 02of16";
        MainViewModel vm = Vm(
            Make.Leaf(target: "Mosaic - X", panelKey: k1, panelLabel: "Panel 01of16 · X P1", panelSource: RowSource.Both),
            Make.Leaf(target: "Mosaic - X", filter: "O", panelKey: k1, panelLabel: "Panel 01of16 · X P1", panelSource: RowSource.Both),
            Make.Disk(target: "Mosaic - X", panelKey: k2, panelLabel: "Panel 02of16", panelSource: RowSource.DiskOnly));

        TargetGroupRow group = (TargetGroupRow)vm.Rows[0];
        vm.ToggleGroup(group);

        // Expanded group shows panel mini-headers (collapsed), not raw leaves.
        Assert.Equal(3, vm.Rows.Count);                       // group + 2 panels
        PanelGroupRow p1 = Assert.IsType<PanelGroupRow>(vm.Rows[1]);
        Assert.IsType<PanelGroupRow>(vm.Rows[2]);

        vm.TogglePanel(p1);
        Assert.Equal(5, vm.Rows.Count);                       // p1's two filter leaves inserted
        Assert.IsType<ReconciliationRow>(vm.Rows[2]);

        vm.TogglePanel(p1);
        Assert.Equal(3, vm.Rows.Count);

        vm.ToggleGroup(group);                                // collapse sweeps panels too
        Assert.Single(vm.Rows);
    }

    [Fact]
    public void RollupExpansion_RestoredAcrossRebuilds()
    {
        ReconciliationRow detail1 = Make.Ts(target: "A", seconds: 300);
        ReconciliationRow detail2 = Make.Disk(target: "A", seconds: 600);
        ReconciliationRow rollup = Make.Leaf(target: "A", mixed: true, detail: [detail1, detail2]);
        MainViewModel vm = Vm(rollup);

        vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);
        vm.ToggleRollup(rollup);
        Assert.Equal(4, vm.Rows.Count);                       // group + rollup + 2 source lines

        vm.SortMode = SortMode.RemainingDesc;                 // wholesale rebuild
        Assert.True(rollup.IsExpanded);                       // remembered state restored
        Assert.Equal(4, vm.Rows.Count);
    }

    [Fact]
    public void SortMode_RemainingDesc_OrdersGroups()
    {
        MainViewModel vm = Vm(
            Make.Leaf(target: "A", desired: 10, disk: 2),     // remaining 8
            Make.Leaf(target: "B", desired: 30, disk: 5));    // remaining 25

        vm.SortMode = SortMode.RemainingDesc;
        Assert.Equal(["B", "A"], vm.Rows.OfType<TargetGroupRow>().Select(g => g.Target).ToArray());
    }

    private static MainViewModel Vm(params ReconciliationRow[] rows)
    {
        MainViewModel vm = new();
        vm.SetRowsForTest(rows);
        return vm;
    }
}
