using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The marks lifecycle through the view-model: an applied edit marks its row + header in place (no grid
// rebuild), pushes/discards clear what they should, and disk-plane leaves never mark. Uses the gate seam
// (stub editor that always applies) over a temp-path TsSync — no XAML runtime, no Brush getters.
public class MainViewModelMarkTests
{
    private sealed class OkEditor : ITsEditor
    {
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) =>
            (new FieldEditResult(RowFound: true, OldValue: "old", Verified: true), RefusalReason.None);
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => true;
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);   // no test drives inserts through this stub
        public void Dispose() { }
    }

    private static MainViewModel Vm(out TsSync sync, params ReconciliationRow[] rows)
    {
        TsSync s = SyncTestEnv.NewSync(out _);
        sync = s;
        MainViewModel vm = new(new TsEditGate(s, _ => new OkEditor()));
        vm.SetRowsForTest(rows);
        return vm;
    }

    [Fact]
    public async Task CommitEdit_MarksRowAndHeaderInPlace()
    {
        MainViewModel vm = Vm(out _,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"));
        TargetGroupRow header = (TargetGroupRow)vm.Rows[0];
        ReconciliationRow row = header.Children[0];
        Assert.Equal("", row.MarkGlyph);

        Assert.True(await vm.SetPlanDesiredAsync(row, 12));

        Assert.Equal(SyncMarks.Out, row.MarkGlyph);           // in place — same row object
        Assert.Contains("desired", row.MarkTooltip);
        Assert.Equal(SyncMarks.Out, header.MarkGlyph);        // rolled up via the row-carried plan key
        Assert.Same(header, vm.Rows[0]);                      // no collection rebuild
    }

    [Fact]
    public async Task TargetLevelEdit_MarksHeader_NotLeaves()
    {
        MainViewModel vm = Vm(out _,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"),
            Make.Disk(target: "A", filter: "O"));
        TargetGroupRow header = (TargetGroupRow)vm.Rows[0];

        Assert.True(await vm.SetTargetEnabledAsync(header, false));

        Assert.Equal(SyncMarks.Out, header.MarkGlyph);
        Assert.All(header.Children, r => Assert.Equal("", r.MarkGlyph));   // incl. the disk-plane leaf
    }

    [Fact]
    public void PushCollapse_OutboundClears_UnmaskedInboundRemains()
    {
        MainViewModel vm = Vm(out TsSync sync,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"));
        sync.Journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "exposure", 600, "300", "A · H");
        sync.Inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "ep-1", "desired", "20", "30")]);
        vm.RefreshAllMarks();
        ReconciliationRow row = ((TargetGroupRow)vm.Rows[0]).Children[0];
        Assert.Equal(SyncMarks.BothWays, row.MarkGlyph);

        sync.Journal.Clear();                                 // the push applied every entry
        vm.RefreshAllMarks();

        Assert.Equal(SyncMarks.In, row.MarkGlyph);            // ⇄ collapses to ← (sticky inbound)
    }

    [Fact]
    public void PartialPush_RetainedEntryKeepsItsMark()
    {
        MainViewModel vm = Vm(out TsSync sync,
            Make.Leaf(target: "A", filter: "H", planTsKey: "ep-1", tsTargetKey: "guid-A"),
            Make.Leaf(target: "A", filter: "O", planTsKey: "ep-2", tsTargetKey: "guid-A"));
        TsJournalEntry applied = sync.Journal.Append(
            TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 12, "10", "A · H")!;
        TsJournalEntry failed = sync.Journal.Append(
            TsEditKind.Manual, TsTable.ExposurePlan, "ep-2", "desired", 9, "5", "A · O")!;

        sync.Journal.CommitPush([TsJournal.FieldKey(applied)], failed.Seq);   // ep-2 failed and was retained
        vm.RefreshAllMarks();

        TargetGroupRow header = (TargetGroupRow)vm.Rows[0];
        Assert.Equal("", header.Children[0].MarkGlyph);
        Assert.Equal(SyncMarks.Out, header.Children[1].MarkGlyph);
        Assert.Equal(SyncMarks.Out, header.MarkGlyph);
    }

    [Fact]
    public void Discard_ClearsOutboundMarks()
    {
        MainViewModel vm = Vm(out TsSync sync,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"));
        sync.Journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 12, "10", "A · H");
        vm.RefreshAllMarks();
        ReconciliationRow row = ((TargetGroupRow)vm.Rows[0]).Children[0];
        Assert.Equal(SyncMarks.Out, row.MarkGlyph);

        sync.Discard();
        vm.RefreshAllMarks();

        Assert.Equal("", row.MarkGlyph);
        Assert.Null(row.MarkTooltip);
    }

    [Fact]
    public void TemplateEdit_MarksUsingRowAndHeader_InPlace()
    {
        MainViewModel vm = Vm(out TsSync sync,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"));
        Guid templateId = Guid.NewGuid();
        vm.SetLoadForTest(new LoadResult([], Report(), new CatalogGraph([], [],
            [new ExposureTemplate(templateId, Guid.NewGuid(), "H900", "H", Gain: null, OffsetAdu: null,
                Binning: null, ReadoutMode: null, DefaultExposureSeconds: 900.0, ImportedFromTsGuid: "5")],
            [],
            [new ExposurePlan(Guid.NewGuid(), Guid.NewGuid(), templateId, ExposureSeconds: null,
                DesiredCount: 10, AcquiredCount: 0, AcceptedCount: 0, Enabled: true, ImportedFromTsGuid: "ep-1")],
            []), TsPlanData.Empty, TimeSpan.Zero, new Dictionary<string, string>()));
        sync.Journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "5", "moonavoidanceenabled", 1, "0", "H900");

        vm.RefreshAllMarks();

        TargetGroupRow header = (TargetGroupRow)vm.Rows[0];
        ReconciliationRow row = header.Children[0];
        Assert.Equal(SyncMarks.Out, row.MarkGlyph);                  // inherited through the plan's template
        Assert.Contains("template 'H900'", row.MarkTooltip);         // attributed, not a bare field line
        Assert.Equal(SyncMarks.Out, header.MarkGlyph);               // rolled up (template counted once)
        Assert.Same(header, vm.Rows[0]);                             // in place — no collection rebuild
    }

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);

    [Fact]
    public void OfflineSession_EmptyInbound_MeansNoInMarks()
    {
        MainViewModel vm = Vm(out TsSync sync,
            Make.Leaf(target: "A", planTsKey: "ep-1", tsTargetKey: "guid-A"));
        sync.Journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 12, "10", "A · H");
        vm.RefreshAllMarks();   // no pull ever ran: the inbound store is empty

        ReconciliationRow row = ((TargetGroupRow)vm.Rows[0]).Children[0];
        Assert.Equal(SyncMarks.Out, row.MarkGlyph);   // outbound unaffected; nothing shows ←
    }
}
