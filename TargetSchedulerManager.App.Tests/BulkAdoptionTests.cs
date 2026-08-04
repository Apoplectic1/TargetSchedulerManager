using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The bulk grain of adoption (openspec adopt-target-rollup): eligible-cell enumeration over a rollup, the
// combined dialog's facts (project situation once, per-cell × per-project scopes, rotation seed), the
// one-batch build (target once + a plan per assignment, per-cell refusal aborts the whole), and the VM
// gate/cancel paths. Per-cell semantics (scope, verdicts, payload fields) are covered by
// AdoptionPlannerTests — the bulk members compose those, so only the composition is tested here.
public class BulkAdoptionTests
{
    // ---- fixture: same world as AdoptionPlannerTests — profile "prof-1", project 101, target 7 ----------

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
        RowConfig? config = null) =>
        Make.Leaf("NGC 7000", RowPlane.Disk, filter, planSeconds: 0, diskSeconds: seconds,
            desired: null, acquired: null, accepted: null, disk: frames, planCount: 0,
            tsTargetKey: tsTargetKey, targetId: DiskTargetId, config: config);

    private static TargetGroupRow Group(params ReconciliationRow[] children) =>
        new("NGC 7000", children, isExpanded: false, isTargetEnabled: true);

    private static RowConfig Config(
        RotationExpression? rotation = null, double? foldDeg = null,
        int gain = 0, int offset = 0, int bin = 1) =>
        new(gain, offset, bin, bin, Camera: null, CameraDisagrees: false,
            Rotation: rotation, RotationFoldDeg: foldDeg);

    // ---- eligible-cell enumeration ----------------------------------------------------------------------

    [Fact]
    public void EligibleCells_AppliesThePerCellGate_PreservingGridOrder()
    {
        // 900 s is the existing plan's bucket (a split row) and a TS-plane row is never adoptable —
        // exactly the per-cell gate, applied per child.
        ReconciliationRow h600 = DiskRow(seconds: 600);
        ReconciliationRow split = DiskRow(seconds: 900);
        ReconciliationRow o600 = DiskRow(filter: "O", seconds: 600);
        TargetGroupRow group = Group(h600, split, Make.Ts(), o600);

        Assert.Equal([h600, o600], AdoptionPlanner.EligibleCells(group, Ts()));
    }

    [Fact]
    public void EligibleCells_MosaicParent_HasNone()
    {
        ReconciliationRow leaf = DiskRow(tsTargetKey: null, seconds: 600);
        PanelGroupRow panel = new("NGC 7000", "P01", "P01", RowSource.Both, [leaf], isExpanded: false);
        TargetGroupRow mosaic = new("NGC 7000", [leaf], isExpanded: false, isTargetEnabled: true, [panel]);

        Assert.Empty(AdoptionPlanner.EligibleCells(mosaic, Ts()));
    }

    // ---- bulk facts -------------------------------------------------------------------------------------

    [Fact]
    public void GetBulkFacts_ExistingTarget_LocksOwner_AndScopesEveryCellPerProject()
    {
        TargetGroupRow group = Group(DiskRow(seconds: 600), DiskRow(filter: "O", seconds: 600));

        (BulkAdoptionFacts? facts, string? refusal) = AdoptionPlanner.GetBulkFacts(group, Graph(), Ts());

        Assert.Null(refusal);
        Assert.True(facts!.ProjectLocked);
        Assert.Null(facts.TargetName);                    // no target creation
        Assert.Equal(2, facts.Cells.Count);
        BulkAdoptionProjectOption option = Assert.Single(facts.Projects);
        Assert.Equal("Nebulae", option.Project.Name);
        Assert.Equal(2, option.CellScopes.Count);         // parallel to Cells
        Assert.Equal("H900", Assert.Single(option.CellScopes[0].Candidates).Template.Name);
        Assert.Empty(option.CellScopes[1].Candidates);    // no O template in the profile
        Assert.Contains("no O template at bin 1", facts.Cells[1].EmptyScopeReason);
    }

    [Fact]
    public void GetBulkFacts_DiskOnlyTarget_CreationFactsAndPickableProjects()
    {
        TargetGroupRow group = Group(
            DiskRow(tsTargetKey: null, seconds: 600),
            DiskRow(tsTargetKey: null, filter: "O", seconds: 600));

        (BulkAdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetBulkFacts(group, Graph(ra: 21.5, dec: 40.25), Ts());

        Assert.Null(refusal);
        Assert.False(facts!.ProjectLocked);
        Assert.Equal("Sh2-119", facts.TargetName);
        Assert.Equal(21.5, facts.RaHours);
        Assert.Equal(40.25, facts.DecDegrees);
        Assert.Equal("Nebulae", Assert.Single(facts.Projects).Project.Name);   // mosaics never offered
    }

    [Fact]
    public void GetBulkFacts_RotationSeed_FirstSkyCellInGridOrder()
    {
        // Cell 1 expresses a mechanical angle (never converts), cell 2 a sky angle — the first SKY cell
        // seeds (design D6), not the first cell outright.
        TargetGroupRow group = Group(
            DiskRow(tsTargetKey: null, seconds: 600,
                config: Config(RotationExpression.Mechanical, 30.0)),
            DiskRow(tsTargetKey: null, filter: "O", seconds: 600,
                config: Config(RotationExpression.Sky, 57.3)));

        (BulkAdoptionFacts? facts, _) = AdoptionPlanner.GetBulkFacts(group, Graph(), Ts());

        Assert.Equal(57.3, facts!.SeededRotationDeg);
    }

    [Fact]
    public void GetBulkFacts_NoEligibleCells_Refuses()
    {
        TargetGroupRow group = Group(DiskRow(seconds: 900));   // only a split row
        (BulkAdoptionFacts? facts, string? refusal) = AdoptionPlanner.GetBulkFacts(group, Graph(), Ts());
        Assert.Null(facts);
        Assert.Contains("no adoptable cells", refusal);
    }

    [Fact]
    public void GetBulkFacts_NoCentroid_Refuses()
    {
        TargetGroupRow group = Group(DiskRow(tsTargetKey: null, seconds: 600));
        (BulkAdoptionFacts? facts, string? refusal) =
            AdoptionPlanner.GetBulkFacts(group, Graph(ra: null), Ts());
        Assert.Null(facts);
        Assert.Contains("centroid", refusal);
    }

    // ---- bulk build -------------------------------------------------------------------------------------

    [Fact]
    public void BuildBulk_ExistingTarget_OnePlanPerAssignment_NoTargetRow()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", 0, 0, 1, 600));
        ReconciliationRow h600 = DiskRow(seconds: 600);
        ReconciliationRow o600 = DiskRow(filter: "O", seconds: 600, frames: 18);
        TargetGroupRow group = Group(h600, o600);

        (BulkAdoptionPlan? plan, string? refusal) = AdoptionPlanner.BuildBulk(group, Graph(), ts,
            new BulkAdoptionChoice(ts.Projects[0], [(h600, ts.Templates[0]), (o600, ts.Templates[1])]));

        Assert.Null(refusal);
        Assert.False(plan!.CreatesTarget);
        Assert.Equal(2, plan.PlanCount);
        Assert.Equal("NGC 7000 · 2 plans", plan.Label);
        Assert.Equal(2, plan.Rows.Count);
        Assert.All(plan.Rows, r => Assert.Equal(TsTable.ExposurePlan, r.Table));
        Assert.All(plan.Rows, r => Assert.Equal("t-7", r.Payload["targetid"]));
        Assert.Equal(42, plan.Rows[0].Payload["desired"]);          // born complete, per cell
        Assert.Equal(18, plan.Rows[1].Payload["desired"]);
        Assert.Equal(600.0, plan.Rows[0].Payload["exposure"]);      // H900 default 900 ≠ 600 → explicit
        Assert.Equal(-1.0, plan.Rows[1].Payload["exposure"]);       // O600 default matches → sentinel
    }

    [Fact]
    public void BuildBulk_MixedPairingOutcomes_SeedPerCell()
    {
        // Each cell seeds by its OWN pairing verdict (pairing-credited-write-back): two pairing cells are
        // born complete, the cautioned non-pairing cell is born empty — one batch, three different seeds.
        TsPlanData ts = Ts(
            new TsExposureTemplate(22, "prof-1", "O600", "O", Gain: 0, Offset: 0, Bin: 1, 600),
            new TsExposureTemplate(23, "prof-1", "S600", "S", Gain: 0, Offset: 0, Bin: 1, 600));
        ReconciliationRow h600 = DiskRow(seconds: 600, frames: 30);
        ReconciliationRow o600 = DiskRow(filter: "O", seconds: 600, frames: 12);
        ReconciliationRow s600 = DiskRow(filter: "S", seconds: 600, frames: 18, config: Config(gain: 53));
        TargetGroupRow group = Group(h600, o600, s600);

        (BulkAdoptionPlan? plan, string? refusal) = AdoptionPlanner.BuildBulk(group, Graph(), ts,
            new BulkAdoptionChoice(ts.Projects[0],
                [(h600, ts.Templates[0]), (o600, ts.Templates[1]), (s600, ts.Templates[2])]));

        Assert.Null(refusal);
        Assert.Equal(30, plan!.Rows[0].Payload["desired"]);
        Assert.Equal(30, plan.Rows[0].Payload["acquired"]);
        Assert.Equal(12, plan.Rows[1].Payload["desired"]);
        Assert.Equal(0, plan.Rows[2].Payload["desired"]);    // gain 53 vs template gain 0 — born empty
        Assert.Equal(0, plan.Rows[2].Payload["acquired"]);
        Assert.Equal(0, plan.Rows[2].Payload["accepted"]);
        Assert.Equal(1, plan.Rows[2].Payload["enabled"]);
    }

    [Fact]
    public void BuildBulk_DiskOnlyTarget_TargetOnce_ThenPlansReferencingItsGuid()
    {
        TsPlanData ts = Ts(new TsExposureTemplate(22, "prof-1", "O600", "O", 0, 0, 1, 600));
        ReconciliationRow h600 = DiskRow(tsTargetKey: null, seconds: 600);
        ReconciliationRow o600 = DiskRow(tsTargetKey: null, filter: "O", seconds: 600);
        TargetGroupRow group = Group(h600, o600);

        (BulkAdoptionPlan? plan, string? refusal) = AdoptionPlanner.BuildBulk(group, Graph(), ts,
            new BulkAdoptionChoice(ts.Projects[0], [(h600, ts.Templates[0]), (o600, ts.Templates[1])]));

        Assert.Null(refusal);
        Assert.True(plan!.CreatesTarget);
        Assert.Equal(3, plan.Rows.Count);
        Assert.Equal(TsTable.Target, plan.Rows[0].Table);
        Assert.Equal("p-101", plan.Rows[0].Payload["projectid"]);
        Assert.Equal("Sh2-119", plan.Rows[0].Payload["name"]);
        object? targetGuid = plan.Rows[0].Payload["guid"];
        Assert.Equal(targetGuid, plan.Rows[1].Payload["targetid"]);   // same-batch guid reference…
        Assert.Equal(targetGuid, plan.Rows[2].Payload["targetid"]);   // …for every plan in the batch
    }

    [Fact]
    public void BuildBulk_RotationSeed_FirstIncludedSkyCell()
    {
        TsPlanData ts = Ts();
        ReconciliationRow mech = DiskRow(tsTargetKey: null, seconds: 600,
            config: Config(RotationExpression.Mechanical, 30.0));
        ReconciliationRow sky = DiskRow(tsTargetKey: null, filter: "O", seconds: 600,
            config: Config(RotationExpression.Sky, 57.3));
        TargetGroupRow group = Group(mech, sky);

        (BulkAdoptionPlan? plan, _) = AdoptionPlanner.BuildBulk(group, Graph(), ts,
            new BulkAdoptionChoice(ts.Projects[0], [(mech, ts.Templates[0]), (sky, ts.Templates[0])]));

        Assert.Equal(57.3, plan!.Rows[0].Payload["rotation"]);
    }

    [Fact]
    public void BuildBulk_NoAssignments_Refuses()
    {
        TsPlanData ts = Ts();
        TargetGroupRow group = Group(DiskRow(seconds: 600));
        (BulkAdoptionPlan? plan, string? refusal) = AdoptionPlanner.BuildBulk(group, Graph(), ts,
            new BulkAdoptionChoice(ts.Projects[0], []));
        Assert.Null(plan);
        Assert.Contains("no cells included", refusal);
    }

    [Fact]
    public void BuildBulk_NoCentroid_AbortsTheWholeBatch_NamingTheCell()
    {
        TsPlanData ts = Ts();
        ReconciliationRow h600 = DiskRow(tsTargetKey: null, seconds: 600);
        TargetGroupRow group = Group(h600);

        (BulkAdoptionPlan? plan, string? refusal) = AdoptionPlanner.BuildBulk(group, Graph(ra: null), ts,
            new BulkAdoptionChoice(ts.Projects[0], [(h600, ts.Templates[0])]));

        Assert.Null(plan);                       // nothing partial — no target, no plans
        Assert.Contains("centroid", refusal);
        Assert.Contains("NGC 7000 · H", refusal);   // the offending cell, named
    }

    // ---- VM gate + cancel (the accept leg ends in a real disk-scan reload — field-verified, like the
    // per-cell funnel; the loader seam is the deferred M2 item) ------------------------------------------

    [Fact]
    public void IsTargetAdoptable_RequiresALoad_AndAnEligibleChild()
    {
        MainViewModel vm = new(Gate());
        TargetGroupRow eligible = Group(DiskRow(seconds: 600));
        TargetGroupRow fullyPlanned = Group(DiskRow(seconds: 900));   // split row only

        Assert.False(vm.IsTargetAdoptable(eligible));                 // no load yet
        vm.SetLoadForTest(Load());
        Assert.True(vm.IsTargetAdoptable(eligible));
        Assert.False(vm.IsTargetAdoptable(fullyPlanned));
    }

    [Fact]
    public async Task AdoptTargetAsync_UnsetPrompt_CancelsWithoutWriting()
    {
        MainViewModel vm = new(Gate());                               // editor factory throws — any write would fail loudly
        vm.SetLoadForTest(Load());

        Assert.False(await vm.AdoptTargetAsync(Group(DiskRow(seconds: 600))));
        Assert.Equal(0, vm.Sync.Journal.CollapsedCount);              // nothing journaled
    }

    [Fact]
    public async Task AdoptTargetAsync_StructuralRefusal_SurfacesThroughTheRefusalPrompt()
    {
        MainViewModel vm = new(Gate());
        vm.SetLoadForTest(Load(Graph(ra: null)));                     // disk-only target, no centroid
        string? surfaced = null;
        vm.AdoptRefusalPrompt = reason => { surfaced = reason; return Task.CompletedTask; };

        Assert.False(await vm.AdoptTargetAsync(Group(DiskRow(tsTargetKey: null, seconds: 600))));
        Assert.Contains("centroid", surfaced);
        Assert.Equal(0, vm.Sync.Journal.CollapsedCount);
    }

    private static TsEditGate Gate() => new(
        SyncTestEnv.NewSync(out _), _ => throw new InvalidOperationException("no editor in bulk-adoption tests"));

    private static LoadResult Load(CatalogGraph? graph = null) => new(
        [], Report(), graph ?? Graph(), Ts(), TimeSpan.Zero, new Dictionary<string, string>());

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);
}
