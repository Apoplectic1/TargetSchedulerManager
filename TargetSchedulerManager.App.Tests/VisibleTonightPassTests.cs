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
// The Plan helper runs both stages with an all-applied overlay (every target edit landed) — the
// happy-path combined behavior; the applied-derivation section exercises partial/empty landings.
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
        var (targets, _) = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        VisibleTonightEdit edit = Assert.Single(targets.Edits);
        Assert.Equal((TsTable.Target, "active", 1), (edit.Table, edit.Column, edit.Value));
        Assert.Equal(1, targets.Enabled);
        Assert.Equal(0, targets.Disabled);
    }

    [Fact]
    public void NeverRisesTarget_Enabled_GetsDisableEdit()
    {
        var (targets, _) = Plan(
            [Project(1, Active)],
            [Target(10, 1, NeverRisesDec, active: 1)]);

        Assert.Contains(targets.Edits, e => e is { Table: TsTable.Target, Column: "active", Value: 0 });
        Assert.Equal(1, targets.Disabled);
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

        VisibleTonightTargetPlan under30 = VisibleTonightPass.PlanTargets(
            Data([Project(1, Active)], [target]), Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 0);
        VisibleTonightTargetPlan underFourHours = VisibleTonightPass.PlanTargets(
            Data([Project(1, Active)], [target]), Site, UtcNow, TimeSpan.FromHours(4), floorAltitudeDeg: 0);

        Assert.Equal(1, under30.Enabled);        // ~2 h window clears a 30-min bar
        Assert.Equal(0, underFourHours.Enabled); // the same window can't stretch to 4 h
        Assert.Equal(1, underFourHours.Unchanged);
    }

    [Fact]
    public void TsMinimumAltitude_IsIgnored()
    {
        // The project demands 80° minimum altitude; the predicate is the geometric 0° horizon only.
        var (targets, _) = Plan(
            [Project(1, Active, minimumAltitude: 80.0)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        Assert.Equal(1, targets.Enabled);
    }

    [Fact]
    public void MatchingValue_YieldsNoEdit()
    {
        var (targets, projects) = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 1)]);

        Assert.Empty(targets.Edits);
        Assert.Empty(projects.Edits);
        Assert.Equal(1, targets.Unchanged);
    }

    // ---- project derivation ------------------------------------------------------------------------

    [Fact]
    public void ProjectWithNoEnabledTargets_GoesInactive()
    {
        var (targets, projects) = Plan(
            [Project(1, Active)],
            [Target(10, 1, NeverRisesDec, active: 1)]);

        VisibleTonightEdit targetEdit = Assert.Single(targets.Edits);
        Assert.Equal((TsTable.Target, "active", 0), (targetEdit.Table, targetEdit.Column, targetEdit.Value));
        VisibleTonightEdit projectEdit = Assert.Single(projects.Edits);
        Assert.Equal((TsTable.Project, "state", Inactive), (projectEdit.Table, projectEdit.Column, projectEdit.Value));
        Assert.Equal(1, projects.Deactivated);
    }

    [Fact]
    public void InactiveProject_RegainingAVisibleTarget_GoesActive()
    {
        var (_, projects) = Plan(
            [Project(1, Inactive)],
            [Target(10, 1, CircumpolarDec, active: 0)]);

        Assert.Contains(projects.Edits, e => e is { Table: TsTable.Project, Column: "state", Value: Active });
        Assert.Equal(1, projects.Activated);
    }

    [Fact]
    public void MixedProject_StaysActive_OnlyTheInvisibleTargetFlips()
    {
        var (targets, projects) = Plan(
            [Project(1, Active)],
            [Target(10, 1, CircumpolarDec, active: 1), Target(11, 1, NeverRisesDec, active: 1)]);

        VisibleTonightEdit edit = Assert.Single(targets.Edits);
        Assert.Equal(TsTable.Target, edit.Table);
        Assert.Empty(projects.Edits);   // one target stays enabled — no project flip
        Assert.Equal(0, projects.Deactivated);
    }

    [Fact]
    public void ProjectWithNoTargets_GoesInactive()
    {
        var (targets, projects) = Plan([Project(1, Active)], []);

        Assert.Empty(targets.Edits);
        VisibleTonightEdit edit = Assert.Single(projects.Edits);
        Assert.Equal((TsTable.Project, "state", Inactive), (edit.Table, edit.Column, edit.Value));
    }

    // ---- applied-state derivation (a failed flip contributes the target's OLD value) ---------------

    [Fact]
    public void FailedEnable_SoleVisibleTarget_DoesNotActivateTheProject()
    {
        // Stage 1 wants the target enabled; nothing landed — the derivation sees it still disabled,
        // so the Inactive project must NOT flip Active over a premise that never applied.
        TsPlanData data = Data([Project(1, Inactive)], [Target(10, 1, CircumpolarDec, active: 0)]);
        VisibleTonightTargetPlan targets = PlanTargets(data);

        Assert.Single(targets.Edits);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(data, appliedTargetEdits: []);

        Assert.Empty(projects.Edits);
        Assert.Equal(0, projects.Activated);
    }

    [Fact]
    public void FailedDisable_TargetEffectivelyStillEnabled_ProjectStaysActive()
    {
        // Stage 1 wants the sole target disabled; the write failed — the target is effectively still
        // enabled, so the Active project keeps its state.
        TsPlanData data = Data([Project(1, Active)], [Target(10, 1, NeverRisesDec, active: 1)]);
        VisibleTonightTargetPlan targets = PlanTargets(data);

        Assert.Single(targets.Edits);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(data, appliedTargetEdits: []);

        Assert.Empty(projects.Edits);
    }

    [Fact]
    public void PartialLanding_OneSurvivingEnable_KeepsTheProjectActive()
    {
        // Both targets get disable edits; only the first landed. The second is effectively still
        // enabled, so no project flip.
        TsPlanData data = Data(
            [Project(1, Active)],
            [Target(10, 1, NeverRisesDec, active: 1), Target(11, 1, NeverRisesDec, active: 1)]);
        VisibleTonightTargetPlan targets = PlanTargets(data);

        Assert.Equal(2, targets.Edits.Count);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(data, [targets.Edits[0]]);

        Assert.Empty(projects.Edits);
    }

    [Fact]
    public void ZeroTargetEdits_CanStillFlipAProject()
    {
        // Every target is already settled (disabled, not visible) — stage 2 still runs and reconciles
        // the Active project against the snapshot.
        TsPlanData data = Data([Project(1, Active)], [Target(10, 1, NeverRisesDec, active: 0)]);
        VisibleTonightTargetPlan targets = PlanTargets(data);

        Assert.Empty(targets.Edits);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(data, appliedTargetEdits: []);

        VisibleTonightEdit edit = Assert.Single(projects.Edits);
        Assert.Equal((TsTable.Project, "state", Inactive), (edit.Table, edit.Column, edit.Value));
    }

    // ---- exclusions + contract ---------------------------------------------------------------------

    [Fact]
    public void DraftAndClosedProjects_TargetsFlip_StateNeverWritten()
    {
        // Enables are sky truth for every project (openspec project-scoped-tonight); lifecycle stays
        // separate — both targets disable, neither project's state is derived or written.
        var (targets, projects) = Plan(
            [Project(1, Draft), Project(2, Closed)],
            [Target(10, 1, NeverRisesDec, active: 1), Target(20, 2, NeverRisesDec, active: 1)]);

        Assert.Equal(2, targets.Edits.Count);
        Assert.All(targets.Edits, e => Assert.Equal((TsTable.Target, "active", 0), (e.Table, e.Column, e.Value)));
        Assert.Equal(2, targets.Disabled);
        Assert.Empty(projects.Edits);
    }

    // ---- scoped press (openspec project-scoped-tonight) --------------------------------------------

    [Fact]
    public void ScopedPress_FlipsOnlyTheSelectedProjectsTargetsAndState()
    {
        // Both Active projects hold an enabled never-rises target; scoping to project 1 must leave
        // project 2's target AND state untouched even though both "deserve" the same flips.
        TsPlanData data = Data(
            [Project(1, Active), Project(2, Active)],
            [Target(10, 1, NeverRisesDec, active: 1), Target(20, 2, NeverRisesDec, active: 1)]);

        VisibleTonightTargetPlan targets = VisibleTonightPass.PlanTargets(
            data, Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 0, onlyProjectId: 1);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(
            data, targets.Edits, onlyProjectId: 1);

        VisibleTonightEdit targetEdit = Assert.Single(targets.Edits);
        Assert.Equal("10", targetEdit.Key);                 // project 2's target untouched
        VisibleTonightEdit projectEdit = Assert.Single(projects.Edits);
        Assert.Equal("1", projectEdit.Key);                 // project 2's state untouched
        Assert.Equal((TsTable.Project, "state", Inactive), (projectEdit.Table, projectEdit.Column, projectEdit.Value));
    }

    [Fact]
    public void ScopedPress_OnDraftProject_FlipsTargetsButNotState()
    {
        TsPlanData data = Data([Project(1, Draft)], [Target(10, 1, NeverRisesDec, active: 1)]);

        VisibleTonightTargetPlan targets = VisibleTonightPass.PlanTargets(
            data, Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 0, onlyProjectId: 1);
        VisibleTonightProjectPlan projects = VisibleTonightPass.PlanProjects(
            data, targets.Edits, onlyProjectId: 1);

        Assert.Single(targets.Edits);
        Assert.Empty(projects.Edits);   // Draft state is never derived, even when explicitly selected
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
        var (targets, projects) = Plan(
            [Project(1, Active, tsGuid: null)],
            [Target(10, 1, NeverRisesDec, active: 1, tsGuid: "guid-10")]);

        Assert.Equal("guid-10", targets.Edits[0].Key);   // target row has a guid
        Assert.Equal("1", projects.Edits[0].Key);        // project row falls back to its Id
    }

    [Fact]
    public void AltitudeFloor_GatesLowTargets()
    {
        // dec -25 at this latitude: up for hours but peaks near 24° — visible over a 0° floor,
        // never over a 30° floor.
        TsTarget lowArc = Target(10, 1, dec: -25.0, active: 0);

        VisibleTonightTargetPlan overZero = VisibleTonightPass.PlanTargets(
            Data([Project(1, Active)], [lowArc]), Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 0);
        VisibleTonightTargetPlan overThirty = VisibleTonightPass.PlanTargets(
            Data([Project(1, Active)], [lowArc]), Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 30);

        Assert.Equal(1, overZero.Enabled);
        Assert.Equal(0, overThirty.Enabled);
        Assert.Equal(1, overThirty.Unchanged);   // active stays 0 — no edit under the 30° floor
    }

    // ---- the definitional-clause composition (openspec project-name-altitude-clause: every name is
    // base + " - N" mirroring the stored altitude; the old never-invent rule is superseded) -----------

    [Theory]
    [InlineData("Nebulae - 45", 30, "Nebulae - 30")]                          // stale clause rewrites
    [InlineData("Nebulae - Above 45", 30, "Nebulae - 30")]                    // legacy form heals to short
    [InlineData("Mosaic - Clamshell  - 30", 25, "Mosaic - Clamshell - 25")]   // normalizes the stray double space
    [InlineData("Nebulea - 0", 40, "Nebulea - 40")]                           // 0 clause rewrites like any other
    [InlineData("Galaxies - 45", 37.5, "Galaxies - 37.5")]                    // decimal altitude
    [InlineData("Galaxies - 45", 0, "Galaxies - 0")]                          // zero floor composes ("as low as possible")
    [InlineData("Galaxies", 45, "Galaxies - 45")]                             // clause-less GAINS its clause (definitional)
    [InlineData("Above the Clouds", 30, "Above the Clouds - 30")]             // "Above" inside a name is base text
    [InlineData("Abell 2218", 30, "Abell 2218 - 30")]                         // bare-number base composes verbatim
    [InlineData("Sh2-155", 30, "Sh2-155 - 30")]                               // hyphen-digit base never mis-strips
    public void ComposeRename_ComposesTheDefinitionalClause(string name, double alt, string expected) =>
        Assert.Equal(expected, VisibleTonightPass.ComposeRename(name, alt));

    [Theory]
    [InlineData("Nebulae - 30", 30)]             // already composed — no edit
    [InlineData("Nebulae - 37.5", 37.5)]         // decimal, already composed
    [InlineData("Nebulea - 0", 0)]               // zero floor, already composed
    public void ComposeRename_YieldsNoEditWhenAlreadyComposed(string name, double alt) =>
        Assert.Null(VisibleTonightPass.ComposeRename(name, alt));

    // ---- builders ----------------------------------------------------------------------------------

    // Scenario tests pin the floor parameter at 0° (the geometric-horizon scenarios of the spec);
    // AltitudeFloor_GatesLowTargets exercises the knob itself.
    private static (VisibleTonightTargetPlan, VisibleTonightProjectPlan) Plan(
        IReadOnlyList<TsProject> projects, IReadOnlyList<TsTarget> targets)
    {
        TsPlanData data = Data(projects, targets);
        VisibleTonightTargetPlan targetPlan = PlanTargets(data);
        return (targetPlan, VisibleTonightPass.PlanProjects(data, targetPlan.Edits));   // all landed
    }

    private static VisibleTonightTargetPlan PlanTargets(TsPlanData data) =>
        VisibleTonightPass.PlanTargets(data, Site, UtcNow, ThirtyMinutes, floorAltitudeDeg: 0);

    private static TsPlanData Data(IReadOnlyList<TsProject> projects, IReadOnlyList<TsTarget> targets) =>
        new(projects, targets, [], []);

    private static TsProject Project(long id, int state, double? minimumAltitude = null, string? tsGuid = null) =>
        new(id, "profile", $"P{id}", state, Priority: 1, minimumAltitude, IsMosaic: 0, tsGuid);

    private static TsTarget Target(
        long id, long projectId, double dec, int active, double raHours = 5.0, string? tsGuid = null) =>
        new(id, $"T{id}", active, raHours, dec, EpochCode: 2, Rotation: null, Roi: null, projectId,
            Priority: 1, tsGuid);
}
