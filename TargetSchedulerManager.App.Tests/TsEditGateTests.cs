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
