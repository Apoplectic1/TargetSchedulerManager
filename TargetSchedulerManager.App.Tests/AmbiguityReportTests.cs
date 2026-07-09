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
        Assert.Equal(4, CountOf(r.Markdown, "✓ none"));           // identity, duplicates, plans, templates
        Assert.Contains("✓ nothing to note", r.Markdown);         // info section's clean marker
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
        Assert.Contains("**FishHead** (TS Id 41)", r.Markdown);
        Assert.Contains("Rename TS target `FishHead` → `IC 1795`", r.Markdown);
        Assert.Contains("2 filter-cell(s) held", r.Markdown);
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
    public void AliasFold_And_UnplannedFrames_AreInfoNotAction()
    {
        WriteBackPlan plan = new([], [],
            [new ReconcileNote(ReconcileNote.UnplannedFramesKind, "M27", "H Light 3 frames @600s - no TS plan at 600s")], 0);

        AmbiguityReportResult r = Build(Graph(),
            Report(aliases: [new AliasTsTarget("M27 - Dumbell", ["M27", "Dumbell"])]), plan);

        Assert.Equal(0, r.ActionCount);
        Assert.Contains("Alias fold: disk `M27 - Dumbell`", r.Markdown);
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
        Assert.Contains("Id 299", r.Markdown);
        Assert.Contains("Id 1040", r.Markdown);
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
        Assert.Contains("Id 10 | Id 11", r.Markdown);
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

    // ---- builders --------------------------------------------------------------------------------------------

    private static AmbiguityReportResult Build(
        CatalogGraph graph, CatalogBuildReport? report = null, WriteBackPlan? plan = null) =>
        AmbiguityReport.Build(graph, report ?? Report(), plan ?? EmptyPlan(),
            new DateTimeOffset(2026, 7, 8, 21, 0, 0, TimeSpan.FromHours(-4)),
            @"X:\ts\schedulerdb.sqlite", @"X:\library", toleranceDegrees: 0.5);

    private static CatalogGraph Graph(
        IReadOnlyList<Target>? targets = null,
        IReadOnlyList<ExposurePlan>? plans = null,
        IReadOnlyList<ExposureTemplate>? templates = null) =>
        new([], [], templates ?? [], targets ?? [], plans ?? [], []);

    private static WriteBackPlan EmptyPlan() => new([], [], [], 0);

    private static ManualGroup Manual(Guid target, string name, string filter, int seconds, ManualReason reason) =>
        new(target, name, filter, FilterPurpose.Light, seconds, DiskCount: 1, reason,
            [new ManualPlan(1, seconds, 0, 0, 10), new ManualPlan(2, seconds, 0, 0, 10)]);

    private static Target Tgt(
        Guid id, TargetSource source, string name, string? dir = null, string? tsKey = null,
        double? ra = 1.0, double? dec = 10.0) =>
        new(id, source, ProjectId: null, name, Enabled: true, RaHours: ra, DecDegreesSigned: dec,
            Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: dir,
            Catalog: null, CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0,
            ImportedFromTsGuid: tsKey);

    private static ExposureTemplate Tpl(
        Guid id, string name, string filter, double defaultSeconds, Guid? profile = null, string? tsKey = "1") =>
        new(id, profile ?? Guid.Empty, name, filter, Gain: null, OffsetAdu: null, Binning: null,
            ReadoutMode: null, DefaultExposureSeconds: defaultSeconds, ImportedFromTsGuid: tsKey);

    private static ExposurePlan Plan(
        Guid target, Guid template, string tsKey, int desired = 10, double? exposure = null) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: exposure, DesiredCount: desired,
            AcquiredCount: 0, AcceptedCount: 0, Enabled: true, ImportedFromTsGuid: tsKey);

    private static CatalogBuildReport Report(
        IReadOnlyList<NameMismatch>? mismatches = null,
        IReadOnlyList<AmbiguousMatch>? ambiguous = null,
        IReadOnlyList<DuplicateTsTarget>? duplicates = null,
        IReadOnlyList<AliasTsTarget>? aliases = null) =>
        new(DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
            NameMismatches: mismatches ?? [], AmbiguousMatches: ambiguous ?? [],
            DuplicateTsTargets: duplicates ?? [], AliasTsTargets: aliases ?? [],
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
