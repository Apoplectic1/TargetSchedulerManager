using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The adoption planner (openspec disk-row-adoption): the eligibility matrix, the template pairing rule with
// its hold refusals, and payload assembly for both the plan-only and the target-creating cases.
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

    // ---- template pairing rule ----------------------------------------------------------------------------

    [Fact]
    public void UniqueMatch_BuildsPlanOnExistingTarget()
    {
        (AdoptionPlan? plan, string? refusal) =
            AdoptionPlanner.Build(DiskRow(seconds: 600), Graph(), Ts(), project: null);

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
        // A second H template can't exist for this to stay unique — use a fresh filter instead.
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", 0, 0, 1, 600));
        (AdoptionPlan? plan, _) = AdoptionPlanner.Build(DiskRow(filter: "O", seconds: 600), Graph(), ts, null);
        Assert.Equal(-1.0, Assert.Single(plan!.Rows).Payload["exposure"]);
    }

    [Fact]
    public void ExpressedGainDisagreement_Holds()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", Gain: 139, Offset: 10, Bin: 1, 600));
        RowConfig gain100 = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);
        (AdoptionPlan? plan, string? refusal) =
            AdoptionPlanner.Build(DiskRow(filter: "O", seconds: 600, config: gain100), Graph(), ts, null);

        Assert.Null(plan);
        Assert.Contains("no template matches", refusal);
        Assert.Contains("gain 100", refusal);
    }

    [Fact]
    public void CameraDefaultTemplate_NeverPairs_HoldsWithHint()
    {
        // The merge rule's honest reading (capture-config-keys): a -1 use-camera-default sentinel is an
        // unspecified value — nothing can be asserted to agree with it, so a plan from such a template
        // would land BESIDE the disk row, not merge. The hold names the near-miss so the fix is obvious.
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", Gain: -1, Offset: -1, Bin: 1, 600));
        RowConfig gain100 = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);
        (AdoptionPlan? plan, string? refusal) =
            AdoptionPlanner.Build(DiskRow(filter: "O", seconds: 600, config: gain100), Graph(), ts, null);

        Assert.Null(plan);
        Assert.Contains("no template matches", refusal);
        Assert.Contains("'O600'", refusal);
        Assert.Contains("camera-default gain", refusal);
    }

    [Fact]
    public void NearMissTemplate_NamedInTheHold()
    {
        // The Abell 78 field case: a same-family template exists but its gain differs from the frames'.
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "Stars B", "B", Gain: 0, Offset: 10, Bin: 1, 60));
        RowConfig gain53 = new(Gain: 53, Offset: 10, BinningX: 1, BinningY: 1, Camera: null, CameraDisagrees: false);
        ReconciliationRow stars = Make.Leaf("Abell 78", RowPlane.Disk, "B", "Stars",
            planSeconds: 0, diskSeconds: 60, desired: null, acquired: null, accepted: null,
            disk: 30, planCount: 0, tsTargetKey: "t-7", targetId: default, config: gain53);

        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(stars, Graph(), ts, null);
        Assert.Null(plan);
        Assert.Contains("close: 'Stars B' (gain 0, offset 10)", refusal);
    }

    [Fact]
    public void TwoCandidates_HoldNamingBoth()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "H600", "H", 0, 0, 1, 600));
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(DiskRow(seconds: 600), Graph(), ts, null);

        Assert.Null(plan);
        Assert.Contains("2 templates match", refusal);
        Assert.Contains("'H900'", refusal);
        Assert.Contains("'H600'", refusal);
    }

    [Fact]
    public void StarsPurpose_MatchesOnlyStarsTemplates()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "Stars H", "H", 0, 0, 1, 10));
        ReconciliationRow stars = Make.Leaf("NGC 7000", RowPlane.Disk, "H", "Stars",
            planSeconds: 0, diskSeconds: 10, desired: null, acquired: null, accepted: null,
            disk: 30, planCount: 0, tsTargetKey: "t-7", targetId: DiskTargetId);

        (AdoptionPlan? plan, _) = AdoptionPlanner.Build(stars, Graph(), ts, null);
        Assert.Equal(22L, Assert.Single(plan!.Rows).Payload["exposureTemplateId"]);
    }

    [Fact]
    public void NonSquareBinning_Holds()
    {
        RowConfig rect = new(Gain: 100, Offset: 10, BinningX: 1, BinningY: 2, Camera: null, CameraDisagrees: false);
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(DiskRow(seconds: 600, config: rect), Graph(), Ts(), null);
        Assert.Null(plan);
        Assert.Contains("non-square", refusal);
    }

    // ---- target creation ----------------------------------------------------------------------------------

    [Fact]
    public void DiskOnlyTarget_NoProject_Holds()
    {
        (AdoptionPlan? plan, string? refusal) =
            AdoptionPlanner.Build(DiskRow(tsTargetKey: null, seconds: 600), Graph(), Ts(), project: null);
        Assert.Null(plan);
        Assert.Contains("pick a project", refusal);
    }

    [Fact]
    public void DiskOnlyTarget_BuildsTargetThenPlan()
    {
        TsPlanData ts = Ts();
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: 21.5, dec: 40.25), ts, ts.Projects[0]);

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
        Assert.Equal("Sh2-119", plan.TargetName);
        Assert.Equal(21.5, plan.RaHours);
    }

    [Fact]
    public void SkyFraming_SeedsRotation_MechanicalDoesNot()
    {
        TsPlanData ts = Ts();
        RowConfig sky = new(0, 0, 1, 1, null, false,
            Rotation: Astronomy.Catalog.Scan.RotationExpression.Sky, RotationFoldDeg: 57.3);
        (AdoptionPlan? withSky, _) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600, config: sky), Graph(), ts, ts.Projects[0]);
        Assert.Equal(57.3, withSky!.Rows[0].Payload["rotation"]);
        Assert.Equal(57.3, withSky.SeededRotationDeg);

        RowConfig mech = sky with { Rotation = Astronomy.Catalog.Scan.RotationExpression.Mechanical };
        (AdoptionPlan? withMech, _) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600, config: mech), Graph(), ts, ts.Projects[0]);
        Assert.False(withMech!.Rows[0].Payload.ContainsKey("rotation"));
    }

    [Fact]
    public void NoCentroid_Holds()
    {
        TsPlanData ts = Ts();
        (AdoptionPlan? plan, string? refusal) = AdoptionPlanner.Build(
            DiskRow(tsTargetKey: null, seconds: 600), Graph(ra: null), ts, ts.Projects[0]);
        Assert.Null(plan);
        Assert.Contains("centroid", refusal);
        Assert.Null(AdoptionPlanner.TargetFacts(DiskRow(), Graph(ra: null)));
    }
}
