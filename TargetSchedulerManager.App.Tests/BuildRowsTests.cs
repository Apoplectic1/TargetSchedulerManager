using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The loader's row shaping over the library cell projection — R1 moved the cell join into
/// <see cref="Astronomy.Catalog.Reconcile.ReconciliationProjection"/>; these pin the planes / rollups / hours
/// the App layers on top (still driven through the unchanged <c>BuildRows</c> entry point). Graph/report
/// builders mirror the library tests'.
/// </summary>
public class BuildRowsTests
{
    [Fact]
    public void BothCell_SameSeconds_MergesIntoSingleBothRow()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Both, r.Plane);
        Assert.Equal(300, r.PlanSeconds);
        Assert.Equal(300, r.DiskSeconds);
        Assert.False(r.SecondsMixed);
        Assert.Null(r.Detail);
        Assert.Equal(10, r.Desired);
        Assert.Equal(4, r.Disk);
        Assert.Equal(10 * 300 / 3600.0, r.PlanHours!.Value, precision: 10);
        Assert.Equal(4 * 300 / 3600.0, r.DiskHours!.Value, precision: 10);
        Assert.Equal(r.DiskHours - r.PlanHours, r.Hours);   // gap, additive convention
    }

    [Fact]
    public void BothCell_DifferentSeconds_MixedRollupWithPerSourceDetail()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 600.0)]),     // frames at a different sub length
            Report());

        ReconciliationRow rollup = Assert.Single(rows);
        Assert.True(rollup.SecondsMixed);
        Assert.NotNull(rollup.Detail);
        Assert.Equal(2, rollup.Detail!.Count);
        Assert.Equal(RowPlane.Ts, rollup.Detail[0].Plane);     // seconds ascending: 300 plan first
        Assert.Equal(300, rollup.Detail[0].PlanSeconds);
        Assert.Equal(RowPlane.Disk, rollup.Detail[1].Plane);
        Assert.Equal(600, rollup.Detail[1].DiskSeconds);
        Assert.All(rollup.Detail, d => Assert.True(d.IsDetail));

        // The rollup still aggregates both planes (counts + hours) even though the buckets never pair.
        Assert.Equal(10, rollup.Desired);
        Assert.Equal(4, rollup.Disk);
    }

    [Fact]
    public void OnePlaneTargets_EmitTsAndDiskRows()
    {
        Guid planned = Guid.NewGuid(), shot = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph(
                [T(planned, "Future", TargetSource.Planned), T(shot, "Done", TargetSource.Actual, dir: "Done")],
                [Plan(planned, tpl, desired: 24, seconds: 300.0)], [Tpl(tpl, "O", "O")],
                [Inv(shot, "L", FilterPurpose.Light, 12, 120.0)]),
            Report());

        ReconciliationRow ts = Assert.Single(rows, r => r.Target == "Future");
        Assert.Equal(RowPlane.Ts, ts.Plane);
        Assert.Equal(RowSource.TsOnly, ts.Source);
        Assert.Equal(-(24 * 300 / 3600.0), ts.Hours!.Value, precision: 10);   // a commitment is a deficit

        ReconciliationRow disk = Assert.Single(rows, r => r.Target == "Done");
        Assert.Equal(RowPlane.Disk, disk.Plane);
        Assert.Equal(RowSource.DiskOnly, disk.Source);
        Assert.Equal(12 * 120 / 3600.0, disk.Hours!.Value, precision: 10);
    }

    [Fact]
    public void TargetWithNoCells_EmitsPlaceholderRow()
    {
        Guid t = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Empty", TargetSource.Planned)], [], [], []), Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal("no data", r.Badge);
        Assert.Equal(RowPlane.Ts, r.Plane);
    }

    [Fact]
    public void UnanchoredPlannedTarget_GetsNoCoordsBadge()
    {
        Guid t = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "LBN 437", TargetSource.Planned)], [], [], []),
            Report(unanchored: [new UnanchoredTsTarget(null, "LBN 437")]));

        Assert.Equal("no-coords", Assert.Single(rows).Badge);
    }

    [Fact]
    public void MosaicFamily_PanelRowsTagged_ParentEmitsNothingItself()
    {
        Guid parent = Guid.NewGuid(), p1 = Guid.NewGuid(), p2 = Guid.NewGuid(), tpl = Guid.NewGuid();
        const string dir1 = "Mosaic - X/Panel 01of16", dir2 = "Mosaic - X/Panel 02of16";
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph(
                [
                    T(parent, "Mosaic - X", TargetSource.Both, dir: "Mosaic - X"),
                    T(p1, "X P1", TargetSource.Both, dir: dir1, parent: parent),
                    T(p2, "X P2", TargetSource.Actual, dir: dir2, parent: parent),
                ],
                [Plan(p1, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(p1, "H", FilterPurpose.Light, 4, 300.0), Inv(p2, "H", FilterPurpose.Light, 7, 300.0)]),
            Report());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Mosaic - X", r.Target));      // grid groups by the parent
        Assert.All(rows, r => Assert.Equal(RowSource.Both, r.Source));    // row Source = PARENT source
        Assert.All(rows, r => Assert.Contains("mosaic", r.Badge));

        ReconciliationRow both = Assert.Single(rows, r => r.PanelKey == dir1);
        Assert.Equal("Panel 01of16 · X P1", both.PanelLabel);             // both-names label on a Both panel
        Assert.Equal(RowSource.Both, both.PanelSource);

        ReconciliationRow actual = Assert.Single(rows, r => r.PanelKey == dir2);
        Assert.Equal("Panel 02of16", actual.PanelLabel);                  // single name when one-sided
        Assert.Equal(RowSource.DiskOnly, actual.PanelSource);
    }

    [Fact]
    public void Rows_SortByTargetFilterThenPlane_TsAboveDisk()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "O", "O")],
                [Inv(t, "B", FilterPurpose.Light, 3, 300.0), Inv(t, "O", FilterPurpose.Light, 2, 600.0)]),
            Report());

        // B (disk-only filter) sorts before O; O's plan and disk buckets disagree → mixed rollup.
        Assert.Equal(2, rows.Count);
        Assert.Equal("B", rows[0].Filter);
        Assert.Equal("O", rows[1].Filter);
        Assert.True(rows[1].SecondsMixed);
        Assert.Equal(RowPlane.Ts, rows[1].Detail![0].Plane);   // within a cell: TS above Disk
    }

    [Fact]
    public void OnePlanCell_CarriesPlanKey_AndIsDesiredEditable()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, tsKey: "ep-7")], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal("ep-7", r.PlanTsKey);       // the lone plan's write-back key
        Assert.True(r.CanEditDesired);
    }

    [Fact]
    public void DiskOnlyRow_HasNoPlanKey_AndIsNotEditable()
    {
        Guid t = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Done", TargetSource.Actual, dir: "Done")], [], [],
                [Inv(t, "L", FilterPurpose.Light, 12, 120.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Null(r.PlanTsKey);
        Assert.False(r.CanEditDesired);
    }

    [Fact]
    public void MixedRollup_AggregateNotEditable_ButEachSubLengthDetailIs()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, tsKey: "ep-a"),
                 Plan(t, tpl, desired: 5, seconds: 600.0, tsKey: "ep-b")],
                [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());

        ReconciliationRow rollup = Assert.Single(rows);
        Assert.True(rollup.SecondsMixed);
        Assert.Null(rollup.PlanTsKey);            // two plan sub-lengths → ambiguous, the sum isn't editable
        Assert.False(rollup.CanEditDesired);
        Assert.Contains(rollup.Detail!, d => d.PlanTsKey == "ep-a");   // but each sub-length addresses its plan
        Assert.Contains(rollup.Detail!, d => d.PlanTsKey == "ep-b");
    }

    [Fact]
    public void EditingDesiredInPlace_ReaggregatesTheGroupHeaderTotals()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, tsKey: "ep-1")], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());
        ReconciliationRow leaf = Assert.Single(rows);
        TargetGroupRow header = new("M 81", rows, isExpanded: false, isTargetEnabled: true);
        Assert.Equal(10, header.Desired);

        // What SetPlanDesiredAsync does after a verified write — no grid rebuild:
        leaf.ApplyDesired(25);
        header.Recompute();

        Assert.Equal(25, leaf.Desired);
        Assert.Equal(25 * 300 / 3600.0, leaf.PlanHours!.Value, precision: 10);   // derived hours follow
        Assert.Equal(25, header.Desired);                                        // the title row's total follows
        Assert.Equal(4, header.Disk);                                            // disk untouched by a desired edit
    }

    // ---- builders (mirroring Astronomy.Catalog.Tests') ----------------------

    private static CatalogGraph Graph(
        IReadOnlyList<Target> targets,
        IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates,
        IReadOnlyList<InventoryFilter> inventory) =>
        new(Profiles: [], Projects: [], templates, targets, plans, inventory);

    private static CatalogBuildReport Report(IReadOnlyList<UnanchoredTsTarget>? unanchored = null) =>
        new(0, 0, 0, 0, 0, [], [], [], [], unanchored ?? [], []);

    private static Target T(Guid id, string name, TargetSource source, string? dir = null, Guid? parent = null) => new(
        id, source, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null, Epoch.J2000,
        RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: dir, Catalog: null, CommonName: null,
        ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: null, ParentTargetId: parent);

    private static ExposureTemplate Tpl(Guid id, string name, string filter) =>
        new(id, Guid.NewGuid(), name, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: 300.0, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(Guid target, Guid template, int desired, double? seconds, string? tsKey = null) =>
        new(Guid.NewGuid(), target, template, seconds, desired, AcquiredCount: 0, AcceptedCount: 0,
            Enabled: true, ImportedFromTsGuid: tsKey);

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1,
            ExposureSeconds: seconds, Cameras: "Z533");
}
