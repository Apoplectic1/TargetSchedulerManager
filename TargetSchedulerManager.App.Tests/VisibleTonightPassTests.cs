using Astronomy.Catalog.TargetScheduler;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Time;
using TargetSchedulerManager.App.Services;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The Visible-tonight planner in isolation: pure records in, edits + counts out. Real astronomy (the
// library computes tonight's window and each verdict) over a fixed site + instant so every verdict is
// deterministic: Penns Park latitude (~40.3°N), mid-night 2026-01-15 02:00 UTC (~21:00 EST Jan 14).
public class VisibleTonightPassTests
{
    private static readonly Location Site = new(
        "Test site", latitude: 40.282835, north: true, longitude: 74.997369, west: true,
        timeZoneInfo: TimeZoneInfo.Utc);

    private static readonly DateTime UtcNow = new(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ThirtyMinutes = TimeSpan.FromMinutes(30);

    // At +40.3° latitude: dec +89 is circumpolar (up all night — always a ≥ 30-min window);
    // dec -80 never rises (limit is about -49.7°).
    private const double CircumpolarDec = 89.0;
    private const double NeverRisesDec = -80.0;

    private const int Draft = 0, Active = 1, Inactive = 2, Closed = 3;

    // ---- predicate ---------------------------------------------------------------------------------

    [Fact]
    public void CircumpolarTarget_DisabledInActiveProject_GetsEnableEdit()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        VisibleTonightEdit edit = Assert.Single(plan.Edits);
        Assert.Equal((TsTable.Target, "active", 1), (edit.Table, edit.Column, edit.Value));
        Assert.Equal(1, plan.TargetsEnabled);
        Assert.Equal(0, plan.TargetsDisabled);
    }

    [Fact]
    public void NeverRisesTarget_Enabled_GetsDisableEdit()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active)],
            [Target(10, 1, NeverRisesDec, active: 1)]);

        Assert.Contains(plan.Edits, e => e is { Table: TsTable.Target, Column: "active", Value: 0 });
        Assert.Equal(1, plan.TargetsDisabled);
    }

    [Fact]
    public void MinimumDuration_GatesASingleContiguousWindow()
    {
        // A target transiting mid-night with a ~2 h total above-horizon arc (dec ≈ -48.7° at this
        // latitude): visible under a 30-min bar, not visible when the bar exceeds the whole arc.
        NightWindow night = NightCalculator.ComputeNight(Site, UtcNow);
        DateTime midNight = night.AstronomicalDusk + (night.AstronomicalDawn - night.AstronomicalDusk) / 2;
        double raAtMidNight = SiderealTime.Local(midNight, longitudeDegEast: -Site.Longitude);
        TsTarget target = Target(10, 1, dec: -48.7, active: 0, raHours: raAtMidNight);

        VisibleTonightPlan under30 = VisibleTonightPass.Plan(
            Data([Project(1, Active)], [target]), Site, UtcNow, ThirtyMinutes, horizonAltitudeDeg: 0);
        VisibleTonightPlan underFourHours = VisibleTonightPass.Plan(
            Data([Project(1, Active)], [target]), Site, UtcNow, TimeSpan.FromHours(4), horizonAltitudeDeg: 0);

        Assert.Equal(1, under30.TargetsEnabled);        // ~2 h window clears a 30-min bar
        Assert.Equal(0, underFourHours.TargetsEnabled); // the same window can't stretch to 4 h
        Assert.Equal(1, underFourHours.TargetsUnchanged);
    }

    [Fact]
    public void TsMinimumAltitude_IsIgnored()
    {
        // The project demands 80° minimum altitude; the predicate is the geometric 0° horizon only.
        VisibleTonightPlan plan = Plan(
            [Project(1, Active, minimumAltitude: 80.0)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        Assert.Equal(1, plan.TargetsEnabled);
    }

    [Fact]
    public void MatchingValue_YieldsNoEdit()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 1)]);

        Assert.Empty(plan.Edits);
        Assert.Equal(1, plan.TargetsUnchanged);
    }

    // ---- project derivation ------------------------------------------------------------------------

    [Fact]
    public void ProjectWithNoEnabledTargets_GoesInactive_TargetEditsFirst()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active)],
            [Target(10, 1, NeverRisesDec, active: 1)]);

        Assert.Equal(2, plan.Edits.Count);
        Assert.Equal((TsTable.Target, "active", 0), (plan.Edits[0].Table, plan.Edits[0].Column, plan.Edits[0].Value));
        Assert.Equal((TsTable.Project, "state", Inactive), (plan.Edits[1].Table, plan.Edits[1].Column, plan.Edits[1].Value));
        Assert.Equal(1, plan.ProjectsDeactivated);
    }

    [Fact]
    public void InactiveProject_RegainingAVisibleTarget_GoesActive()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Inactive)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        Assert.Contains(plan.Edits, e => e is { Table: TsTable.Project, Column: "state", Value: Active });
        Assert.Equal(1, plan.ProjectsActivated);
    }

    [Fact]
    public void MixedProject_StaysActive_OnlyTheInvisibleTargetFlips()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 1), Target(11, 1, NeverRisesDec, active: 1)]);

        VisibleTonightEdit edit = Assert.Single(plan.Edits);   // no project edit — one target stays enabled
        Assert.Equal(TsTable.Target, edit.Table);
        Assert.Equal(0, plan.ProjectsDeactivated);
    }

    [Fact]
    public void ProjectWithNoTargets_GoesInactive()
    {
        VisibleTonightPlan plan = Plan([Project(1, Active)], []);

        VisibleTonightEdit edit = Assert.Single(plan.Edits);
        Assert.Equal((TsTable.Project, "state", Inactive), (edit.Table, edit.Column, edit.Value));
    }

    // ---- exclusions + contract ---------------------------------------------------------------------

    [Fact]
    public void DraftAndClosedProjects_AndTheirTargets_AreUntouched()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Draft), Project(2, Closed)],
            [Target(10, 1, NeverRisesDec, active: 1), Target(20, 2, NeverRisesDec, active: 1)]);

        Assert.Empty(plan.Edits);
        Assert.Equal(0, plan.TargetsDisabled + plan.TargetsEnabled + plan.TargetsUnchanged);
    }

    [Fact]
    public void TargetWithoutRaDec_AbortsTheWholePass()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 1) with { Ra = null }]));

        Assert.Contains("T10", ex.Message);   // names the offending target
    }

    [Fact]
    public void EditKeys_PreferTheTsGuid_FallBackToId()
    {
        VisibleTonightPlan plan = Plan(
            [Project(1, Active, tsGuid: null)],
            [Target(10, 1, NeverRisesDec, active: 1, tsGuid: "guid-10")]);

        Assert.Equal("guid-10", plan.Edits[0].Key);   // target row has a guid
        Assert.Equal("1", plan.Edits[1].Key);         // project row falls back to its Id
    }

    [Fact]
    public void HorizonAltitudeFloor_GatesLowTargets()
    {
        // dec -25 at this latitude: up for hours but peaks near 24° — visible over a 0° floor,
        // never over a 30° floor.
        TsTarget lowArc = Target(10, 1, dec: -25.0, active: 0);

        VisibleTonightPlan overZero = VisibleTonightPass.Plan(
            Data([Project(1, Active)], [lowArc]), Site, UtcNow, ThirtyMinutes, horizonAltitudeDeg: 0);
        VisibleTonightPlan overThirty = VisibleTonightPass.Plan(
            Data([Project(1, Active)], [lowArc]), Site, UtcNow, ThirtyMinutes, horizonAltitudeDeg: 30);

        Assert.Equal(1, overZero.TargetsEnabled);
        Assert.Equal(0, overThirty.TargetsEnabled);
        Assert.Equal(1, overThirty.TargetsUnchanged);   // active stays 0 — no edit under the 30° floor
    }

    // ---- builders ----------------------------------------------------------------------------------

    // Scenario tests pin the horizon parameter at 0° (the geometric-horizon scenarios of the spec);
    // HorizonAltitudeFloor_GatesLowTargets exercises the knob itself.
    private static VisibleTonightPlan Plan(IReadOnlyList<TsProject> projects, IReadOnlyList<TsTarget> targets) =>
        VisibleTonightPass.Plan(Data(projects, targets), Site, UtcNow, ThirtyMinutes, horizonAltitudeDeg: 0);

    private static TsPlanData Data(IReadOnlyList<TsProject> projects, IReadOnlyList<TsTarget> targets) =>
        new(projects, targets, [], []);

    private static TsProject Project(long id, int state, double? minimumAltitude = null, string? tsGuid = null) =>
        new(id, "profile", $"P{id}", state, Priority: 1, minimumAltitude, IsMosaic: 0, tsGuid);

    private static TsTarget Target(
        long id, long projectId, double dec, int active, double raHours = 5.0, string? tsGuid = null) =>
        new(id, $"T{id}", active, raHours, dec, EpochCode: 2, Rotation: null, Roi: null, projectId,
            Priority: 1, tsGuid);
}
