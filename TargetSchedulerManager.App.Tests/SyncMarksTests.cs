using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The marks resolver in isolation: journal (→) + inbound store (←) + the graph's plan-key map, resolved to
// glyph + tooltip per row kind. Covers the spec's mark-meaning, rollup, project-scope, template-gap, and
// tooltip requirements.
public class SyncMarksTests
{
    private static readonly Guid Tid = Guid.NewGuid();

    private static TsJournal NewJournal() =>
        new(Path.Combine(SyncTestEnv.NewDir(), "edits.jsonl"));

    private static ExposurePlan Plan(string tsKey, Guid? targetId = null) =>
        new(Guid.NewGuid(), targetId ?? Tid, Guid.NewGuid(),
            ExposureSeconds: 300, DesiredCount: 10, AcquiredCount: 5, AcceptedCount: 5,
            Enabled: true, ImportedFromTsGuid: tsKey);

    // ---- leaf (ForPlan) -----------------------------------------------------------------------------------

    [Fact]
    public void CleanPlan_AndKeylessRow_AreBlank()
    {
        SyncMarks marks = SyncMarks.Build(NewJournal(), new TsInboundStore(), []);
        Assert.Equal(("", null), marks.ForPlan("42"));
        Assert.Equal(("", null), marks.ForPlan(null));   // disk-plane / rollup rows: structurally blank
    }

    [Fact]
    public void UnpushedEdit_MarksOut_WithOldToNewTooltip()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), []);

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
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), []);

        Assert.Equal(SyncMarks.Out, marks.ForPlan("42").Glyph);
    }

    [Fact]
    public void InboundChange_MarksIn_WithBirdwatcherTooltip()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "acquired", "10", "14")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, []);

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

        Assert.Equal(SyncMarks.BothWays, SyncMarks.Build(journal, inbound, []).ForPlan("42").Glyph);

        journal.Clear();   // the push applied — outbound gone, the rig's change stays visible
        Assert.Equal(SyncMarks.In, SyncMarks.Build(journal, inbound, []).ForPlan("42").Glyph);
    }

    [Fact]
    public void NewRemoteRow_TooltipSaysNewRow()
    {
        TsInboundStore inbound = new();
        inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "43", TsInboundDiff.NewRowColumn, null, "row")]);
        SyncMarks marks = SyncMarks.Build(NewJournal(), inbound, []);

        Assert.Contains("new row", marks.ForPlan("43").Tooltip);
    }

    // ---- header (ForKeys) ---------------------------------------------------------------------------------

    [Fact]
    public void FoldedPlan_RollsUpToHeader_ViaGraphMap()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "77", "desired", 9, "5", "A · H");
        // The grid folded plan 77 into a multi-plan rollup (no row-level key) — only the graph knows it.
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), [Plan("77")]);

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [Tid]).Glyph);
    }

    [Fact]
    public void RowCarriedPlanKeys_RollUp_WithoutAGraph()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "42", "desired", 25, "20", "A · H");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), []);

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: null, [], ["42"]).Glyph);
    }

    [Fact]
    public void TargetEdit_MarksViaTargetKey()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.Target, "guid-A", "active", 0, "1", "A");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), []);

        Assert.Equal(SyncMarks.Out, marks.ForKeys(["guid-A"], projectKey: null, []).Glyph);
    }

    [Fact]
    public void ProjectEdit_MarksTheProjectKeyHolder_NotThePanelCall()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.Project, "7", "priority", 2, "1", "Cygnus mosaic");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), []);

        Assert.Equal(SyncMarks.Out, marks.ForKeys([], projectKey: "7", []).Glyph);    // the parent header
        Assert.Equal("", marks.ForKeys(["panel-guid"], projectKey: null, []).Glyph);  // panels pass no project key
    }

    [Fact]
    public void MixedDirections_UnionToBothWays_WithCountsTooltip()
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
        SyncMarks marks = SyncMarks.Build(journal, inbound, [Plan("42"), Plan("77")]);

        (string glyph, string? tooltip) = marks.ForKeys(["guid-A"], projectKey: null, [Tid]);
        Assert.Equal(SyncMarks.BothWays, glyph);
        Assert.Contains("2 field(s) arrived changed", tooltip);
        Assert.Contains("2 field(s) unpushed", tooltip);
    }

    [Fact]
    public void TemplateEdit_MarksNothingAnywhere()
    {
        TsJournal journal = NewJournal();
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "gain", 100, "139", "Ha 6nm");
        SyncMarks marks = SyncMarks.Build(journal, new TsInboundStore(), [Plan("42")]);

        Assert.Equal("", marks.ForPlan("42").Glyph);
        Assert.Equal("", marks.ForKeys(["guid-A"], projectKey: "7", [Tid], ["42", "5"]).Glyph);
    }
}
