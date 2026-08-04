using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The printable ambiguity report (pure builder): existing detections render with fix text, the three
/// TS-internal checks catch what the grid can't badge, alias folds stay informational, and a clean system
/// produces an affirmative zero-action report.
/// </summary>
public class AmbiguityReportTests
{
    // ---- 3.1 existing detections + shape -------------------------------------------------------------------

    [Fact]
    public void Clean_ZeroActions_EverySectionAffirmative()
    {
        AmbiguityReportResult r = Build(Graph(), Report(), EmptyPlan());

        Assert.Equal(0, r.ActionCount);
        Assert.Contains("All checks clean — 0 action items", r.Markdown);
        Assert.Equal(5, CountOf(r.Markdown, "✓ none"));           // identity, duplicates, plans, templates, unreadable
        Assert.Contains("✓ nothing to note", r.Markdown);         // info section's clean marker
    }

    // ---- unreadable files + framing info (openspec framing-overlap-column) ---------------------------------

    [Fact]
    public void UnreadableFiles_AreActionItems_WithPathAndReason()
    {
        // A skipped frame silently lowers the Actual counts — the report is where the loss becomes a
        // repairable item, path in hand.
        AmbiguityReportResult r = Build(Graph(), skippedFiles: new Dictionary<string, string>
        {
            [@"X:\library\M 81\H\bad.xisf"] = "XISF header missing the mandatory Image geometry",
        });

        Assert.Equal(1, r.ActionCount);
        Assert.Contains("Fix on disk — unreadable files", r.Markdown);
        Assert.Contains(@"`X:\library\M 81\H\bad.xisf` — XISF header missing", r.Markdown);
        Assert.Contains("every count this load excludes it", r.Markdown);
    }

    [Fact]
    public void OffPlanPointing_IsInfoNotAction()
    {
        // A framing at the plan's own angle, pointed 0.15° off its center: it SERVES (no badge anywhere in
        // the grid), so the report's info section is the only place the displacement speaks. 84% with the
        // Z183 field — the same union rule the badge decoration uses, priced by the library.
        Guid t = Guid.NewGuid();
        AmbiguityReportResult r = Build(Graph(
            targets: [Tgt(t, TargetSource.Both, "Markarian's Chain", dir: "Markarian's Chain",
                ra: 19.5, dec: 10.0, rotation: 0.0)],
            inventory: [Inv(t, "L", FilterPurpose.Light, 71, 300.0, rotationFold: 0.0,
                centroidRaHours: 19.5, centroidDecDeg: 10.15,
                fieldWidthDeg: 1.423, fieldHeightDeg: 0.951)]));

        Assert.Equal(0, r.ActionCount);
        Assert.Contains("Off-plan pointing: **Markarian's Chain", r.Markdown);
        Assert.Contains("84% of its footprint", r.Markdown);
        Assert.Contains("rotation serves — the grid shows no badge", r.Markdown);
    }

    [Fact]
    public void OnFootprintFraming_ReportsNothing()
    {
        // Same shape, centered on the plan: above the on-footprint threshold, nothing to note.
        Guid t = Guid.NewGuid();
        AmbiguityReportResult r = Build(Graph(
            targets: [Tgt(t, TargetSource.Both, "M 33", dir: "M 33", ra: 19.5, dec: 10.0, rotation: 110.0)],
            inventory: [Inv(t, "L", FilterPurpose.Light, 430, 300.0, rotationFold: 110.0,
                centroidRaHours: 19.5, centroidDecDeg: 10.0,
                fieldWidthDeg: 1.423, fieldHeightDeg: 0.951)]));

        Assert.DoesNotContain("Off-plan pointing", r.Markdown);
        Assert.DoesNotContain("Mixed-sensor framing", r.Markdown);
    }

    [Fact]
    public void NameMismatch_RenameFix_WithHeldCellCount()
    {
        Guid fish = Guid.NewGuid();
        CatalogGraph graph = Graph(targets: [Tgt(fish, TargetSource.Both, "FishHead", dir: "IC 1795 - Fish Head", tsKey: "41")]);
        CatalogBuildReport report = Report(mismatches:
            [new NameMismatch("41", "FishHead", "IC 1795 - Fish Head", "IC1795", 0.004)]);
        WriteBackPlan plan = new([], [
            Manual(fish, "FishHead", "H", 900, ManualReason.IdentityConflict),
            Manual(fish, "FishHead", "O", 900, ManualReason.IdentityConflict),
        ], [], 0);

        AmbiguityReportResult r = Build(graph, report, plan);

        Assert.Equal(1, r.ActionCount);                            // one target = one fix; held cells fold in
        Assert.Contains("**FishHead** —", r.Markdown);
        Assert.DoesNotContain("TS Id", r.Markdown);                 // target guids are meaningless at the rig
        Assert.Contains("Rename TS target `FishHead` → `IC 1795`", r.Markdown);
        Assert.Contains("2 filter-cell(s) held", r.Markdown);
    }

    [Fact]
    public void NameMismatch_PanelPath_DescribesTokenDisagreement_NoBogusRename()
    {
        // Rosette-P4-shaped regression (real-data run 2026-07-08): a mismatch on a composite "mosaic/panel"
        // path must not prescribe renaming to the mosaic prefix ("→ Rename to `Mosaic`").
        AmbiguityReportResult r = Build(Graph(), Report(mismatches:
            [new NameMismatch(null, "Rosette P4", "Mosaic - Rosette/Panel Center", null, 0.196)]), EmptyPlan());

        Assert.Equal(1, r.ActionCount);
        Assert.Contains("claimed disk panel `Panel Center` of `Mosaic - Rosette`", r.Markdown);
        Assert.Contains("confirm which panel this really is", r.Markdown);
        Assert.DoesNotContain("Rename TS target `Rosette P4`", r.Markdown);
    }

    [Fact]
    public void DuplicateFold_ConsolidateFix_DesiredWarning()
    {
        AmbiguityReportResult r = Build(Graph(),
            Report(duplicates: [new DuplicateTsTarget("M42 - Orion", ["M42", "M42 core"])]), EmptyPlan());

        Assert.Equal(1, r.ActionCount);
        Assert.Contains("M42 | M42 core", r.Markdown);
        Assert.Contains("Check `desired`", r.Markdown);
    }

    [Fact]
    public void UnplannedFrames_AreInfoNotAction()
    {
        WriteBackPlan plan = new([], [],
            [new ReconcileNote(ReconcileNote.UnplannedFramesKind, "M27", "H Light 3 frames @600s - no TS plan at 600s")], 0);

        AmbiguityReportResult r = Build(Graph(), plan: plan);

        Assert.Equal(0, r.ActionCount);
        Assert.Contains("Unplanned frames: **M27**", r.Markdown);
        Assert.Contains("All checks clean", r.Markdown);           // info never trips the wire
    }

    // ---- 3.2 the TS-internal checks -------------------------------------------------------------------------

    [Fact]
    public void SameKey_HeldCell_RendersOnce_WithPlanIds()
    {
        Guid swan = Guid.NewGuid(), h900 = Guid.NewGuid();
        CatalogGraph graph = Graph(
            targets: [Tgt(swan, TargetSource.Both, "Swan", dir: "M17 - Swan", tsKey: "77")],
            templates: [Tpl(h900, "H900", "H", 900)],
            plans: [Plan(swan, h900, tsKey: "299", desired: 64), Plan(swan, h900, tsKey: "1040", desired: 1)]);
        WriteBackPlan plan = new([], [new ManualGroup(swan, "Swan", "H", FilterPurpose.Light, 900, 8,
            ManualReason.MultiPlan,
            [new ManualPlan(299, 900, 8, 9, 64), new ManualPlan(1040, 900, 1, 1, 1)])], [], 0);

        AmbiguityReportResult r = Build(graph, plan: plan);

        Assert.Equal(1, r.ActionCount);                            // manual item wins; same-key check de-dupes
        Assert.Equal(1, CountOf(r.Markdown, "Swan · H @900s"));
        Assert.Contains("\n  - H900 — desired 64, acq 8 / acc 9", r.Markdown);   // one indented row per plan,
        Assert.Contains("\n  - H900 — desired 1, acq 1 / acc 1", r.Markdown);    // template-named — the TS-UI shape
        Assert.DoesNotContain("Id 299", r.Markdown);
        Assert.Contains("disk has 8 frame(s)", r.Markdown);
    }

    [Fact]
    public void SameKey_PlannedOnlyTarget_CaughtWithoutWriteBack()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        CatalogGraph graph = Graph(
            targets: [Tgt(t, TargetSource.Planned, "Seagull", tsKey: "88")],
            templates: [Tpl(tpl, "O600", "O", 600)],
            plans: [Plan(t, tpl, tsKey: "5"), Plan(t, tpl, tsKey: "6")]);

        AmbiguityReportResult r = Build(graph);                     // empty manual: planner never saw it

        Assert.Equal(1, r.ActionCount);
        Assert.Contains("Seagull · O @600s", r.Markdown);
        Assert.Contains("No disk anchor", r.Markdown);
    }

    [Fact]
    public void SameKey_ExAliasShape_IsAnActionItem_NoExemption()
    {
        // The M27 shape the retired alias exemption used to wave through: two twins fold onto one
        // canonical target, so the key carries 2 plans. No exemptions any more — it reads as a same-key
        // duplicate demanding a hand fix (the fold masked the unintentional twin for weeks).
        Guid m27 = Guid.NewGuid(), h900 = Guid.NewGuid();
        CatalogGraph graph = Graph(
            targets: [Tgt(m27, TargetSource.Both, "M27 - Dumbell", dir: "M27 - Dumbell", tsKey: "9")],
            templates: [Tpl(h900, "H900", "H", 900)],
            plans: [Plan(m27, h900, tsKey: "19", desired: 129), Plan(m27, h900, tsKey: "1076", desired: 169)]);
        CatalogBuildReport report = Report(duplicates: [new DuplicateTsTarget("M27 - Dumbell", ["M27", "Dumbell"])]);

        AmbiguityReportResult r = Build(graph, report);

        Assert.Equal(2, r.ActionCount);                            // duplicate fold + same-key plans, both live
        Assert.Contains("2 plans share one key", r.Markdown);
        Assert.DoesNotContain("Alias fold", r.Markdown);
    }

    [Fact]
    public void SameFilter_DifferentSeconds_NotFlagged()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        CatalogGraph graph = Graph(
            targets: [Tgt(t, TargetSource.Planned, "X", tsKey: "1")],
            templates: [Tpl(tpl, "H900", "H", 900)],
            plans: [Plan(t, tpl, tsKey: "2", exposure: 300), Plan(t, tpl, tsKey: "3", exposure: 600)]);

        Assert.Equal(0, Build(graph).ActionCount);                  // different cells auto-resolve — the rule
    }

    [Fact]
    public void PlannedOnlyTwins_SameName_OneItem()
    {
        CatalogGraph graph = Graph(targets: [
            Tgt(Guid.NewGuid(), TargetSource.Planned, "M31", tsKey: "10", ra: 0.712, dec: 41.27),
            Tgt(Guid.NewGuid(), TargetSource.Planned, "M31", tsKey: "11", ra: 0.712, dec: 41.27)]);

        AmbiguityReportResult r = Build(graph);

        Assert.Equal(1, r.ActionCount);                            // one item for the pair, not one per twin
        Assert.Contains("2 planned-only TS targets share this name", r.Markdown);
        Assert.DoesNotContain("Id 10", r.Markdown);                 // twins discriminate by project, not raw ids
    }

    [Fact]
    public void PlannedOnlyPair_InsideTolerance_ReportedWithSeparation()
    {
        CatalogGraph graph = Graph(targets: [
            Tgt(Guid.NewGuid(), TargetSource.Planned, "SH2-101", tsKey: "20", ra: 20.0, dec: 35.0),
            Tgt(Guid.NewGuid(), TargetSource.Planned, "Tulip Wide", tsKey: "21", ra: 20.0, dec: 35.2)]);

        AmbiguityReportResult r = Build(graph);

        Assert.Equal(1, r.ActionCount);
        Assert.Contains("0.200°", r.Markdown);
        Assert.Contains("contest the same disk directory", r.Markdown);
    }

    [Fact]
    public void PlannedOnlyPair_OutsideTolerance_Clean()
    {
        CatalogGraph graph = Graph(targets: [
            Tgt(Guid.NewGuid(), TargetSource.Planned, "A", tsKey: "1", ra: 20.0, dec: 35.0),
            Tgt(Guid.NewGuid(), TargetSource.Planned, "B", tsKey: "2", ra: 20.0, dec: 36.0)]);

        Assert.Equal(0, Build(graph).ActionCount);                  // 1° apart > 0.5° tolerance
    }

    [Fact]
    public void DuplicateTemplateNames_SameProfileFlagged_CrossProfileClean()
    {
        Guid profile = Guid.NewGuid();
        CatalogGraph flagged = Graph(templates: [
            Tpl(Guid.NewGuid(), "H900", "H", 900, profile, tsKey: "1"),
            Tpl(Guid.NewGuid(), "H900", "H", 900, profile, tsKey: "2")]);
        CatalogGraph clean = Graph(templates: [
            Tpl(Guid.NewGuid(), "H900", "H", 900, Guid.NewGuid(), tsKey: "1"),
            Tpl(Guid.NewGuid(), "H900", "H", 900, Guid.NewGuid(), tsKey: "2")]);

        AmbiguityReportResult r = Build(flagged);
        Assert.Equal(1, r.ActionCount);
        Assert.Contains("2 templates share this name", r.Markdown);
        Assert.Equal(0, Build(clean).ActionCount);
    }

    [Fact]
    public void SentinelTemplate_IsAnActionItem_NamingFieldsAndBlastRadius()
    {
        // Field obs b22d: the report must say what CAUSED a `sentinel` badge — the template, exactly which
        // field(s) defer to the camera, and the plans riding on it.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        CatalogGraph graph = Graph(
            targets: [Tgt(t, TargetSource.Planned, "Abell 78", tsKey: "7")],
            templates: [Tpl(tpl, "Stars B", "B", 30, tsKey: "21", gain: null, readout: -1)],
            plans: [Plan(t, tpl, "31"), Plan(t, tpl, "32", exposure: 60.0)]);   // different seconds — no same-key item

        AmbiguityReportResult r = Build(graph);
        Assert.Equal(1, r.ActionCount);
        Assert.Contains("camera-default sentinel on gain + readout mode", r.Markdown);
        Assert.Contains("used by 2 plan(s) on [Abell 78]", r.Markdown);
        Assert.Contains("Set an explicit gain and readout mode", r.Markdown);
    }

    [Fact]
    public void SentinelTemplate_ZeroUse_StillAnItem_ExplicitTemplatesClean()
    {
        CatalogGraph sentinel = Graph(templates: [Tpl(Guid.NewGuid(), "O600", "O", 600, offset: null)]);
        AmbiguityReportResult r = Build(sentinel);
        Assert.Equal(1, r.ActionCount);
        Assert.Contains("no plans use it yet", r.Markdown);

        CatalogGraph clean = Graph(templates: [Tpl(Guid.NewGuid(), "O600", "O", 600)]);
        Assert.Equal(0, Build(clean).ActionCount);
    }

    // ---- builders --------------------------------------------------------------------------------------------

    private static AmbiguityReportResult Build(
        CatalogGraph graph, CatalogBuildReport? report = null, WriteBackPlan? plan = null,
        IReadOnlyDictionary<string, string>? skippedFiles = null) =>
        AmbiguityReport.Build(graph, report ?? Report(), plan ?? EmptyPlan(),
            new DateTimeOffset(2026, 7, 8, 21, 0, 0, TimeSpan.FromHours(-4)),
            @"X:\ts\schedulerdb.sqlite", @"X:\library", toleranceDegrees: 0.5, skippedFiles);

    private static CatalogGraph Graph(
        IReadOnlyList<Target>? targets = null,
        IReadOnlyList<ExposurePlan>? plans = null,
        IReadOnlyList<ExposureTemplate>? templates = null,
        IReadOnlyList<InventoryFilter>? inventory = null) =>
        new([], [], templates ?? [], targets ?? [], plans ?? [], inventory ?? []);

    private static WriteBackPlan EmptyPlan() => new([], [], [], 0);

    private static ManualGroup Manual(Guid target, string name, string filter, int seconds, ManualReason reason) =>
        new(target, name, filter, FilterPurpose.Light, seconds, DiskCount: 1, reason,
            [new ManualPlan(1, seconds, 0, 0, 10), new ManualPlan(2, seconds, 0, 0, 10)]);

    private static Target Tgt(
        Guid id, TargetSource source, string name, string? dir = null, string? tsKey = null,
        double? ra = 1.0, double? dec = 10.0, double? rotation = null) =>
        new(id, source, ProjectId: null, name, Enabled: true, RaHours: ra, DecDegreesSigned: dec,
            Epoch.J2000, RotationDeg: rotation, RoiPercent: null, Priority: null, DirectoryName: dir,
            Catalog: null, CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0,
            ImportedFromTsGuid: tsKey);

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds,
        double? rotationFold = 20.0, double? centroidRaHours = null, double? centroidDecDeg = null,
        double? fieldWidthDeg = null, double? fieldHeightDeg = null, bool spansSensors = false) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1,
            TypicalBinningY: 1, ExposureSeconds: seconds, Camera: "Z533",
            FramingOrdinal: 0, RotationExpression: RotationExpression.Sky, RotationFoldDeg: rotationFold,
            FramingCentroidRaHours: centroidRaHours, FramingCentroidDecDeg: centroidDecDeg,
            FramingFieldWidthDeg: fieldWidthDeg, FramingFieldHeightDeg: fieldHeightDeg,
            FramingSpansMultipleSensors: spansSensors);

    // Explicit config by default — a null gain/offset is the camera-default sentinel and would put a
    // sentinel action item into every test's report; pass nulls deliberately to test that item.
    private static ExposureTemplate Tpl(
        Guid id, string name, string filter, double defaultSeconds, Guid? profile = null, string? tsKey = "1",
        int? gain = 100, int? offset = 50, int? readout = 0) =>
        new(id, profile ?? Guid.Empty, name, filter, Gain: gain, OffsetAdu: offset, Binning: 1,
            ReadoutMode: readout, DefaultExposureSeconds: defaultSeconds, ImportedFromTsGuid: tsKey);

    private static ExposurePlan Plan(
        Guid target, Guid template, string tsKey, int desired = 10, double? exposure = null) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: exposure, DesiredCount: desired,
            AcquiredCount: 0, AcceptedCount: 0, Enabled: true, ImportedFromTsGuid: tsKey);

    private static CatalogBuildReport Report(
        IReadOnlyList<NameMismatch>? mismatches = null,
        IReadOnlyList<AmbiguousMatch>? ambiguous = null,
        IReadOnlyList<DuplicateTsTarget>? duplicates = null) =>
        new(DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
            NameMismatches: mismatches ?? [], AmbiguousMatches: ambiguous ?? [],
            DuplicateTsTargets: duplicates ?? [],
            UnanchoredTsTargets: [], InvalidTsTargets: []);

    private static int CountOf(string text, string token)
    {
        int count = 0;
        for (int i = text.IndexOf(token, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
