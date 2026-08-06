using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using TargetSchedulerManager.App.ViewModels.Rows;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The busy exclusion (openspec busy-exclusion): bulk operations are mutually exclusive, row edits are
// refused while one runs, and an in-flight edit blocks a bulk operation from starting. The gate helpers
// are exercised directly (internal seam) plus through the visible-tonight pass — the one bulk operation
// testable without a disk scan.
public class MainViewModelBusyGateTests
{
    // ---- the gate primitives --------------------------------------------------------------------------

    [Fact]
    public void TryBeginBusy_CheckAndSet_SecondRefused_EndBusyRecovers()
    {
        MainViewModel vm = new(Gate(new StubEditor()));

        Assert.True(vm.CanEdit);
        Assert.True(vm.TryBeginBusy());
        Assert.True(vm.IsLoading);
        Assert.False(vm.CanEdit);
        Assert.False(vm.TryBeginBusy());          // held — a second bulk operation is refused

        vm.EndBusy();
        Assert.False(vm.IsLoading);
        Assert.True(vm.CanEdit);
        Assert.True(vm.TryBeginBusy());           // released — the next operation acquires normally
        vm.EndBusy();
    }

    // ---- row edits refused while busy -----------------------------------------------------------------

    [Fact]
    public async Task EverySetter_RefusesUnderBusy_NothingReachesTheGate()
    {
        int opens = 0;
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "1", Verified: true), RefusalReason.None) };
        TsSync sync = SyncTestEnv.NewSync(out _);
        MainViewModel vm = new(new TsEditGate(sync, _ => { opens++; return ed; }));

        ReconciliationRow leaf1 = Make.Leaf(target: "M 101", desired: 10, planTsKey: "ep-1",
            tsTargetKey: "p1", enabled: true, planEnabled: true);
        ReconciliationRow leaf2 = Make.Leaf(target: "M 101", filter: "O", desired: 10, tsTargetKey: "p2", enabled: false);
        PanelGroupRow panel1 = new("M 101", "P1", "Panel 01", Models.RowSource.Both, [leaf1], false);
        PanelGroupRow panel2 = new("M 101", "P2", "Panel 02", Models.RowSource.Both, [leaf2], false);
        TargetGroupRow group = new("M 101", [leaf1, leaf2], false, true, panels: [panel1, panel2]);

        Assert.True(vm.TryBeginBusy());

        Assert.False(await vm.SetTargetEnabledAsync(group, false));
        Assert.False(await vm.SetMosaicEnabledAsync(group, false));
        Assert.False(await vm.SetPlanEnabledAsync(leaf1, false));
        Assert.False(await vm.SetPlanDesiredAsync(leaf1, 25));
        Assert.False(await vm.SetPlanExposureAsync(leaf1, 301, mirrorSeconds: 301));
        Assert.False(await vm.SetTsFieldAsync(TsTable.Target, "p1", "priority", 2, "M 101"));

        Assert.Equal(0, opens);                   // the funnel refused before any editor opened
        Assert.True(sync.Journal.IsEmpty);        // nothing journaled
        Assert.Contains("busy", vm.StatusText);   // and it said why

        vm.EndBusy();
    }

    // ---- an in-flight edit blocks a bulk operation ----------------------------------------------------

    [Fact]
    public async Task EditInFlight_BlocksTryBeginBusy_UntilItsWorkerCompletes()
    {
        using ManualResetEventSlim entered = new(), release = new();
        BlockingEditor ed = new(entered, release);
        TsSync sync = SyncTestEnv.NewSync(out _);
        MainViewModel vm = new(new TsEditGate(sync, _ => ed));
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planTsKey: "ep-1");
        vm.SetRowsForTest([row]);

        Task<bool> edit = vm.SetPlanDesiredAsync(row, 25);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));   // the edit's worker holds the editor now

        Assert.False(vm.TryBeginBusy());                       // refused while the edit is in flight
        Assert.Contains("edit is still applying", vm.StatusText);

        release.Set();
        Assert.True(await edit);

        Assert.True(vm.TryBeginBusy());                        // the window closed with the edit
        vm.EndBusy();
    }

    // ---- visible-tonight under the gate ---------------------------------------------------------------

    [Fact]
    public async Task VisibleTonight_HoldsTheExclusion_WhileTheBatchApplies()
    {
        using ManualResetEventSlim entered = new(), release = new();
        int opens = 0;
        BlockingEditor ed = new(entered, release);
        TsSync sync = SyncTestEnv.NewSync(out _);
        MainViewModel vm = new(new TsEditGate(sync, _ => { opens++; return ed; }));
        vm.SetLoadForTest(Load(NeverRisesInactiveTarget()));   // deterministic zero-flip pass
        ReconciliationRow row = Make.Leaf(target: "A", desired: 10, planTsKey: "ep-1");

        Task pass = vm.RunVisibleTonightAsync(TimeSpan.FromMinutes(30), floorAltitudeDeg: 0);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));    // the batch's editor session is open

        Assert.True(vm.IsLoading);                             // the pass holds the exclusion…
        Assert.False(vm.CanEdit);
        Assert.False(await vm.SetPlanDesiredAsync(row, 25));   // …so row edits are refused…

        Task second = vm.RunVisibleTonightAsync(TimeSpan.FromMinutes(30), floorAltitudeDeg: 0);
        Assert.True(second.IsCompleted);                       // …and a second pass is refused outright
        Assert.Equal(1, opens);                                // no second editor session

        release.Set();
        await pass;

        Assert.False(vm.IsLoading);                            // released on completion
        Assert.True(sync.Journal.IsEmpty);                     // zero flips journaled (nothing double-applied)
        Assert.Contains("Visible tonight:", vm.StatusText);    // the summary still lands
    }

    [Fact]
    public async Task VisibleTonight_RefusedTargetFlip_DoesNotOrphanAProjectFlip()
    {
        // Inactive project + disabled circumpolar target (dec +89 — always visible at the DevDefaults
        // latitude): the pass wants the target enabled, and had that landed, the project would flip
        // Active. Every write is refused — the project derivation must see the target still disabled
        // and journal NO project flip (and with nothing landed, no closing reload/disk scan runs).
        StubEditor ed = new() { Next = (null, RefusalReason.ReadOnly) };
        TsSync sync = SyncTestEnv.NewSync(out _);
        MainViewModel vm = new(new TsEditGate(sync, _ => ed));
        vm.SetLoadForTest(Load(CircumpolarDisabledTargetInInactiveProject()));

        await vm.RunVisibleTonightAsync(TimeSpan.FromMinutes(30), floorAltitudeDeg: 0);

        Assert.True(sync.Journal.IsEmpty);                        // no orphaned project.state edit
        Assert.Contains("1 FAILED", vm.StatusText);               // the refused target flip is reported
        Assert.Contains("0 project(s) flipped", vm.StatusText);   // actual (applied) project count
        Assert.False(vm.IsLoading);                               // released; no reload took the gate
    }

    [Fact]
    public async Task ScopedPress_AttemptsOnlyChangedConstraints_AndOnlyTheSelectedProject()
    {
        // Scope carries a changed minimumtime and an UNCHANGED (null) minimumaltitude; project 2 also
        // "deserves" flips. Every write is refused so nothing lands (and the closing reload — a real
        // disk scan — never runs); the recording still proves what the press ATTEMPTED: the one
        // constraint field, then enables scoped to project 1 only. Null scope members write nothing —
        // the VM half of only-if-changed (the box-vs-fill compare itself lives in the view).
        RecordingEditor ed = new();
        TsSync sync = SyncTestEnv.NewSync(out _);
        MainViewModel vm = new(new TsEditGate(sync, _ => ed));
        vm.SetLoadForTest(Load(new TsPlanData(
            [new TsProject(1, "profile", "P1", 1 /* Active */, Priority: 1, null, IsMosaic: 0, null),
             new TsProject(2, "profile", "P2", 1 /* Active */, Priority: 1, null, IsMosaic: 0, null)],
            [new TsTarget(10, "T10", 1, 5.0, -80.0, EpochCode: 2, Rotation: null, Roi: null, 1, Priority: 1, null),
             new TsTarget(20, "T20", 1, 5.0, -80.0, EpochCode: 2, Rotation: null, Roi: null, 2, Priority: 1, null)],
            [], [])));

        await vm.RunVisibleTonightAsync(
            TimeSpan.FromMinutes(30), floorAltitudeDeg: 0,
            new MainViewModel.TonightScope(1, "1", "P1", NewMinimumTime: 90, NewMinimumAltitude: null));

        (TsTable Table, string Key, string Column) constraint = Assert.Single(
            ed.Calls, c => c.Column is "minimumtime" or "minimumaltitude");
        Assert.Equal((TsTable.Project, "1", "minimumtime"), constraint);   // the unchanged altitude never travels
        (TsTable Table, string Key, string Column) enable = Assert.Single(ed.Calls, c => c.Column == "active");
        Assert.Equal("10", enable.Key);                                    // project 2's target untouched
        Assert.DoesNotContain(ed.Calls, c => c.Column == "name");          // no altitude landed ⇒ no rename attempt
        Assert.True(sync.Journal.IsEmpty);                                 // refused everywhere — nothing landed
        Assert.Contains("[P1]", vm.StatusText);
    }

    [Fact]
    public async Task AllProjectsPress_NeverAttemptsAConstraintWrite()
    {
        RecordingEditor ed = new();
        MainViewModel vm = new(Gate(ed));
        vm.SetLoadForTest(Load(CircumpolarDisabledTargetInInactiveProject()));

        await vm.RunVisibleTonightAsync(TimeSpan.FromMinutes(30), floorAltitudeDeg: 0);

        Assert.DoesNotContain(ed.Calls, c => c.Column is "minimumtime" or "minimumaltitude");
        Assert.Contains(ed.Calls, c => c.Column == "active");   // the pass itself still planned enables
    }

    [Fact]
    public async Task VisibleTonight_NoLoad_ReleasesTheGate()
    {
        MainViewModel vm = new(Gate(new StubEditor()));

        await vm.RunVisibleTonightAsync(TimeSpan.FromMinutes(30), floorAltitudeDeg: 0);

        Assert.Equal("no load yet — nothing to reconcile", vm.StatusText);
        Assert.False(vm.IsLoading);               // the early return released the exclusion
        Assert.True(vm.TryBeginBusy());
        vm.EndBusy();
    }

    // ---- builders -------------------------------------------------------------------------------------

    private static TsEditGate Gate(ITsEditor editor) => new(SyncTestEnv.NewSync(out _), _ => editor);

    // At the DevDefaults site (~40.3°N) dec −80° never rises; target already inactive AND its project
    // already Inactive ⇒ the pass plans zero edits (an Active project would plan a state flip), so its
    // closing reload (a real disk scan) never runs — the batch window is still exercised because
    // ApplyManyAsync opens its editor session unconditionally.
    private static TsPlanData NeverRisesInactiveTarget() => new(
        [new TsProject(1, "profile", "P1", 2 /* Inactive */, Priority: 1, null, IsMosaic: 0, null)],
        [new TsTarget(10, "T10", 0 /* inactive */, 5.0, -80.0, EpochCode: 2, Rotation: null,
            Roi: null, 1, Priority: 1, null)],
        [], []);

    // Circumpolar (dec +89) disabled target in an Inactive project: the pass plans an enable + a
    // dependent project Active flip — the orphan-derivation fixture.
    private static TsPlanData CircumpolarDisabledTargetInInactiveProject() => new(
        [new TsProject(1, "profile", "P1", 2 /* Inactive */, Priority: 1, null, IsMosaic: 0, null)],
        [new TsTarget(10, "T10", 0 /* inactive */, 5.0, 89.0, EpochCode: 2, Rotation: null,
            Roi: null, 1, Priority: 1, null)],
        [], []);

    private static LoadResult Load(TsPlanData ts) => new(
        [], Report(), new CatalogGraph([], [], [], [], [], []), ts, TimeSpan.Zero,
        new Dictionary<string, string>());

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);

    /// <summary>Records every attempted field write, refusing them all — orchestration assertions
    /// without any landing (so the closing reload's real disk scan never runs in a test).</summary>
    private sealed class RecordingEditor : ITsEditor
    {
        public List<(TsTable Table, string Key, string Column)> Calls { get; } = [];
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value)
        {
            Calls.Add((table, tsKey, column));
            return (null, RefusalReason.ReadOnly);
        }
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => true;
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);
        public void Dispose() { }
    }

    private sealed class StubEditor : ITsEditor
    {
        public (FieldEditResult? Result, RefusalReason Refusal) Next = (null, RefusalReason.None);
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) => Next;
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => true;
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);   // no test drives inserts through this stub
        public void Dispose() { }
    }

    /// <summary>Signals <c>entered</c> when the worker first touches the editor, then blocks until
    /// <c>release</c> — holds the "in flight" / "batch applying" window open for assertions.</summary>
    private sealed class BlockingEditor(ManualResetEventSlim entered, ManualResetEventSlim release) : ITsEditor
    {
        private void Block() { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value)
        {
            Block();
            return (new FieldEditResult(RowFound: true, OldValue: "10", Verified: true), RefusalReason.None);
        }
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => true;
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);   // no test drives inserts through this stub
        public void Dispose() => Block();   // a zero-edit batch still opens+closes the session — hold it here too
    }
}
