using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The adoption planner (openspec disk-row-adoption, assignment model): the eligibility matrix, the strict
// template scope with its merge verdicts and preselect ranking, the dialog facts (locked vs choosable
// project, structural refusals), and payload assembly for the plan-only and target-creating cases.
public class AdoptionPlannerTests
{
    // ---- fixture: profile "prof-1", project 101 (p-101), target 7 (t-7) with one H-Light plan @900s ------

    private static readonly Guid DiskTargetId = Guid.NewGuid();

    private static TsPlanData Ts(params TsExposureTemplate[] extraTemplates) => new(
        Projects:
        [
            new TsProject(101, "prof-1", "Nebulae", 1, 1, null, IsMosaic: 0, "p-101"),
            new TsProject(102, "prof-1", "CygnusLoop Mosaic", 1, 1, null, IsMosaic: 1, "p-102"),
        ],
        Targets: [new TsTarget(7, "NGC 7000", 1, 20.9, 44.5, 2, null, 100, 101, -1, "t-7")],
        Templates:
        [
            new TsExposureTemplate(21, "prof-1", "H900", "H", Gain: 0, Offset: 0, Bin: 1, DefaultExposure: 900),
            .. extraTemplates,
        ],
        Plans: [new TsExposurePlan(31, "prof-1", -1, 20, 5, 5, TargetId: 7, ExposureTemplateId: 21)]);

    private static CatalogGraph Graph(double? ra = 21.5, double? dec = 40.25) => new(
        [], [], [],
        [new Target(DiskTargetId, TargetSource.Actual, null, "Sh2-119", true, ra, dec, Epoch.J2000,
            null, null, null, "Sh2-119", null, null, null, 1L, 1L, null)],
        [], []);

    private static ReconciliationRow DiskRow(
        string? tsTargetKey = "t-7", string filter = "H", int seconds = 600, int frames = 42,
        string? panelKey = null, RowConfig? config = null) =>
        Make.Leaf("NGC 7000", RowPlane.Disk, filter, planSeconds: 0, diskSeconds: seconds,
            desired: null, acquired: null, accepted: null, disk: frames, planCount: 0,
            tsTargetKey: tsTargetKey, targetId: DiskTargetId, panelKey: panelKey,
            panelLabel: panelKey, config: config);

    private static ReconciliationRow StarsRow(RowConfig? config = null, string? tsTargetKey = "t-7") =>
        Make.Leaf("Abell 78", RowPlane.Disk, "B", "Stars",
            planSeconds: 0, diskSeconds: 60, desired: null, acquired: null, accepted: null,
            disk: 30, planCount: 0, tsTargetKey: tsTargetKey, targetId: DiskTargetId, config: config);

    // ---- eligibility --------------------------------------------------------------------------------------

    [Fact]
    public void UnplannedDiskCell_IsEligible()
        => Assert.True(AdoptionPlanner.IsEligible(DiskRow(seconds: 600), Ts()));

    [Fact]
    public void SplitRow_SameKeyPlanExists_IsIneligible()
        // 900 s is exactly the existing plan's effective bucket — this disk row separated from it by a
        // config/framing disagreement; adopting would mint a same-key duplicate.
        => Assert.False(AdoptionPlanner.IsEligible(DiskRow(seconds: 900), Ts()));

    [Fact]
    public void NonDiskPlanes_AreIneligible()
    {
        Assert.False(AdoptionPlanner.IsEligible(Make.Ts(), Ts()));
        Assert.False(AdoptionPlanner.IsEligible(Make.Leaf(plane: RowPlane.Both), Ts()));
    }

    [Fact]
    public void DiskOnlyPanel_IsIneligible()
        => Assert.False(AdoptionPlanner.IsEligible(
            DiskRow(tsTargetKey: null, panelKey: "P01"), Ts()));

    [Fact]
    public void DiskOnlyTarget_IsEligible()
        => Assert.True(AdoptionPlanner.IsEligible(DiskRow(tsTargetKey: null), Ts()));

    [Fact]
    public void StaleTsKey_IsIneligible()
        => Assert.False(AdoptionPlanner.IsEligible(DiskRow(tsTargetKey: "t-gone"), Ts()));

    [Fact]
    public void PickableProjects_ExcludeMosaics()
    {
        TsProject project = Assert.Single(AdoptionPlanner.PickableProjects(Ts()));
        Assert.Equal("Nebulae", project.Name);
    }

    // ---- strict scope + merge verdicts --------------------------------------------------------------------

    [Fact]
    public void Scope_IsSameFilterSameBinOnly()
    {
        // A 2x2 H template is a different integration (binning rule, obs 2278) and another filter is
        // another cell entirely — neither is ever listed.
        TsPlanData ts = Ts(
            new TsExposureTemplate(22, "prof-1", "H 2x2", "H", 0, 0, Bin: 2, 300),
            new TsExposureTemplate(23, "prof-1", "O600", "O", 0, 0, Bin: 1, 600));

        (IReadOnlyList<AdoptionCandidate> candidates, _) =
            AdoptionPlanner.ListCandidates(DiskRow(seconds: 600), ts, "prof-1");

        Assert.Equal("H900", Assert.Single(candidates).Template.Name);
    }

    [Fact]
    public void NonSquareBinning_EmptyScope()
    {
        RowConfig rect = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 2, Camera: null, CameraDisagrees: false);
        (IReadOnlyList<AdoptionCandidate> candidates, int preselect) =
            AdoptionPlanner.ListCandidates(DiskRow(seconds: 600, config: rect), Ts(), "prof-1");
        Assert.Empty(candidates);
        Assert.Equal(-1, preselect);
    }

    [Fact]
    public void MatchingTemplate_WouldPair()
    {
        (IReadOnlyList<AdoptionCandidate> candidates, int preselect) =
            AdoptionPlanner.ListCandidates(DiskRow(seconds: 600), Ts(), "prof-1");

        AdoptionCandidate h900 = Assert.Single(candidates);
        Assert.True(h900.WouldPair);
        Assert.Null(h900.MismatchReason);
        Assert.Equal(0, preselect);
    }

    [Fact]
    public void GainDisagreement_CautionsWithBothValues()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", Gain: 139, Offset: 10, Bin: 1, 600));
        RowConfig gain100 = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);

        (IReadOnlyList<AdoptionCandidate> candidates, _) =
            AdoptionPlanner.ListCandidates(DiskRow(filter: "O", seconds: 600, config: gain100), ts, "prof-1");

        AdoptionCandidate o600 = Assert.Single(candidates);
        Assert.False(o600.WouldPair);
        Assert.Contains("gain 139 vs 100", o600.MismatchReason);
    }

    [Fact]
    public void CameraDefaultSentinel_NeverPairs_NamedAsSuch()
    {
        // The projection keys a template's -1 as the value it is, so a sentinel template lands beside an
        // expressed disk cell — the caution says so in camera-default words.
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", Gain: -1, Offset: -1, Bin: 1, 600));
        RowConfig gain100 = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);

        (IReadOnlyList<AdoptionCandidate> candidates, _) =
            AdoptionPlanner.ListCandidates(DiskRow(filter: "O", seconds: 600, config: gain100), ts, "prof-1");

        AdoptionCandidate o600 = Assert.Single(candidates);
        Assert.False(o600.WouldPair);
        Assert.Contains("camera-default gain vs 100", o600.MismatchReason);
        Assert.Contains("camera-default offset vs 10", o600.MismatchReason);
    }

    [Fact]
    public void PurposeDisagreement_Cautions()
    {
        // The Abell 78 field case, assignment reading: 'Stars B' (gain 0) over gain-53 Stars frames — same
        // purpose but the gain disagrees; 'B300' (Light) additionally reads as the wrong purpose.
        TsPlanData ts = Ts(
            new TsExposureTemplate(22, "prof-1", "Stars B", "B", Gain: 0, Offset: 10, Bin: 1, 60),
            new TsExposureTemplate(23, "prof-1", "B300", "B", Gain: 0, Offset: 10, Bin: 1, 300));
        RowConfig gain53 = new(Gain: 53, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);

        (IReadOnlyList<AdoptionCandidate> candidates, int preselect) =
            AdoptionPlanner.ListCandidates(StarsRow(config: gain53), ts, "prof-1");

        Assert.Equal(2, candidates.Count);
        AdoptionCandidate b300 = candidates.Single(c => c.Template.Name == "B300");
        Assert.Contains("purpose Light vs Stars", b300.MismatchReason);
        AdoptionCandidate starsB = candidates.Single(c => c.Template.Name == "Stars B");
        Assert.Contains("gain 0 vs 53", starsB.MismatchReason);
        Assert.DoesNotContain("purpose", starsB.MismatchReason);
        // Nothing pairs → the same-purpose near-miss is the preselect.
        Assert.Equal("Stars B", candidates[preselect].Template.Name);
    }

    [Fact]
    public void Preselect_PairingCandidateOutranksNameOrder()
    {
        // "A600" sorts first but disagrees on gain; "H900"... — use an exact-pair candidate later in name
        // order to prove ranking beats listing order.
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "A pair-breaker H", "H", Gain: 139, Offset: 0, Bin: 1, 600));

        (IReadOnlyList<AdoptionCandidate> candidates, int preselect) =
            AdoptionPlanner.ListCandidates(DiskRow(seconds: 600), ts, "prof-1");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("A pair-breaker H", candidates[0].Template.Name);   // name order lists it first
        Assert.Equal("H900", candidates[preselect].Template.Name);       // the pairing one preselects
    }

    // ---- dialog facts -------------------------------------------------------------------------------------

    [Fact]
    public void ExistingTarget_LocksTheOwningProject()
    {
        (AdoptionFacts? facts, string? refusal) = AdoptionPlanner.GetFacts(DiskRow(seconds: 600), Graph(), Ts());

        Assert.Null(refusal);
        Assert.True(facts!.ProjectLocked);
        AdoptionProjectOption option = Assert.Single(facts.Projects);
        Assert.Equal("Nebulae", option.Project.Name);
        Assert.Null(facts.TargetName);           // no target creation
        Assert.Equal("H", facts.Filter);
        Assert.Equal(42, facts.DiskCount);
        Assert.Equal(600, facts.Seconds);
    }

    [Fact]
    public void DiskOnlyTarget_OffersPickableProjectsAndCreationFacts()
    {
        (AdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetFacts(DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: 21.5, dec: 40.25), Ts());

        Assert.Null(refusal);
        Assert.False(facts!.ProjectLocked);
        Assert.Equal("Nebulae", Assert.Single(facts.Projects).Project.Name);   // mosaics never offered
        Assert.Equal("Sh2-119", facts.TargetName);
        Assert.Equal(21.5, facts.RaHours);
        Assert.Equal(40.25, facts.DecDegrees);
        Assert.Null(facts.SeededRotationDeg);
    }

    [Fact]
    public void SkyFraming_SeedsTheFactsRotation_MechanicalDoesNot()
    {
        RowConfig sky = new(0, 0, 1, 1, null, false,
            Rotation: Astronomy.Catalog.Scan.RotationExpression.Sky, RotationFoldDeg: 57.3);
        (AdoptionFacts? withSky, _) = AdoptionPlanner.GetFacts(
            DiskRow(tsTargetKey: null, seconds: 600, config: sky), Graph(), Ts());
        Assert.Equal(57.3, withSky!.SeededRotationDeg);

        RowConfig mech = sky with { Rotation = Astronomy.Catalog.Scan.RotationExpression.Mechanical };
        (AdoptionFacts? withMech, _) = AdoptionPlanner.GetFacts(
            DiskRow(tsTargetKey: null, seconds: 600, config: mech), Graph(), Ts());
        Assert.Null(withMech!.SeededRotationDeg);
    }

    [Fact]
    public void NoCentroid_Refuses()
    {
        (AdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetFacts(DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: null), Ts());
        Assert.Null(facts);
        Assert.Contains("centroid", refusal);
    }

    [Fact]
    public void NoPickableProjects_Refuses()
    {
        TsPlanData mosaicOnly = new(
            Projects: [new TsProject(102, "prof-1", "CygnusLoop Mosaic", 1, 1, null, IsMosaic: 1, "p-102")],
            Targets: [], Templates: [], Plans: []);
        (AdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetFacts(DiskRow(tsTargetKey: null, seconds: 600), Graph(), mosaicOnly);
        Assert.Null(facts);
        Assert.Contains("no TS projects", refusal);
    }

    [Fact]
    public void StaleOwningProject_Refuses()
    {
        TsPlanData orphaned = new(
            Projects: [new TsProject(101, "prof-1", "Nebulae", 1, 1, null, IsMosaic: 0, "p-101")],
            Targets: [new TsTarget(7, "NGC 7000", 1, 20.9, 44.5, 2, null, 100, ProjectId: 999, -1, "t-7")],
            Templates: [], Plans: []);
        (AdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetFacts(DiskRow(seconds: 600), Graph(), orphaned);
        Assert.Null(facts);
        Assert.Contains("project is missing", refusal);
    }

    [Fact]
    public void EmptyScopeReason_NamesNonSquareOrMissingTemplate()
    {
        RowConfig rect = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 2, Camera: null, CameraDisagrees: false);
        (AdoptionFacts? nonSquare, _) = AdoptionPlanner.GetFacts(DiskRow(seconds: 600, config: rect), Graph(), Ts());
        Assert.Contains("non-square", nonSquare!.EmptyScopeReason);

        (AdoptionFacts? square, _) = AdoptionPlanner.GetFacts(DiskRow(seconds: 600), Graph(), Ts());
        Assert.Contains("no H template at bin 1", square!.EmptyScopeReason);
    }

    // ---- build --------------------------------------------------------------------------------------------

    [Fact]
    public void AssignedTemplate_BuildsPlanOnExistingTarget()
    {
        TsPlanData ts = Ts();
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(seconds: 600), Graph(), ts, ts.Projects[0], ts.Templates[0]);

        Assert.Null(refusal);
        TsRowInsert insert = Assert.Single(plan!.Rows);
        Assert.False(plan.CreatesTarget);
        Assert.Equal(TsTable.ExposurePlan, insert.Table);
        Assert.Equal("t-7", insert.Payload["targetid"]);          // guid form — ids can diverge
        Assert.Equal(21L, insert.Payload["exposureTemplateId"]);  // copy-stable integer id
        Assert.Equal("prof-1", insert.Payload["profileId"]);
        Assert.Equal(600.0, insert.Payload["exposure"]);          // template default 900 ≠ 600 → explicit
        Assert.Equal(42, insert.Payload["desired"]);              // born complete
        Assert.Equal(42, insert.Payload["acquired"]);
        Assert.Equal(42, insert.Payload["accepted"]);
        Assert.Equal(1, insert.Payload["enabled"]);
    }

    [Fact]
    public void TemplateDefaultMatchesSeconds_UsesSentinel()
    {
        TsExposureTemplate o600 = new(22, "prof-1", "O600", "O", 0, 0, 1, 600);
        TsPlanData ts = Ts(o600);
        (AdoptionPlan? plan, _) = AdoptionPlanner.Build(
            DiskRow(filter: "O", seconds: 600), Graph(), ts, ts.Projects[0], o600);
        Assert.Equal(-1.0, Assert.Single(plan!.Rows).Payload["exposure"]);
    }

    [Fact]
    public void NonPairingTemplate_StillBuilds_SeedsZeros()
    {
        // Assignment never blocks (the caution is the dialog's): a gain-139 template over gain-100 frames
        // builds a plan that will render beside the disk row — the informed choice the user accepted. Its
        // counts seed 0/0/0: no disk files correspond to the plan being created, so the pushed row carries
        // no counts the disk cannot back (pairing-credited-write-back).
        TsExposureTemplate o600 = new(22, "prof-1", "O600", "O", Gain: 139, Offset: 10, Bin: 1, 600);
        TsPlanData ts = Ts(o600);
        RowConfig gain100 = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);

        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(filter: "O", seconds: 600, config: gain100), Graph(), ts, ts.Projects[0], o600);

        Assert.Null(refusal);
        TsRowInsert insert = Assert.Single(plan!.Rows);
        Assert.Equal(22L, insert.Payload["exposureTemplateId"]);
        Assert.Equal(0, insert.Payload["desired"]);
        Assert.Equal(0, insert.Payload["acquired"]);
        Assert.Equal(0, insert.Payload["accepted"]);
        Assert.Equal(1, insert.Payload["enabled"]);   // enabled either way
    }

    [Fact]
    public void SentinelTemplate_SeedsZeros()
    {
        // A camera-default sentinel can never be asserted to agree with what was captured — the same
        // never-pairs rule as everywhere, so the plan is born empty.
        TsExposureTemplate o600 = new(22, "prof-1", "O600", "O", Gain: -1, Offset: 0, Bin: 1, 600);
        TsPlanData ts = Ts(o600);

        (AdoptionPlan? plan, _) = AdoptionPlanner.Build(
            DiskRow(filter: "O", seconds: 600), Graph(), ts, ts.Projects[0], o600);

        Assert.Equal(0, Assert.Single(plan!.Rows).Payload["desired"]);
    }

    // ---- target creation ----------------------------------------------------------------------------------

    [Fact]
    public void DiskOnlyTarget_BuildsTargetThenPlan()
    {
        TsPlanData ts = Ts();
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: 21.5, dec: 40.25), ts, ts.Projects[0], ts.Templates[0]);

        Assert.Null(refusal);
        Assert.True(plan!.CreatesTarget);
        Assert.Equal(2, plan.Rows.Count);
        TsRowInsert target = plan.Rows[0];
        Assert.Equal(TsTable.Target, target.Table);
        Assert.Equal("p-101", target.Payload["projectid"]);       // project by guid
        Assert.Equal("Sh2-119", target.Payload["name"]);
        Assert.Equal(21.5, target.Payload["ra"]);                 // graph coords are already TS units
        Assert.Equal(40.25, target.Payload["dec"]);
        Assert.Equal(2, target.Payload["epochcode"]);             // NINA J2000
        Assert.False(target.Payload.ContainsKey("rotation"));     // nothing expressed → stays NULL

        TsRowInsert planRow = plan.Rows[1];
        Assert.Equal(target.Payload["guid"], planRow.Payload["targetid"]);   // same-batch guid reference
    }

    [Fact]
    public void SkyFraming_SeedsRotationPayload_MechanicalDoesNot()
    {
        TsPlanData ts = Ts();
        RowConfig sky = new(0, 0, 1, 1, null, false,
            Rotation: Astronomy.Catalog.Scan.RotationExpression.Sky, RotationFoldDeg: 57.3);
        (AdoptionPlan? withSky, _) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600, config: sky), Graph(), ts, ts.Projects[0], ts.Templates[0]);
        Assert.Equal(57.3, withSky!.Rows[0].Payload["rotation"]);

        RowConfig mech = sky with { Rotation = Astronomy.Catalog.Scan.RotationExpression.Mechanical };
        (AdoptionPlan? withMech, _) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600, config: mech), Graph(), ts, ts.Projects[0], ts.Templates[0]);
        Assert.False(withMech!.Rows[0].Payload.ContainsKey("rotation"));
    }

    [Fact]
    public void NoCentroid_BuildRefusesAsBackstop()
    {
        TsPlanData ts = Ts();
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: null), ts, ts.Projects[0], ts.Templates[0]);
        Assert.Null(plan);
        Assert.Contains("centroid", refusal);
    }
}
