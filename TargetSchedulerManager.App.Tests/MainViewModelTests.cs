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

    [Fact]
    public async Task SetPlanDesired_AppliedWrite_UpdatesLeafAndHeaderInPlace()
    {
        var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "10", true),
                                                     Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
        var gate = new TargetSchedulerManager.App.Shared.TsEditGate(SyncTestEnv.NewSync(out _), _ => ed);
        var vm = new MainViewModel(gate);
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planSeconds: 300, planTsKey: "ep-1");
        vm.SetRowsForTest([row]);
        vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);

        Assert.True(await vm.SetPlanDesiredAsync(row, 25));
        Assert.Equal(25, row.Desired);
        Assert.Equal(25, vm.Rows.OfType<TargetGroupRow>().Single().Desired);
    }

    [Fact]
    public async Task SetPlanEnabled_AppliedWrite_MirrorsCheckboxInPlace()
    {
        var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "1", true),
                                                     Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
        var gate = new TargetSchedulerManager.App.Shared.TsEditGate(SyncTestEnv.NewSync(out _), _ => ed);
        var vm = new MainViewModel(gate);
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planSeconds: 300, planTsKey: "ep-1", planEnabled: true);
        vm.SetRowsForTest([row]);

        Assert.True(await vm.SetPlanEnabledAsync(row, false));
        Assert.False(row.IsPlanEnabled);                        // in-place mirror, no reload
        Assert.True(row.PlanEnableVisibility == Microsoft.UI.Xaml.Visibility.Visible);
    }

    [Fact]
    public async Task SetPlanExposure_AppliedWrite_MirrorsSecondsAndHoursInPlace()
    {
        var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "-1", true),
                                                     Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
        var gate = new TargetSchedulerManager.App.Shared.TsEditGate(SyncTestEnv.NewSync(out _), _ => ed);
        var vm = new MainViewModel(gate);
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planSeconds: 300, planTsKey: "ep-1");
        vm.SetRowsForTest([row]);
        vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);

        Assert.True(await vm.SetPlanExposureAsync(row, 301, mirrorSeconds: 301));
        Assert.Equal(301, row.PlanSeconds);
        Assert.Equal("301", row.SecondsText);
        Assert.Equal(10 * 301.0 / 3600.0, row.PlanHours!.Value, 6);

        // Null mirror (reverting an already-overridden plan): the effective value resolves from the db
        // (plan→template join) so the cell still mirrors immediately.
        ed.EffectiveExposure = (true, 300.0);
        Assert.True(await vm.SetPlanExposureAsync(row, -1, mirrorSeconds: null));
        Assert.Equal(300, row.PlanSeconds);

        // Resolution unavailable (e.g. template row missing): display left for the next reload.
        ed.EffectiveExposure = (false, null);
        Assert.True(await vm.SetPlanExposureAsync(row, -1, mirrorSeconds: null));
        Assert.Equal(300, row.PlanSeconds);
    }

    [Fact]
    public async Task SetPlanExposure_ZeroIsLiteral_MirrorsZeroInPlace()
    {
        // 0 is a literal zero-second exposure (Library adjudication 2026-07-07: TS's planner defers only
        // on -1), so a resolved effective 0 mirrors the cell — it is not "unknown".
        var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "300", true),
                                                     Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
        var gate = new TargetSchedulerManager.App.Shared.TsEditGate(SyncTestEnv.NewSync(out _), _ => ed);
        var vm = new MainViewModel(gate);
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planSeconds: 300, planTsKey: "ep-1");
        vm.SetRowsForTest([row]);
        vm.ToggleGroup((TargetGroupRow)vm.Rows[0]);

        ed.EffectiveExposure = (true, 0.0);
        Assert.True(await vm.SetPlanExposureAsync(row, 0, mirrorSeconds: null));
        Assert.Equal(0, row.PlanSeconds);
        Assert.Equal("0", row.SecondsText);
    }

    [Fact]
    public async Task MosaicEnable_FansOutToEveryPanel_AndAggregatesState()
    {
        var ed = new TsEditGateTests_Stub { Next = (new Astronomy.Catalog.TargetScheduler.FieldEditResult(true, "1", true),
                                                     Astronomy.Catalog.TargetScheduler.RefusalReason.None) };
        var gate = new TargetSchedulerManager.App.Shared.TsEditGate(SyncTestEnv.NewSync(out _), _ => ed);
        var vm = new MainViewModel(gate);

        ReconciliationRow leaf1 = Make.Leaf(target: "M 101", desired: 10, tsTargetKey: "p1", enabled: true);
        ReconciliationRow leaf2 = Make.Leaf(target: "M 101", desired: 10, tsTargetKey: "p2", enabled: false);
        PanelGroupRow panel1 = new("M 101", "P1", "Panel 01", Models.RowSource.Both, [leaf1], false);
        PanelGroupRow panel2 = new("M 101", "P2", "Panel 02", Models.RowSource.Both, [leaf2], false);
        TargetGroupRow mosaic = new("M 101", [leaf1, leaf2], false, true, panels: [panel1, panel2]);

        Assert.Null(vm.GetMosaicEnabledState(mosaic));                    // mixed → indeterminate

        Assert.True(await vm.SetMosaicEnabledAsync(mosaic, true));        // fan-out enables both
        Assert.Equal(true, vm.GetMosaicEnabledState(mosaic));             // pending edits now agree

        Assert.True(await vm.SetMosaicEnabledAsync(mosaic, false));
        Assert.Equal(false, vm.GetMosaicEnabledState(mosaic));
    }

    private static MainViewModel Vm(params ReconciliationRow[] rows)
    {
        MainViewModel vm = new();
        vm.SetRowsForTest(rows);
        return vm;
    }
}

internal sealed class TsEditGateTests_Stub : TargetSchedulerManager.App.Shared.ITsEditor
{
    public (Astronomy.Catalog.TargetScheduler.FieldEditResult? Result, Astronomy.Catalog.TargetScheduler.RefusalReason Refusal) Next;
    public (Astronomy.Catalog.TargetScheduler.FieldEditResult? Result, Astronomy.Catalog.TargetScheduler.RefusalReason Refusal) TrySetField(
        Astronomy.Catalog.TargetScheduler.TsTable table, string tsKey, string column, object? value) => Next;
    public (bool Found, object? Value) ReadField(
        Astronomy.Catalog.TargetScheduler.TsTable table, string tsKey, string column) => (false, null);
    public bool IsFieldAvailable(Astronomy.Catalog.TargetScheduler.TsTable table, string column) => true;
    public (bool Found, double? Value) EffectiveExposure = (false, null);
    public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => EffectiveExposure;
    public void Dispose() { }
}
