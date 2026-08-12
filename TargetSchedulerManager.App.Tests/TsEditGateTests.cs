using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The guarded write in isolation: a stub ITsEditor (no SQLite) over a temp-path TsSync. The gate always
// targets the local db and journals every verified write; refusals/failures leave the journal untouched.
public class TsEditGateTests
{
    private sealed class StubEditor : ITsEditor
    {
        public (FieldEditResult? Result, RefusalReason Refusal) Next = (null, RefusalReason.None);
        public bool Throw;
        public Dictionary<string, object?> Row = new(StringComparer.OrdinalIgnoreCase);   // read-seed source
        public HashSet<string> AbsentColumns = new(StringComparer.OrdinalIgnoreCase);     // simulated schema drift
        public bool RowFound = true;
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) =>
            Throw ? throw new InvalidOperationException("boom") : Next;
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) =>
            Throw ? throw new InvalidOperationException("boom")
                  : RowFound ? (true, Row.TryGetValue(column, out object? v) ? v : null) : (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => !AbsentColumns.Contains(column);
        public (bool Found, double? Value) EffectiveExposure = (false, null);
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) =>
            Throw ? throw new InvalidOperationException("boom") : EffectiveExposure;
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);   // no test drives inserts through this stub
        public void Dispose() { }
    }

    private static TsEditGate Gate(StubEditor editor, out TsSync sync)
    {
        sync = SyncTestEnv.NewSync(out _);
        return new TsEditGate(sync, _ => editor);
    }

    [Fact]
    public async Task CleanWrite_ReturnsApplied_AndJournals()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: true), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        EditOutcome.Applied a = Assert.IsType<EditOutcome.Applied>(o);
        Assert.Equal("5", a.Old);
        Assert.Equal(10, a.New);

        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);   // the verified write journaled for push
        Assert.Equal(TsEditKind.Manual, entry.Kind);
        Assert.Equal(TsTable.ExposurePlan, entry.Table);
        Assert.Equal("ep-1", entry.Key);
        Assert.Equal("desired", entry.Column);
        Assert.Equal(10L, entry.Value);   // journal canonicalizes integer boxes to long
        Assert.Equal("5", entry.Old);
        Assert.Equal("A · H", entry.Label);
    }

    [Fact]
    public async Task TargetRename_JournalsLikeAnyIntentEdit()
    {
        // The rename verb (add-target-rename): target.name flows through the same gate → journal →
        // push-replay pipeline as every other Manual edit; the label carries the identity for review.
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "Cygnus Loop P9", Verified: true), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        EditOutcome o = await gate.ApplyAsync(TsTable.Target, "tg-9", "name", "CygnusLoop P9", "Cygnus Loop P9");
        Assert.IsType<EditOutcome.Applied>(o);

        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);
        Assert.Equal(TsEditKind.Manual, entry.Kind);
        Assert.Equal(TsTable.Target, entry.Table);
        Assert.Equal("tg-9", entry.Key);
        Assert.Equal("name", entry.Column);
        Assert.Equal("CygnusLoop P9", entry.Value);
        Assert.Equal("Cygnus Loop P9", entry.Old);
    }

    [Fact]
    public async Task ProjectFieldWrite_JournalsWithTheProjectKey()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "45", Verified: true), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        Assert.IsType<EditOutcome.Applied>(
            await gate.ApplyAsync(TsTable.Project, "prj-1", "minimumtime", 90, "Nebulae — project"));

        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);   // project edits ride the same push
        Assert.Equal(TsEditKind.Manual, entry.Kind);
        Assert.Equal(TsTable.Project, entry.Table);
        Assert.Equal("prj-1", entry.Key);
        Assert.Equal("minimumtime", entry.Column);
        Assert.Equal(90L, entry.Value);
    }

    [Fact]
    public async Task TemplateFieldWrite_JournalsWithTheTemplateKeyAndLabel()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "0", Verified: true), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        Assert.IsType<EditOutcome.Applied>(await gate.ApplyAsync(
            TsTable.ExposureTemplate, "11", "moonavoidanceenabled", 1, "Template 'Ha 300' — used by 12 plan(s)"));

        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);
        Assert.Equal(TsTable.ExposureTemplate, entry.Table);
        Assert.Equal("11", entry.Key);
        Assert.Equal("Template 'Ha 300' — used by 12 plan(s)", entry.Label);   // the push review states the scope
    }

    [Fact]
    public async Task RefusedWrite_PassesTheReasonThrough_NoJournalEntry()
    {
        StubEditor ed = new() { Next = (null, RefusalReason.OpenSidecar) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        Assert.Equal(RefusalReason.OpenSidecar, Assert.IsType<EditOutcome.Refused>(o).Reason);
        Assert.True(sync.Journal.IsEmpty);
    }

    [Fact]
    public async Task VerifyFails_ReturnsFailed_NoJournalEntry()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: false), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);
        EditOutcome.Failed f = Assert.IsType<EditOutcome.Failed>(
            await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H"));
        Assert.True(f.Found);
        Assert.False(f.Verified);
        Assert.True(sync.Journal.IsEmpty);
    }

    [Fact]
    public async Task EditorThrows_ReturnsFailed_NoJournalEntry()
    {
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = Gate(ed, out TsSync sync);
        Assert.IsType<EditOutcome.Failed>(await gate.ApplyAsync(TsTable.Target, "g-1", "active", 1, "A"));
        Assert.True(sync.Journal.IsEmpty);
    }

    // ---- the batch path -------------------------------------------------------------------------------

    // Per-call scripted editor: each TrySetField consumes the next step (a result, or a throw).
    private sealed class ScriptedEditor : ITsEditor
    {
        public readonly Queue<Func<(FieldEditResult? Result, RefusalReason Refusal)>> Script = new();
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) => Script.Dequeue()();
        public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
        public bool IsFieldAvailable(TsTable table, string column) => true;
        public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);
        public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows) =>
            (null, RefusalReason.SchemaIncompatible);   // no test drives inserts through this stub
        public void Dispose() { }
    }

    private static TsFieldEdit Edit(string key, string column = "desired", object? value = null) =>
        new(TsTable.ExposurePlan, key, column, value ?? 10, $"T · {key}");

    [Fact]
    public async Task ApplyMany_OneEditorSession_OutcomesAlignByIndex()
    {
        ScriptedEditor ed = new();
        ed.Script.Enqueue(() => (new FieldEditResult(RowFound: true, OldValue: "5", Verified: true), RefusalReason.None));
        ed.Script.Enqueue(() => (null, RefusalReason.OpenSidecar));
        ed.Script.Enqueue(() => (new FieldEditResult(RowFound: true, OldValue: "7", Verified: false), RefusalReason.None));

        int opens = 0;
        TsSync sync = SyncTestEnv.NewSync(out _);
        TsEditGate gate = new(sync, _ => { opens++; return ed; });

        IReadOnlyList<EditOutcome> outcomes =
            await gate.ApplyManyAsync([Edit("ep-1"), Edit("ep-2"), Edit("ep-3")]);

        Assert.Equal(1, opens);                                   // ONE session serves the whole batch
        Assert.IsType<EditOutcome.Applied>(outcomes[0]);
        Assert.IsType<EditOutcome.Refused>(outcomes[1]);
        Assert.IsType<EditOutcome.Failed>(outcomes[2]);
        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);   // only the applied edit journals
        Assert.Equal("ep-1", entry.Key);
    }

    [Fact]
    public async Task ApplyMany_ThrowingEdit_FailsOnlyItself()
    {
        ScriptedEditor ed = new();
        ed.Script.Enqueue(() => (new FieldEditResult(RowFound: true, OldValue: "1", Verified: true), RefusalReason.None));
        ed.Script.Enqueue(() => throw new InvalidOperationException("boom"));
        ed.Script.Enqueue(() => (new FieldEditResult(RowFound: true, OldValue: "3", Verified: true), RefusalReason.None));

        TsSync sync = SyncTestEnv.NewSync(out _);
        TsEditGate gate = new(sync, _ => ed);

        IReadOnlyList<EditOutcome> outcomes =
            await gate.ApplyManyAsync([Edit("ep-1"), Edit("ep-2"), Edit("ep-3")]);

        Assert.IsType<EditOutcome.Applied>(outcomes[0]);
        Assert.IsType<EditOutcome.Failed>(outcomes[1]);           // the throw fails only its own edit
        Assert.IsType<EditOutcome.Applied>(outcomes[2]);
        Assert.Equal(2, sync.Journal.Entries.Count);
    }

    [Fact]
    public async Task ApplyMany_EditorCannotOpen_FailsEveryEdit_NothingJournaled()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        TsEditGate gate = new(sync, _ => throw new InvalidOperationException("no db"));

        IReadOnlyList<EditOutcome> outcomes = await gate.ApplyManyAsync([Edit("ep-1"), Edit("ep-2")]);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.IsType<EditOutcome.Failed>(o));
        Assert.True(sync.Journal.IsEmpty);
    }

    [Fact]
    public async Task ApplyMany_OneElement_MatchesApplyAsync()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: true), RefusalReason.None) };
        TsEditGate gate = Gate(ed, out TsSync sync);

        IReadOnlyList<EditOutcome> outcomes = await gate.ApplyManyAsync(
            [new TsFieldEdit(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H")]);

        EditOutcome.Applied a = Assert.IsType<EditOutcome.Applied>(Assert.Single(outcomes));
        Assert.Equal("5", a.Old);
        Assert.Equal(10, a.New);
        TsJournalEntry entry = Assert.Single(sync.Journal.Entries);   // same journal shape as ApplyAsync
        Assert.Equal(10L, entry.Value);
    }

    [Fact]
    public async Task ReadFields_ReturnsEveryEditableColumnFromTheDb()
    {
        StubEditor ed = new()
        {
            Row = new(StringComparer.OrdinalIgnoreCase)
            { ["active"] = 1L, ["priority"] = -1L, ["rotation"] = 12.5 },
        };
        TsEditGate gate = Gate(ed, out _);
        IReadOnlyDictionary<string, object?>? seed = await gate.ReadFieldsAsync(TsTable.Target, "g-1", "A");
        Assert.NotNull(seed);
        Assert.Equal(TsEditableSchema.For(TsTable.Target).Count, seed.Count);
        Assert.Equal(12.5, seed["rotation"]);
        Assert.Equal(-1L, seed["priority"]);
    }

    [Fact]
    public async Task ReadFields_SkipsColumnsAbsentOnThisDb()
    {
        StubEditor ed = new() { AbsentColumns = ["rotation"] };
        TsEditGate gate = Gate(ed, out _);
        IReadOnlyDictionary<string, object?>? seed = await gate.ReadFieldsAsync(TsTable.Target, "g-1", "A");
        Assert.NotNull(seed);
        Assert.False(seed.ContainsKey("rotation"));
        Assert.Equal(TsEditableSchema.For(TsTable.Target).Count - 1, seed.Count);
    }

    [Fact]
    public async Task ReadFields_RowMissing_ReturnsNull()
    {
        StubEditor ed = new() { RowFound = false };
        TsEditGate gate = Gate(ed, out _);
        Assert.Null(await gate.ReadFieldsAsync(TsTable.Target, "no-such", "A"));
    }

    [Fact]
    public async Task ReadFields_EditorThrows_ReturnsNull()
    {
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = Gate(ed, out _);
        Assert.Null(await gate.ReadFieldsAsync(TsTable.ExposurePlan, "ep-1", "A · H"));
    }
}
