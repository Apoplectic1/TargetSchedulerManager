using TargetCatalogManager.App.Models;
using TargetCatalogManager.App.ViewModels.Rows;
using Xunit;

namespace TargetCatalogManager.App.Tests;

/// <summary>The Hours sign convention, the "mixed" pill, and the search rule — the row logic the review
/// flagged as untested. Brush properties are NOT touched here (they need the app's theme resources).</summary>
public class ReconciliationRowTests
{
    [Fact]
    public void Hours_TsRow_IsNegativeCommitment()
    {
        Assert.Equal(-2.5, Make.Ts(desired: 30, seconds: 300).Hours);   // 30 × 300 s = 2.5 h deficit
    }

    [Fact]
    public void Hours_DiskRow_IsPositiveActual()
    {
        Assert.Equal(0.5, Make.Disk(frames: 6, seconds: 300).Hours);
    }

    [Fact]
    public void Hours_BothRow_IsDiskMinusPlanGap()
    {
        ReconciliationRow r = Make.Leaf(planHours: 2.0, diskHours: 1.5);
        Assert.Equal(-0.5, r.Hours!.Value, precision: 10);
    }

    [Fact]
    public void HoursText_PositiveGap_GetsPlusPrefix()
    {
        Assert.Equal("+0.5", Make.Leaf(planHours: 1.0, diskHours: 1.5).HoursText);
        Assert.Equal("-0.5", Make.Leaf(planHours: 1.5, diskHours: 1.0).HoursText);
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
}

/// <summary>The additive rules every header level shares.</summary>
public class RowAggregatesTests
{
    [Fact]
    public void Compute_SumsColumns_AndHoursDeltaFromComponents()
    {
        RowAggregates a = RowAggregates.Compute(
        [
            Make.Ts(desired: 12, seconds: 300),                       // plan 1.0 h
            Make.Disk(frames: 6, seconds: 300),                       // disk 0.5 h
            Make.Leaf(desired: 6, acquired: 3, accepted: 2, disk: 3,
                planHours: 0.5, diskHours: 0.25),
        ]);

        Assert.Equal(18, a.Desired);
        Assert.Equal(3, a.Acquired);
        Assert.Equal(2, a.Accepted);
        Assert.Equal(9, a.Disk);
        Assert.Equal(-0.75, a.HoursDelta!.Value, precision: 10);      // (0.5+0.25) − (1.0+0.5)
    }

    [Fact]
    public void Compute_RemainingIsPerRowShortfall_OvershootNeverMasks()
    {
        RowAggregates a = RowAggregates.Compute(
        [
            Make.Leaf(filter: "H", desired: 10, disk: 2),    // short 8
            Make.Leaf(filter: "O", desired: 5, disk: 9),     // overshot — contributes 0, not −4
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
