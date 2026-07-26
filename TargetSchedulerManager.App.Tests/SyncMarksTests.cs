using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The marks resolver in isolation: journal (→) + inbound store (←) + the graph-derived maps (plan keys,
// template keys, display names), resolved to glyph + tooltip per row kind. Covers the spec's mark-meaning,
// rollup, template-scope, own-scope attribution, and tooltip requirements.
public class SyncMarksTests
{
    private static readonly Guid Tid = Guid.NewGuid();

    private static TsJournal NewJournal() =>
        new(Path.Combine(SyncTestEnv.NewDir(), "edits.jsonl"));

    private static ExposurePlan Plan(string tsKey, Guid? targetId = null, Guid? templateId = null) =>
        new(Guid.NewGuid(), targetId ?? Tid, templateId ?? Guid.NewGuid(),
            ExposureSeconds: 300, DesiredCount: 10, AcquiredCount: 5, AcceptedCount: 5,
            Enabled: true, ImportedFromTsGuid: tsKey);

    private static ExposureTemplate Template(string tsKey, string name, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), name, "H", Gain: null, OffsetAdu: null, Binning: null,
            ReadoutMode: null, DefaultExposureSeconds: 900, ImportedFromTsGuid: tsKey);

    private static Target TargetRow(string tsKey, string name) =>
        new(Guid.NewGuid(), TargetSource.Both, ProjectId: null, name, Enabled: true,
            RaHours: null, DecDegreesSigned: null, Epoch.J2000, RotationDeg: null, RoiPercent: null,
            Priority: null, DirectoryName: null, Catalog: null, CommonName: null, ObjectName: null,
            ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: tsKey);

    private static Project ProjectRow(string tsKey, string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, Description: null, ProjectState.Active,
            ProjectPriority.Normal, MinimumAltitudeDeg: null, MaximumAltitudeDeg: null,
            MinimumTimeMinutes: null, UseCustomHorizon: false, HorizonOffsetDeg: null,
            MeridianWindowMinutes: null, IsMosaic: false, EnableGrader: false,
            CreatedAt: 0, ActiveAt: null, InactiveAt: null, ImportedFromTsGuid: tsKey);

    private static CatalogGraph Graph(
        IReadOnlyList<ExposurePlan>? plans = null,
        IReadOnlyList<ExposureTemplate>? templates = null,
        IReadOnlyList<Target>? targets = null,
        IReadOnlyList<Project>? projects = null) =>
        new([], projects ?? [], templates ?? [], targets ?? [], plans ?? [], []);

    // ---- leaf (ForPlan) -----------------------------------------------------------------------------------

    [Fact]
    public void CleanPlan_AndKeylessRow_AreBlank()
    {
        SyncMarks marks = SyncMarks.Build(NewJournal(), new TsInboundStore(), null);
        Assert.Equal(("", null), marks.ForPlan("42"));
        Assert.Equal(("", null), marks.ForPlan(null));   // disk-plane / rollup rows: structurally blank
    }

    [Fact]
    public void UnpushedEdit_MarksOut_WithOldToNewTooltip()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        (string glyph, string? tooltip) = marks.ForPlan("42");
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("desired 20 → 25", tooltip);
        Assert.Contains("unpushed", tooltip);
    }

    [Fact]
    public void WriteBackStamp_MarksOut_LikeAnyEdit()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.WriteBack, TsTable.ExposurePlan, "42", "acquired", 14, "10", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        Assert.Equal(SyncMarks.Out, marks.ForPlan("42").Glyph);
    }

    [Fact]
    public void InboundChange_MarksIn_WithBirdwatcherTooltip()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "acquired", "10", "14")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, null);

        (string glyph, string? tooltip) = marks.ForPlan("42");
        Assert.Equal(SyncMarks.In, glyph);
        Assert.Contains("BIRDWATCHER: acquired 10 → 14", tooltip);
    }

    [Fact]
    public void BothDirections_MarkBothWays_AndCollapseToIn_WhenJournalClears()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "exposure", 600, "300", "A · H");
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "desired", "20", "30")]);

        Assert.Equal(SyncMarks.BothWays, SyncMarks.Build(journal, inbound, null).ForPlan("42").Glyph);

        journal.Clear();   // the push applied — outbound gone, the rig's change stays visible
        Assert.Equal(SyncMarks.In, SyncMarks.Build(journal, inbound, null).ForPlan("42").Glyph);
    }

    [Fact]
    public void NewRemoteRow_TooltipSaysNewRow()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "43", TsInboundDiff.NewRowColumn, null, "row")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, null);

        Assert.Contains("new row", marks.ForPlan("43").Tooltip);
    }

    // ---- template-scope (ForPlan inheritance + ForTemplate) -----------------------------------------------

    [Fact]
    public void TemplateEdit_MarksEveryPlanUsingIt_WithAttribution()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "moonavoidanceenabled", 1, "0", "H900");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("42", templateId: templateId), Plan("43", templateId: templateId), Plan("99")],
            templates: [Template("5", "H900", templateId)]));

        (string glyph, string? tooltip) = marks.ForPlan("42");
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("template 'H900'", tooltip);
        Assert.Contains("moonavoidanceenabled 0 → 1", tooltip);
        Assert.Equal(SyncMarks.Out, marks.ForPlan("43").Glyph);
        Assert.Equal("", marks.ForPlan("99").Glyph);   // a plan on a different template stays clean
    }

    [Fact]
    public void InboundTemplateChange_MarksUsingPlansIn()
    {
        Guid templateId = Guid.NewGuid();
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposureTemplate, "5", "moonavoidanceseparation", "60", "45")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, Graph(
            plans: [Plan("42", templateId: templateId)],
            templates: [Template("5", "H900", templateId)]));

        (string glyph, string? tooltip) = marks.ForPlan("42");
        Assert.Equal(SyncMarks.In, glyph);
        Assert.Contains("BIRDWATCHER — template 'H900': moonavoidanceseparation 60 → 45", tooltip);
    }

    [Fact]
    public void PlanEdit_AndInboundTemplateChange_UnionToBothWays_WithDistinguishableLines()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposureTemplate, "5", "gain", "139", "100")]);
        SyncMarks marks = SyncMarks.Build(journal, inbound, Graph(
            plans: [Plan("42", templateId: templateId)],
            templates: [Template("5", "H900", templateId)]));

        (string glyph, string? tooltip) = marks.ForPlan("42");
        Assert.Equal(SyncMarks.BothWays, glyph);
        Assert.Contains("→ unpushed: desired 20 → 25", tooltip);                       // direct: unattributed
        Assert.Contains("← BIRDWATCHER — template 'H900': gain 139 → 100", tooltip);   // inherited: attributed
    }

    [Fact]
    public void TemplateNameFallsBackToRawKey_WhenGraphLacksIt()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "H900");
        // The template row itself carries no name here — simulate by naming it its key.
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("42", templateId: templateId)],
            templates: [Template("5", "5", templateId)]));

        Assert.Contains("template '5'", marks.ForPlan("42").Tooltip);
    }

    [Fact]
    public void ZeroUseTemplate_MarksNoRow_ButForTemplateShowsIt()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "H900");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("42")],                       // references some other template
            templates: [Template("5", "H900")]));

        Assert.Equal("", marks.ForPlan("42").Glyph);
        Assert.Equal("", marks.ForKeys([], projectKey: null, [Tid]).Glyph);

        (string glyph, string? tooltip) = marks.ForTemplate("5");
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("gain 139 → 100", tooltip);
    }

    [Fact]
    public void ForTemplate_CleanTemplate_IsBlank()
    {
        SyncMarks marks = SyncMarks.Build(NewJournal(), new TsInboundStore(), null);
        Assert.Equal(("", null), marks.ForTemplate("5"));
    }

    // ---- per-field (ForField — the flyout rows) -----------------------------------------------------------

    [Fact]
    public void ForField_UnpushedField_ResolvesOut_WithGrammarTooltip()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "moonavoidanceenabled", 1, "0", "H900");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        (string glyph, string? tooltip) = marks.ForField(TsTable.ExposureTemplate, "5", "moonavoidanceenabled");
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Equal("→ unpushed: moonavoidanceenabled 0 → 1", tooltip);
    }

    [Fact]
    public void ForField_InboundField_ResolvesIn()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "acquired", "10", "14")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, null);

        (string glyph, string? tooltip) = marks.ForField(TsTable.ExposurePlan, "42", "acquired");
        Assert.Equal(SyncMarks.In, glyph);
        Assert.Equal("← BIRDWATCHER: acquired 10 → 14", tooltip);
    }

    [Fact]
    public void ForField_ExactFieldCollision_IsBothWays_SiblingStaysSingle()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "exposure", 600, "300", "A · H");
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "desired", "20", "30")]);
        SyncMarks marks = SyncMarks.Build(journal, inbound, null);

        (string glyph, string? tooltip) = marks.ForField(TsTable.ExposurePlan, "42", "desired");
        Assert.Equal(SyncMarks.BothWays, glyph);                        // the exact-field collision
        Assert.Contains("← BIRDWATCHER: desired 20 → 30", tooltip);
        Assert.Contains("→ unpushed: desired 20 → 25", tooltip);
        Assert.Equal(SyncMarks.Out, marks.ForField(TsTable.ExposurePlan, "42", "exposure").Glyph);
    }

    [Fact]
    public void ForField_CleanField_IsBlank_EvenOnAMarkedRow()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        Assert.Equal(("", null), marks.ForField(TsTable.ExposurePlan, "42", "enabled"));
        Assert.Equal(("", null), marks.ForField(TsTable.ExposurePlan, "77", "desired"));
    }

    [Fact]
    public void ForField_NewRowEntry_NeverSurfacesPerField()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "43", TsInboundDiff.NewRowColumn, null, "row")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, null);

        Assert.Equal(("", null), marks.ForField(TsTable.ExposurePlan, "43", "desired"));
        Assert.Equal(("", null), marks.ForField(TsTable.ExposurePlan, "43", TsInboundDiff.NewRowColumn));
    }

    // ---- header (ForKeys) ---------------------------------------------------------------------------------

    [Fact]
    public void FoldedPlan_RollsUpToHeader_ViaGraphMap()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "77", "desired", 9, "5", "A · H");
        // The grid folded plan 77 into a multi-plan rollup (no row-level key) — only the graph knows it.
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(plans: [Plan("77")]));

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [Tid]).Glyph);
    }

    [Fact]
    public void RowCarriedPlanKeys_RollUp_WithoutAGraph()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [], ["42"]).Glyph);
    }

    [Fact]
    public void TargetEdit_MarksViaTargetKey_WithAttributedLine()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.Target, "guid-A", "active", 0, "1", "A");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            targets: [TargetRow("guid-A", "M 81")]));

        (string glyph, string? tooltip) = marks.ForKeys(["guid-A"], projectKey: null, []);
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("→ unpushed — target 'M 81': active 1 → 0", tooltip);
    }

    [Fact]
    public void ProjectEdit_MarksTheProjectKeyHolder_NotThePanelCall_AndIsAttributed()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.Project, "7", "minimumaltitude", 45, "30", "Nebulae - Above 45");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            projects: [ProjectRow("7", "Nebulae - Above 45")]));

        (string glyph, string? tooltip) = marks.ForKeys([], projectKey: "7", []);      // the parent header
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("→ unpushed — project 'Nebulae - Above 45': minimumaltitude 30 → 45", tooltip);
        Assert.Equal("", marks.ForKeys(["panel-guid"], projectKey: null, []).Glyph);   // panels pass no project key
    }

    [Fact]
    public void OwnScopeAttribution_FallsBackToRawKey_WithoutAGraph()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.Target, "guid-A", "active", 0, "1", "A");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), null);

        Assert.Contains("target 'guid-A'", marks.ForKeys(["guid-A"], projectKey: null, []).Tooltip);
    }

    [Fact]
    public void MixedDirections_UnionToBothWays_RolledUpAsCounts_OwnScopeAsLines()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.Target, "guid-A", "active", 0, "1", "A");
        TsInboundStore inbound = new();
        inbound.Apply(
        [
            new TsInboundChange(TsTable.ExposurePlan, "77", "acquired", "3", "4"),
            new TsInboundChange(TsTable.ExposurePlan, "77", "accepted", "3", "4"),
        ]);
        SyncMarks marks = SyncMarks.Build(journal, inbound, Graph(plans: [Plan("42"), Plan("77")]));

        (string glyph, string? tooltip) = marks.ForKeys(["guid-A"], projectKey: null, [Tid]);
        Assert.Equal(SyncMarks.BothWays, glyph);
        Assert.Contains("2 field(s) arrived changed", tooltip);        // rolled-up plan fields: counts
        Assert.Contains("1 field(s) unpushed", tooltip);               // the target edit is a line, not a count
        Assert.Contains("target 'guid-A'", tooltip);
        Assert.DoesNotContain("desired 20 → 25", tooltip);             // rolled-up detail lives on the leaf row
    }

    [Fact]
    public void Header_CountsASharedTemplateFieldOnce()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "H900");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("42", templateId: templateId), Plan("43", templateId: templateId)],
            templates: [Template("5", "H900", templateId)]));

        (string glyph, string? tooltip) = marks.ForKeys([], projectKey: null, [Tid]);
        Assert.Equal(SyncMarks.Out, glyph);
        Assert.Contains("1 field(s) unpushed", tooltip);   // once — not once per plan sharing the template
    }

    [Fact]
    public void FoldedPlansTemplate_RollsUpToHeader_ViaGraphMap()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "H900");
        // No row-carried plan keys at all — only the graph links target → plan → template.
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("77", templateId: templateId)],
            templates: [Template("5", "H900", templateId)]));

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [Tid]).Glyph);
    }

    [Fact]
    public void RowCarriedPlanKey_ResolvesItsTemplate_WithoutTargetIds()
    {
        Guid templateId = Guid.NewGuid();
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "H900");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), Graph(
            plans: [Plan("42", templateId: templateId)],
            templates: [Template("5", "H900", templateId)]));

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [], ["42"]).Glyph);
    }
}
