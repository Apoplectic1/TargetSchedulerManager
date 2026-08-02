using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>The Hours sign convention, the "mixed" pill, and the search rule — the row logic the review
/// flagged as untested. Brush properties are NOT touched here (they need the app's theme resources).</summary>
public class ReconciliationRowTests
{
    // ---- The Hours gauge (obs 01b7): time still owed (negative) or captured total (positive) ----------

    [Fact]
    public void Hours_TsRow_IsRemainingNotFullCommitment()
    {
        // Acquired counts against the debt: 30 desired − 0 acquired = full 2.5 h owed…
        Assert.Equal(-2.5, Make.Ts(desired: 30, seconds: 300).Hours);
        // …and a complete plan owes nothing — dash, not 0: its frames live on the disk sibling.
        Assert.Null(Make.Leaf(plane: RowPlane.Ts, desired: 30, acquired: 30, disk: 0, diskHours: null).Hours);
    }

    [Fact]
    public void Hours_DiskRow_IsPositiveActual()
    {
        Assert.Equal(0.5, Make.Disk(frames: 6, seconds: 300).Hours);
    }

    [Fact]
    public void Hours_BothRow_Incomplete_ShowsRemaining_NotTheDiskGap()
    {
        // The M81-R shape: disk "full" (10 frames vs 10 desired) but only 4 acquired serve the plan —
        // the gauge reads the 6 subs still owed, where the old disk−desired gap read 0 ("met").
        ReconciliationRow r = Make.Leaf(desired: 10, acquired: 4, disk: 10, planSeconds: 300,
            planHours: 10 * 300 / 3600.0, diskHours: 10 * 300 / 3600.0);
        Assert.Equal(-0.5, r.Hours!.Value, precision: 10);
        Assert.Equal("-0.5", r.HoursText);
    }

    [Fact]
    public void Hours_BothRow_Complete_ShowsTotalDisk_NotSurplus()
    {
        // 12 frames against 10 desired, all acquired: the value is the captured 1.0 h total — plain, no
        // "+" — not the old +0.17 surplus.
        ReconciliationRow r = Make.Leaf(desired: 10, acquired: 10, disk: 12, planSeconds: 300,
            planHours: 10 * 300 / 3600.0, diskHours: 12 * 300 / 3600.0);
        Assert.Equal(1.0, r.Hours!.Value, precision: 10);
        Assert.Equal("1.0", r.HoursText);
    }

    [Fact]
    public void Hours_DesiredZeroPlan_KeepsItsTripwireZero()
    {
        // A desired-0 plan is data that shouldn't exist — 0.0 (with the critical fill), never the dash.
        Assert.Equal(0.0, Make.Leaf(plane: RowPlane.Ts, desired: 0, acquired: 0, disk: 0, diskHours: null).Hours);
    }

    [Fact]
    public void ApplyDesired_RecomputesTheRemainingDebt()
    {
        ReconciliationRow r = Make.Leaf(desired: 10, acquired: 4, disk: 10, planSeconds: 300,
            planTsKey: "42", diskHours: 10 * 300 / 3600.0);
        r.ApplyDesired(4);                                   // goal lowered to what's acquired
        Assert.Equal("0.8", r.HoursText);                    // debt cleared → captured total (10 × 300 s)
        r.ApplyDesired(16);
        Assert.Equal("-1.0", r.HoursText);                   // 12 owed again
    }

    [Fact]
    public void SecondsText_MixedRollup_ReadsMixed()
    {
        Assert.Equal("mixed", Make.Leaf(mixed: true, detail: [Make.Ts(), Make.Disk()]).SecondsText);
        Assert.Equal("300", Make.Leaf().SecondsText);
        Assert.Equal("—", Make.Ts(seconds: 0).SecondsText);
    }

    [Fact]
    public void ChevronGlyph_OnlyOnExpandableRollups()
    {
        ReconciliationRow rollup = Make.Leaf(mixed: true, detail: [Make.Ts(), Make.Disk()]);
        Assert.Equal("\uE76C", rollup.ChevronGlyph);   // collapsed: ChevronRight
        rollup.IsExpanded = true;
        Assert.Equal("\uE70D", rollup.ChevronGlyph);   // expanded: ChevronDown
        Assert.Equal("", Make.Leaf().ChevronGlyph);    // plain row: none
    }

    [Fact]
    public void Matches_SearchesTargetFilterBadgeAndPanelLabel_CaseInsensitive()
    {
        ReconciliationRow r = Make.Leaf(
            target: "Mosaic - Cygnus Loop", filter: "H", badge: "mosaic",
            panelKey: "Mosaic - Cygnus Loop/Panel 01of16", panelLabel: "Panel 01of16 · CygnusLoop P1");

        Assert.True(r.Matches("cygnus"));
        Assert.True(r.Matches("MOSAIC"));        // badge
        Assert.True(r.Matches("panel 01"));      // panel label
        Assert.False(r.Matches("witch head"));
    }

    [Fact]
    public void SourceMargin_IndentLadder_RollupPlainDetailPlusPanelShift()
    {
        Assert.Equal(18, Make.Leaf(mixed: true, detail: [Make.Ts()]).SourceMargin.Left);
        Assert.Equal(36, Make.Leaf().SourceMargin.Left);
        Assert.Equal(50, Make.Leaf(isDetail: true).SourceMargin.Left);
        Assert.Equal(50, Make.Leaf(panelKey: "p").SourceMargin.Left);            // 36 + 14 under a panel
        Assert.Equal(64, Make.Leaf(isDetail: true, panelKey: "p").SourceMargin.Left);
    }
}

public class FormatTests
{
    [Theory]
    [InlineData(0.0, "0.0")]
    [InlineData(0.03, "0.03")]     // tiny non-zero keeps two decimals ("0.0" would read as missing)
    [InlineData(-0.04, "-0.04")]
    [InlineData(2.34, "2.3")]
    [InlineData(-10.26, "-10.3")]
    public void Hours_F1ExceptTinyNonZero(double value, string expected) =>
        Assert.Equal(expected, Format.Hours(value));

    [Fact]
    public void Hours_NegativeZero_NormalizesToPlainZero() =>
        Assert.Equal("0.0", Format.Hours(-0.0));   // a negated empty commitment must not read "-0.0"

    [Fact]
    public void When_FullStampAlways_LocalTime()
    {
        DateTimeOffset at = new(new DateTime(2026, 8, 2, 13, 9, 0, DateTimeKind.Local));
        Assert.Equal("26/08/02 01:09 PM", Format.When(at));
    }
}

/// <summary>The additive rules every header level shares.</summary>
public class RowAggregatesTests
{
    [Fact]
    public void Compute_SumsColumns_AndTheGaugeComponents()
    {
        RowAggregates a = RowAggregates.Compute(
        [
            Make.Ts(desired: 12, seconds: 300),                       // owes 1.0 h (nothing acquired)
            Make.Disk(frames: 6, seconds: 300),                       // captured 0.5 h, owes nothing
            Make.Leaf(desired: 6, acquired: 3, accepted: 2, disk: 3,
                planSeconds: 300, planHours: 0.5, diskHours: 0.25),   // owes 3 × 300 s = 0.25 h
        ]);

        Assert.Equal(18, a.Desired);
        Assert.Equal(3, a.Acquired);
        Assert.Equal(2, a.Accepted);
        Assert.Equal(9, a.Disk);
        // The gauge's two additive components, carried separately: the debt beneath and the captured total.
        Assert.Equal(1.25, a.RemainingHours!.Value, precision: 10);
        Assert.Equal(0.75, a.DiskHours!.Value, precision: 10);
    }

    [Fact]
    public void Compute_RemainingIsAcquiredBased_OvershootNeverMasks()
    {
        // Same acquired basis as the Hours gauge (obs 01b7): disk frames that don't serve never reduce the
        // debt, and one cell's overshoot never masks another's shortfall.
        RowAggregates a = RowAggregates.Compute(
        [
            Make.Leaf(filter: "H", desired: 10, acquired: 2, disk: 10),   // short 8 despite a "full" disk
            Make.Leaf(filter: "O", desired: 5, acquired: 9, disk: 9),     // overshot — contributes 0, not −4
        ]);

        Assert.Equal(8, a.Remaining);
    }

    [Fact]
    public void Compute_AllDiskRows_PlannedColumnsNull()
    {
        RowAggregates a = RowAggregates.Compute([Make.Disk(frames: 4), Make.Disk(filter: "O", frames: 2)]);

        Assert.Null(a.Desired);
        Assert.Null(a.Acquired);
        Assert.Equal(6, a.Disk);
    }

    [Fact]
    public void Compute_BadgeUnionDistinct_AndFlaggedPropagates()
    {
        RowAggregates a = RowAggregates.Compute(
        [
            Make.Leaf(badge: "mosaic"),
            Make.Leaf(badge: "mosaic"),
            Make.Leaf(badge: "name≠", flagged: true),
        ]);

        Assert.Equal("mosaic · name≠", a.Badge);
        Assert.True(a.IsFlagged);
    }
}
