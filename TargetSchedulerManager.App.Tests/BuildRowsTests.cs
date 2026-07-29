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
    // ---- capture configuration: pairing, separation, and what the reader sees ----------------------------

    [Theory]
    // A plan pairs with captured frames only when the whole capture configuration agrees. Each row varies one
    // dimension of the disk side away from the plan's 100 / 50 / bin 1.
    [InlineData(53, 50, 1)]    // the 2024 broadband gain switch
    [InlineData(100, 10, 1)]   // the offset-50 frames scattered through every filter
    [InlineData(100, 50, 2)]   // bin 2 frames do not stack with bin 1
    public void ConfigurationDiffers_SeparatesIntoTsAndDiskRows(int diskGain, int diskOffset, int diskBin)
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, gain: diskGain, offset: diskOffset, bin: diskBin)]),
            Report());

        // The filter still has both planes, so its rollup stays "Both" — but it keeps its disclosure, and
        // beneath it the planes are separate. Never one merged line asserting the frames satisfy the plan.
        ReconciliationRow rollup = Assert.Single(rows);
        Assert.True(rollup.SecondsMixed);
        Assert.NotNull(rollup.Detail);
        Assert.DoesNotContain(rollup.Detail!, r => r.Plane == RowPlane.Both);
        ReconciliationRow ts = Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Ts);
        ReconciliationRow disk = Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Disk);
        Assert.Equal(10, ts.Desired);
        Assert.Equal(4, disk.Disk);
        Assert.False(rollup.CanEditDesired);   // the sum of unmatched planes is not an inline edit target
    }

    [Fact]
    public void ConfigurationAgrees_PairsAndTheRowShowsIt()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, camera: "Z183")]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Both, r.Plane);
        Assert.Equal("Z183", r.CameraText);
        Assert.Equal("100", r.GainText);
        Assert.Equal("50", r.OffsetText);
        Assert.Equal("1", r.BinText);
    }

    [Fact]
    public void CameraDifference_NeverPreventsPairing()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        // A plan cannot name a camera, so the camera must not enter the pairing test.
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, camera: "Z533")]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Both, r.Plane);
        Assert.Equal("Z533", r.CameraText);
    }

    [Fact]
    public void TsRow_ShowsNoCameraButKeepsTheTemplatesConfiguration()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Planned)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")], []),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Ts, r.Plane);
        Assert.Equal(Format.Dash, r.CameraText);   // TS cannot express a camera
        Assert.Equal("100", r.GainText);           // but the template's configuration is real
    }

    [Fact]
    public void SeparatedPlanRow_StaysInlineEditable()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        // A plan that separated from its frames is still exactly one plan, so its desired stays editable.
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, tsKey: "77")], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, gain: 53)]),
            Report());

        ReconciliationRow rollup = Assert.Single(rows);
        ReconciliationRow ts = Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Ts);
        Assert.Equal("77", ts.PlanTsKey);
        Assert.True(ts.CanEditDesired);
    }

    [Theory]
    [InlineData("Misc", Badges.UnknownCamera)]     // a directory naming no camera we know
    public void UnknownCameraDirectory_FlagsOnlyTheRowsDrawingOnIt(string camera, string expected)
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid(), tpl2 = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0), Plan(t, tpl2, desired: 10, seconds: 300.0)],
                [Tpl(tpl, "H", "H"), Tpl(tpl2, "L", "L")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, camera: camera),
                 Inv(t, "L", FilterPurpose.Light, 4, 300.0, camera: "Z183")]),
            Report());

        ReconciliationRow bad = Assert.Single(rows, r => r.Filter == "H");
        ReconciliationRow good = Assert.Single(rows, r => r.Filter == "L");
        Assert.Contains(expected, bad.Badge);
        Assert.True(bad.IsFlagged);
        Assert.DoesNotContain(expected, good.Badge);   // never spreads to a sibling row
        Assert.Equal("Misc", bad.CameraText);          // the offending name stays readable
    }

    [Fact]
    public void FramesRecordingAnotherCamera_FlagTheRow()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, disagrees: true)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Contains(Badges.CameraMismatch, r.Badge);
        Assert.True(r.IsFlagged);
    }

    [Fact]
    public void RollupOverDifferingConfigurations_ReadsMixed()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        // One plan, and frames from two gain eras at a different sub length — a mixed rollup whose source
        // lines disagree on gain but share camera and binning.
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0),
                 Inv(t, "H", FilterPurpose.Light, 6, 600.0, gain: 53)]),
            Report());

        ReconciliationRow rollup = Assert.Single(rows, r => r.Detail is not null);
        Assert.Equal(Format.Mixed, rollup.GainText);    // the dimension that differs
        Assert.Equal("Z533", rollup.CameraText);        // the ones that do not still report their value
        Assert.Equal("1", rollup.BinText);
    }

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
        Assert.False(r.IsFlagged);      // coordinates are fine — queued work, not broken authoring
    }

    [Fact]
    public void UnanchoredPlannedTarget_GetsNoCoordsBadge_AndIsFlagged()
    {
        Guid t = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "LBN 437", TargetSource.Planned)], [], [], []),
            Report(unanchored: [new UnanchoredTsTarget(null, "LBN 437")]));

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal("no-coords", r.Badge);
        // TS can't schedule a coordinate-less target: repairable authoring, so the flagged-only filter keeps it.
        Assert.True(r.IsFlagged);
    }

    [Fact]
    public void UnanchoredTargetWithAPlan_FlagsItsCellRows()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "LBN 437", TargetSource.Planned)],
                [Plan(t, tpl, desired: 20, seconds: 300.0)], [Tpl(tpl, "H", "H")], []),
            Report(unanchored: [new UnanchoredTsTarget(null, "LBN 437")]));

        // With a plan the target HAS cells, so it takes the badge path rather than the no-cells fallback.
        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal("no-coords", r.Badge);
        Assert.True(r.IsFlagged);
        Assert.Equal(20, r.Desired);
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
    public void MixedRollup_SinglePlan_KeepsFlyoutKeyButDesiredEditsOnlyAtTheDetailLine()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 900.0, tsKey: "ep-1")], [Tpl(tpl, "O", "O")],
                [Inv(t, "O", FilterPurpose.Light, 4, 300.0)]),
            Report());

        // One plan sub-length + a different disk sub-length → mixed rollup. The plan key stays on the
        // rollup (flyout gesture), but the inline Desired box moves down to the plan's own detail line
        // so each plan is editable in exactly one place.
        ReconciliationRow rollup = Assert.Single(rows);
        Assert.True(rollup.SecondsMixed);
        Assert.Equal("ep-1", rollup.PlanTsKey);
        Assert.False(rollup.CanEditDesired);
        ReconciliationRow ts = Assert.Single(rollup.Detail!, d => d.Plane == RowPlane.Ts);
        Assert.True(ts.CanEditDesired);
    }

    [Fact]
    public void MixedRollup_NestedBothDetail_IsTheEditablePlace()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 33, seconds: 60.0, tsKey: "ep-1")], [Tpl(tpl, "R", "R")],
                [Inv(t, "R", FilterPurpose.Light, 1, 30.0), Inv(t, "R", FilterPurpose.Light, 19, 60.0)]),
            Report());

        // Plan and disk agree at 60s while a stray 30s disk bucket makes the rollup mixed: the plan's
        // detail line renders as a nested Both, and that line — not the rollup — carries the edit box.
        ReconciliationRow rollup = Assert.Single(rows);
        Assert.True(rollup.SecondsMixed);
        Assert.False(rollup.CanEditDesired);
        ReconciliationRow both = Assert.Single(rollup.Detail!, d => d.Plane == RowPlane.Both);
        Assert.True(both.CanEditDesired);
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

    [Fact]
    public void PlanAcceptedNotEqualAcquired_GetsAccNeAcqBadge_AndIsFlagged()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, acquired: 10, accepted: 12)],   // TS drift: acc > acq
                [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Contains("acc≠acq", r.Badge);
        Assert.True(r.IsFlagged);
    }

    [Fact]
    public void PlanAcceptedEqualsAcquired_HasNoAccNeAcqBadge()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0, acquired: 8, accepted: 8)],     // healthy: acc == acq
                [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.DoesNotContain("acc≠acq", r.Badge);
        Assert.False(r.IsFlagged);
    }

    [Fact]
    public void Rows_OrderTargetsNaturally()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(a, "IC 1318", TargetSource.Actual, dir: "IC 1318"),
                   T(b, "IC 405", TargetSource.Actual, dir: "IC 405")],
                [], [],
                [Inv(a, "H", FilterPurpose.Light, 1, 300.0), Inv(b, "H", FilterPurpose.Light, 1, 300.0)]),
            Report());

        // Natural order: IC 405 precedes IC 1318 (an ordinal compare would put 1318 first).
        Assert.Equal(2, rows.Count);
        Assert.Equal("IC 405", rows[0].Target);
        Assert.Equal("IC 1318", rows[1].Target);
    }

    // ---- framing (openspec rotation-framing-key) -----------------------------

    [Fact]
    public void FramingDisagreement_SeparatesRows_AndBadgesTheStray()
    {
        // The Barnard 202 shape: plan @50°, clusters @50° (28) and @60° (451). The agreeing minority pairs;
        // the majority renders as a Disk row carrying the warning `framing` badge — findable by filter.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Barnard 202", TargetSource.Both, dir: "Barnard 202", rotation: 50.0)],
                [Plan(t, tpl, desired: 100, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 28, 300.0, framingOrdinal: 1, rotationFold: 50.0),
                    Inv(t, "H", FilterPurpose.Light, 451, 300.0, framingOrdinal: 0, rotationFold: 60.0),
                ]),
            Report());

        ReconciliationRow rollup = Assert.Single(rows);
        Assert.NotNull(rollup.Detail);
        ReconciliationRow both = Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Both);
        Assert.Equal(28, both.Disk);
        Assert.Equal("50°", both.RotText);
        Assert.DoesNotContain(Badges.Framing, both.Badge);

        ReconciliationRow stray = Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Disk);
        Assert.Equal(451, stray.Disk);
        Assert.Equal("60°", stray.RotText);
        Assert.Contains(Badges.Framing, stray.Badge);        // the triggering line carries the badge
        Assert.True(stray.IsFlagged);
        Assert.Equal("mixed", rollup.RotText);               // the rollup names the dimension responsible

        // The badge sits at the deepest VISIBLE level (user obs 6b72 + 8be0): collapsed, the rollup shows
        // it (the triggering line is hidden); expanded, the line shows it and the rollup hands it down.
        // Badge (the full union — what headers aggregate and the filter reasons over) always carries it.
        Assert.Contains(Badges.Framing, rollup.Badge);
        Assert.True(rollup.IsFlagged);
        Assert.Contains(Badges.Framing, rollup.BadgeText);   // collapsed
        rollup.IsExpanded = true;
        Assert.DoesNotContain(Badges.Framing, rollup.BadgeText);   // expanded — the line beneath shows it
        Assert.Contains(Badges.Framing, RowAggregates.Compute([rollup]).Badge);   // header union unaffected
    }

    [Fact]
    public void AgreeingFraming_RendersIdenticallyOnBothPlanes_NoBadge()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Pacman", TargetSource.Both, dir: "NGC 281 - Pacman", rotation: 147.01)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 91, 300.0, rotationFold: 147.0)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Both, r.Plane);
        Assert.Equal("147°", r.RotText);
        Assert.DoesNotContain(Badges.Framing, r.Badge);
        Assert.False(r.IsFlagged);
    }

    [Fact]
    public void MechanicalRotation_IsMarked_NeverBadged_NeverPreventsPairing()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Leo Triplet", TargetSource.Both, dir: "Leo Triplet", rotation: 110.0)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 265, 300.0,
                    rotation: RotationExpression.Mechanical, rotationFold: 172.3)]),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Both, r.Plane);
        Assert.Equal("172.3°m", r.RotText);                  // visibly mechanical — never dressed as sky
        Assert.DoesNotContain(Badges.Framing, r.Badge);
    }

    [Fact]
    public void TsRow_ShowsTheTargetsOwnRotation_Folded()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        // Plan rotation 345.43° folds to 165.43° — the TS row reads the same way an agreeing disk row would.
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Jellyfish", TargetSource.Planned, rotation: 345.43)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")], []),
            Report());

        ReconciliationRow r = Assert.Single(rows);
        Assert.Equal(RowPlane.Ts, r.Plane);
        Assert.Equal("165.4°", r.RotText);
    }

    [Fact]
    public void RowScopedBadges_AllFollowTheDeepestVisibleLevel()
    {
        // The deepest-visible-level rule is general (user 2026-07-29): camera provenance behaves exactly
        // like framing. A rollup over a cam≠ disk line shows the token collapsed, hands it down expanded;
        // Badge (the full union) carries it throughout for header aggregation and the flagged filter.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, gain: 53, disagrees: true)]),   // gain splits planes
            Report());

        ReconciliationRow rollup = Assert.Single(rows, r => r.Detail is not null);
        Assert.Contains(Badges.CameraMismatch, rollup.Badge);
        Assert.Contains(Badges.CameraMismatch, rollup.BadgeText);        // collapsed: line hidden → shown here
        rollup.IsExpanded = true;
        Assert.DoesNotContain(Badges.CameraMismatch, rollup.BadgeText);  // expanded: the line shows it
        Assert.Contains(Badges.CameraMismatch,
            Assert.Single(rollup.Detail!, r => r.Plane == RowPlane.Disk).Badge);
        Assert.Contains(Badges.CameraMismatch, RowAggregates.Compute([rollup]).Badge);
    }

    [Fact]
    public void RollupDash_NeverCountsAsDisagreement()
    {
        // The Barnard 202 B-Light shape (user obs 0d19, 2026-07-29): a TS line and a Disk line under one
        // rollup. The TS row cannot express a camera (dash), and the dash means "nothing to say" — so the
        // rollup's camera is the one expressed value (Z533), not `mixed`. Likewise rotation: the plan
        // carries none here, so the rollup shows the disk framing's angle.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "Barnard 202", TargetSource.Both, dir: "Barnard 202")],
                [Plan(t, tpl, desired: 64, seconds: 300.0)], [Tpl(tpl, "B", "B")],
                [Inv(t, "B", FilterPurpose.Light, 64, 300.0, gain: 53)]),   // plan gain 100 → planes separate
            Report());

        ReconciliationRow rollup = Assert.Single(rows, r => r.Detail is not null);
        Assert.Equal(Format.Mixed, rollup.GainText);    // two expressed gains genuinely disagree
        Assert.Equal("Z533", rollup.CameraText);        // dash (TS) + Z533 (Disk) → Z533, never mixed
        Assert.Equal("20°", rollup.RotText);            // dash (no plan rotation) + 20° (Disk) → 20°
    }

    [Fact]
    public void UnknownRotation_ShowsTheDash()
    {
        Guid t = Guid.NewGuid();
        List<ReconciliationRow> rows = ReconciliationLoader.BuildRows(
            Graph([T(t, "NGC 4449", TargetSource.Actual, dir: "NGC 4449")], [], [],
                [Inv(t, "H", FilterPurpose.Light, 6, 300.0,
                    rotation: RotationExpression.Unknown, rotationFold: null)]),
            Report());

        Assert.Equal(Format.Dash, Assert.Single(rows).RotText);
    }

    // ---- builders (mirroring Astronomy.Catalog.Tests') ----------------------

    private static CatalogGraph Graph(
        IReadOnlyList<Target> targets,
        IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates,
        IReadOnlyList<InventoryFilter> inventory) =>
        new(Profiles: [], Projects: [], templates, targets, plans, inventory);

    private static CatalogBuildReport Report(IReadOnlyList<UnanchoredTsTarget>? unanchored = null) =>
        new(0, 0, 0, 0, 0, [], [], [], unanchored ?? [], []);

    private static Target T(Guid id, string name, TargetSource source, string? dir = null, Guid? parent = null,
        double? rotation = null) => new(
        id, source, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null, Epoch.J2000,
        RotationDeg: rotation, RoiPercent: null, Priority: null, DirectoryName: dir, Catalog: null, CommonName: null,
        ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: null, ParentTargetId: parent);

    private static ExposureTemplate Tpl(Guid id, string name, string filter) =>
        new(id, Guid.NewGuid(), name, filter, Gain: 100, OffsetAdu: 50, Binning: 1, ReadoutMode: null,
            DefaultExposureSeconds: 300.0, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(Guid target, Guid template, int desired, double? seconds, string? tsKey = null,
        int acquired = 0, int accepted = 0) =>
        new(Guid.NewGuid(), target, template, seconds, desired, AcquiredCount: acquired, AcceptedCount: accepted,
            Enabled: true, ImportedFromTsGuid: tsKey);

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds,
        int gain = 100, int offset = 50, int bin = 1, string camera = "Z533", bool disagrees = false,
        int framingOrdinal = 0, RotationExpression rotation = RotationExpression.Sky,
        double? rotationFold = 20.0) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: gain, TypicalOffset: offset, TypicalSetTempC: -10.0, TypicalBinningX: bin,
            TypicalBinningY: bin, ExposureSeconds: seconds, Camera: camera,
            FramingOrdinal: framingOrdinal, RotationExpression: rotation, RotationFoldDeg: rotationFold,
            CameraDisagrees: disagrees);
}
