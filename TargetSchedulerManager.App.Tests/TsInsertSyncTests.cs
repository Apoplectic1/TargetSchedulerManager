using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The insert kind through the sync model: journal round-trip, the gate's key-space rule (target guid, plan
// minted id), the push insert leg (targets before plans, fold-in of field edits on unpushed inserts, retained
// failures), and the review's creates section.
public class TsInsertSyncTests
{
    private const string PlanPayload =
        /*lang=json*/ """{"guid":"ep-g","targetid":"t-g","exposureTemplateId":"et-g","enabled":1,"desired":42,"acquired":42,"accepted":42,"exposure":-1}""";
    private const string TargetPayload =
        /*lang=json*/ """{"guid":"t-g","projectid":"p-g","name":"Sh2-119","active":1,"ra":1.5,"dec":44.1}""";

    // ---- journal ------------------------------------------------------------------------------------------

    [Fact]
    public void AppendInsert_MakesDirty_SurvivesRelaunch()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");
        Assert.True(sync.IsDirty);
        Assert.Equal(1, sync.Journal.CollapsedCount);

        TsSync relaunched = new(sync.RemotePath, sync.LocalPath,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        TsJournalEntry entry = Assert.Single(relaunched.Journal.Entries);
        Assert.Equal(TsEditKind.Insert, entry.Kind);
        Assert.Equal(TsJournal.InsertColumn, entry.Column);
        Assert.Equal("ep-g", entry.RowGuid);
        Assert.Equal("900", entry.Key);
        Assert.Equal(PlanPayload, entry.Value);
        Assert.Null(entry.Old);
    }

    [Fact]
    public async Task ApplyInsertAsync_JournalsEachTablesOwnKeySpace()
    {
        // Target rows journal under their guid (the target key space); plans under the freshly minted local
        // integer id (the plan key space) — so marks and later field edits resolve with no special cases.
        TsSync sync = SyncTestEnv.NewSync(out _);
        RecordingEditor editor = new();   // mints row ids from 700
        TsEditGate gate = new(sync, _ => editor);

        EditOutcome outcome = await gate.ApplyInsertAsync(
        [
            new TsRowInsert(TsTable.Target, new Dictionary<string, object?>
            { ["guid"] = "t-g", ["projectid"] = "p-g", ["name"] = "Sh2-119", ["active"] = 1 }),
            new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-g", ["targetid"] = "t-g", ["exposureTemplateId"] = "et-g", ["desired"] = 42 }),
        ], "Sh2-119 · O");

        Assert.IsType<EditOutcome.Applied>(outcome);
        IReadOnlyList<TsJournalEntry> entries = sync.Journal.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal("t-g", entries[0].Key);      // target: guid
        Assert.Equal("701", entries[1].Key);      // plan: the minted local id (700 went to the target row)
        Assert.Equal("ep-g", entries[1].RowGuid);
    }

    [Fact]
    public async Task ApplyInsertAsync_Refusal_JournalsNothing()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        RecordingEditor editor = new() { RefuseAll = RefusalReason.HasOverrideOrder };
        TsEditGate gate = new(sync, _ => editor);

        EditOutcome outcome = await gate.ApplyInsertAsync(
            [new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-g", ["targetid"] = 1L, ["exposureTemplateId"] = 2L })], "A · H");

        EditOutcome.Refused refused = Assert.IsType<EditOutcome.Refused>(outcome);
        Assert.Equal(RefusalReason.HasOverrideOrder, refused.Reason);
        Assert.False(sync.IsDirty);
    }

    // ---- push insert leg ----------------------------------------------------------------------------------

    private static TsSync NewPushSync(RecordingEditor editor, StubWriteBackApplier applier)
    {
        string dir = SyncTestEnv.NewDir();
        TsSync sync = new(
            Path.Combine(dir, "remote.sqlite"), Path.Combine(dir, "local.sqlite"),
            _ => editor, _ => applier);
        SyncTestEnv.CreateDb(sync.RemotePath, "remote");   // reachable, no sidecar
        return sync;
    }

    [Fact]
    public void Push_ReplaysTargetInsertBeforeItsPlan_RegardlessOfJournalOrder()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier());
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");
        sync.RecordInsert(TsTable.Target, "t-g", TargetPayload, "t-g", "Sh2-119");

        PushResult result = sync.Push();

        Assert.Equal(PushOutcome.Success, result.Outcome);
        Assert.Equal(2, editor.Inserts.Count);
        Assert.Equal(TsTable.Target, editor.Inserts[0].Table);
        Assert.Equal(TsTable.ExposurePlan, editor.Inserts[1].Table);
        Assert.Equal("t-g", editor.Inserts[1].Payload["targetid"]);   // FK travels as the parent guid
        Assert.False(sync.IsDirty);
    }

    [Fact]
    public void Push_FoldsFieldEditsIntoTheInsert_NoSeparateUpdate()
    {
        // A desired edit on an unpushed adopted plan journals under the local plan id; replaying it as a
        // remote UPDATE keyed by that id would miss (ids diverge). It folds into the INSERT instead.
        RecordingEditor editor = new();
        StubWriteBackApplier applier = new();
        TsSync sync = NewPushSync(editor, applier);
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");
        sync.RecordEdit(TsTable.ExposurePlan, "900", "desired", 60, "42", "Sh2-119 · O");
        sync.RecordWriteBack("900", "acquired", 45, "42", "Sh2-119 · O");

        PushResult result = sync.Push();

        Assert.Equal(PushOutcome.Success, result.Outcome);
        TsRowInsert insert = Assert.Single(editor.Inserts);
        Assert.Equal(60L, insert.Payload["desired"]);     // manual edit folded
        Assert.Equal(45L, insert.Payload["acquired"]);    // write-back stamp folded
        Assert.Empty(editor.Writes);                      // no UPDATE keyed by the stale local id
        Assert.Equal(0, applier.ApplyCalls);              // no write-back leg row survived the fold
        Assert.False(sync.IsDirty);
    }

    [Fact]
    public void Push_UnresolvedParent_RetainsInsertAndItsFoldedEdits()
    {
        RecordingEditor editor = new();
        editor.UnresolvedParentGuids.Add("et-g");         // template vanished remotely
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier());
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");
        sync.RecordEdit(TsTable.ExposurePlan, "900", "desired", 60, "42", "Sh2-119 · O");
        sync.RecordEdit(TsTable.Target, "t-other", "active", 0, "1", "M 81");   // unrelated edit still applies

        PushResult result = sync.Push();

        Assert.Equal(PushOutcome.PartialFailure, result.Outcome);
        Assert.Contains(result.Failures, f => f.Detail.Contains("exposureTemplateId"));
        IReadOnlyList<TsJournalEntry> retained = sync.Journal.Entries;
        Assert.Equal(2, retained.Count);                  // the insert + its folded edit stay journaled
        Assert.Contains(retained, e => e.Kind == TsEditKind.Insert);
        Assert.Contains(retained, e => e.Column == "desired");
        Assert.Contains(editor.Writes, w => w.Key == "t-other");   // the unrelated edit landed
    }

    [Fact]
    public void Push_StructuralRefusalOnInserts_RefusesWholePush_JournalIntact()
    {
        RecordingEditor editor = new() { RefuseAll = RefusalReason.ReadOnly };
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier());
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");

        PushResult result = sync.Push();

        Assert.Equal(PushOutcome.Refused, result.Outcome);
        Assert.True(sync.IsDirty);
        Assert.Empty(editor.Inserts);
    }

    // ---- review -------------------------------------------------------------------------------------------

    [Fact]
    public void PreparePush_PresentsCreatesDistinctly_PlumbingExcluded()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        sync.RecordInsert(TsTable.Target, "t-g", TargetPayload, "t-g", "Sh2-119");
        sync.RecordInsert(TsTable.ExposurePlan, "900", PlanPayload, "ep-g", "Sh2-119 · O");

        PushReview review = sync.PreparePush(probe: null);

        Assert.Equal(2, review.Creates.Count);
        PushReviewCreateLine target = review.Creates[0];
        Assert.Equal("Sh2-119", target.Label);
        Assert.Equal("target", target.Entity);
        Assert.Contains("name Sh2-119", target.Summary);
        Assert.DoesNotContain("guid", target.Summary);
        Assert.DoesNotContain("p-g", target.Summary);      // parent reference is plumbing, not review content
        PushReviewCreateLine plan = review.Creates[1];
        Assert.Equal("exposure plan", plan.Entity);
        Assert.Contains("desired 42", plan.Summary);
        Assert.Equal(2, review.CollapsedCount);
        Assert.Empty(review.Manual);
    }
}
