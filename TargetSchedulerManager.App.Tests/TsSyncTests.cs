using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The sync orchestrator: the pull/skip matrix over real temp files (the online-backup pull needs real SQLite
// dbs; "unreachable" is a path that does not exist — same probe semantics as a down host, minus the SMB stall),
// and the push replay through the recording editor + stub write-back applier seams.
public class TsSyncTests
{
    // ---- pull/skip matrix (task 1.3) ---------------------------------------------------------------------

    [Fact]
    public void Probe_MissingRemote_IsUnreachable()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);   // remote path never created
        Assert.Null(sync.ProbeRemote());
        Assert.True(sync.HasProbed);
        Assert.False(sync.RemoteReachable);
    }

    [Fact]
    public void FirstRun_Unbaselined_Pulls_AndRecordsBaseline()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");

        TsDbStat probe = sync.ProbeRemote()!;
        Assert.True(sync.ShouldPull(probe));                    // unbaselined-first-run cell
        Assert.True(sync.PullIfChanged(probe));
        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
        Assert.NotNull(sync.Baseline);
        Assert.Equal(probe.Length, sync.Baseline!.RemoteLength);
    }

    [Fact]
    public void UnchangedRemote_SkipsTheCopy()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);

        SyncTestEnv.CreateDb(sync.LocalPath, "local-work");     // fingerprint the local copy
        Assert.False(sync.PullIfChanged(sync.ProbeRemote()!));  // baseline matches, no sidecar → skip
        Assert.Equal("local-work", SyncTestEnv.ReadMarker(sync.LocalPath));   // untouched — no copy ran
    }

    [Fact]
    public void ChangedRemote_Pulls_AndReRecordsBaseline()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);
        TsBaseline first = sync.Baseline!;

        SyncTestEnv.CreateDb(sync.RemotePath, "night-2");       // remote content + mtime change
        File.SetLastWriteTimeUtc(sync.RemotePath, first.RemoteLastWriteUtc.AddMinutes(5));

        TsDbStat probe = sync.ProbeRemote()!;
        Assert.True(sync.ShouldPull(probe));
        Assert.True(sync.PullIfChanged(probe));
        Assert.Equal("night-2", SyncTestEnv.ReadMarker(sync.LocalPath));
        Assert.NotEqual(first.RemoteLastWriteUtc, sync.Baseline!.RemoteLastWriteUtc);
    }

    [Fact]
    public void RemoteSidecar_ForcesPull_EvenWithMatchingBaseline()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);

        File.WriteAllText(sync.RemotePath + "-wal", "");        // WAL content invisible to the main file's mtime
        TsDbStat probe = sync.ProbeRemote()!;
        Assert.True(probe.HasSidecar);
        Assert.True(sync.ShouldPull(probe));
    }

    [Fact]
    public void Baseline_SurvivesRelaunch()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);

        TsSync relaunched = new(sync.RemotePath, sync.LocalPath,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        Assert.NotNull(relaunched.Baseline);
        Assert.False(relaunched.ShouldPull(relaunched.ProbeRemote()!));   // skip on relaunch — the test-loop case
    }

    // ---- dirty state --------------------------------------------------------------------------------------

    [Fact]
    public void RecordEdit_MakesDirty_DiscardClears_BothSurviveReload()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        Assert.False(sync.IsDirty);

        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        Assert.True(sync.IsDirty);

        TsSync relaunched = new(sync.RemotePath, sync.LocalPath,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        Assert.True(relaunched.IsDirty);                        // crash-safe: dirty is the journal itself

        relaunched.Discard();
        Assert.False(relaunched.IsDirty);
        Assert.False(new TsSync(sync.RemotePath, sync.LocalPath,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()).IsDirty);
    }

    // ---- push (tasks 4.1–4.3) -----------------------------------------------------------------------------

    private static TsSync NewPushSync(RecordingEditor editor, StubWriteBackApplier applier, out string dir)
    {
        string d = SyncTestEnv.NewDir();
        dir = d;
        return new TsSync(
            Path.Combine(d, "remote.sqlite"), Path.Combine(d, "local.sqlite"),
            _ => editor, _ => applier);
    }

    [Fact]
    public void Push_EmptyJournal_IsNothingToPush()
    {
        TsSync sync = NewPushSync(new RecordingEditor(), new StubWriteBackApplier(), out _);
        Assert.Equal(PushOutcome.NothingToPush, sync.Push().Outcome);
    }

    [Fact]
    public void Push_RemoteUnreachable_RefusesWholePush_JournalIntact()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);   // remote never created
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.Unreachable, result.Outcome);
        Assert.Empty(editor.Writes);
        Assert.True(sync.IsDirty);
    }

    [Fact]
    public void Push_RemoteSidecar_RefusesWholePush_NothingWritten()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        File.WriteAllText(sync.RemotePath + "-journal", "");    // NINA holds the db
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.RefusedBusy, result.Outcome);
        Assert.Empty(editor.Writes);
        Assert.True(sync.IsDirty);
    }

    [Fact]
    public void Push_ReplaysOnlyCollapsedFields_ClearsJournal_PullsFresh()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 20, "10", "A · H");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");   // collapses onto the first
        sync.RecordEdit(TsTable.Target, "g-1", "active", 0, "1", "B");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.Success, result.Outcome);
        Assert.Equal(2, result.AppliedCount);
        Assert.Equal(2, editor.Writes.Count);                   // exactly the collapsed fields, nothing else
        Assert.Contains(editor.Writes, w => w is { Key: "ep-1", Column: "desired", Value: 25L });
        Assert.Contains(editor.Writes, w => w is { Key: "g-1", Column: "active", Value: 0L });

        Assert.False(sync.IsDirty);                             // journal cleared
        Assert.True(result.PulledFresh);                        // ...and the closing pull re-baselined
        Assert.NotNull(sync.Baseline);
        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
    }

    // ---- truthful outcome (openspec truthful-outcome) -----------------------------------------------------

    [Fact]
    public void Push_ClosingPullFails_StillSuccess_JournalClearedAndFlagged()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        // Stats fine (the probe is a file stat) but is no SQLite db — the replay legs use the stub
        // editors and never notice; the closing pull's backup chokes on it.
        File.WriteAllText(sync.RemotePath, "not a sqlite database");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        PushResult result = sync.Push();

        Assert.Equal(PushOutcome.Success, result.Outcome);      // the push DID land — never "failed"
        Assert.True(result.ClosingPullFailed);
        Assert.False(result.PulledFresh);
        Assert.Single(editor.Writes);                           // the write applied…
        Assert.False(sync.IsDirty);                             // …and the journal cleared with it
    }

    [Fact]
    public void Push_ClosingPullCancelled_Success_NotFlaggedAsFailed()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        PushResult result = sync.Push(pullCancel: new CancellationToken(canceled: true));

        Assert.Equal(PushOutcome.Success, result.Outcome);
        Assert.False(result.PulledFresh);
        Assert.False(result.ClosingPullFailed);                 // the user cancelled — not a fault
        Assert.False(sync.IsDirty);
    }

    [Fact]
    public void Push_ReplayLegThrows_EscapesWithJournalIntact()
    {
        // Pins the PushAsync catch's premise: every throw that ESCAPES Push precedes the journal rewrite.
        string dir = SyncTestEnv.NewDir();
        TsSync sync = new(
            Path.Combine(dir, "remote.sqlite"), Path.Combine(dir, "local.sqlite"),
            _ => throw new IOException("editor open fault"), _ => new StubWriteBackApplier());
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        Assert.Throws<IOException>(() => sync.Push());
        Assert.True(sync.IsDirty);                              // every entry still journaled — re-push recovers
        Assert.Single(sync.Journal.Entries);
    }

    [Fact]
    public void Discard_ClearsJournalOnly_BaselineStays()
    {
        TsSync sync = NewPushSync(new RecordingEditor(), new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        sync.Pull(sync.ProbeRemote()!);                         // the discarding pull lands first…

        sync.Discard();                                         // …then Discard is bookkeeping only

        Assert.False(sync.IsDirty);
        Assert.NotNull(sync.Baseline);                          // pull-first keeps the fresh baseline
        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
    }

    [Fact]
    public void Push_RowMissing_RetainsThatEntry_AppliesTheRest()
    {
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        editor.MissingKeys.Add("ep-gone");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-gone", "desired", 25, "20", "A · H");
        sync.RecordEdit(TsTable.Target, "g-1", "active", 1, "0", "B");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.PartialFailure, result.Outcome);
        Assert.Equal(1, result.AppliedCount);
        PushFailure failure = Assert.Single(result.Failures);
        Assert.Equal("A · H", failure.Label);
        Assert.True(sync.IsDirty);                              // the failed entry is retained
        Assert.Equal("ep-gone", Assert.Single(sync.Journal.Entries).Key);
        Assert.Null(sync.Baseline);                             // no closing pull on partial failure

        // The retained entry re-pushes once the cause clears.
        editor.MissingKeys.Clear();
        Assert.Equal(PushOutcome.Success, sync.Push().Outcome);
        Assert.False(sync.IsDirty);
    }

    [Fact]
    public void Push_WriteBackEntries_RouteThroughTheWriter_NotTheFieldEditor()
    {
        RecordingEditor editor = new();
        StubWriteBackApplier applier = new();
        applier.Rows[500] = (40, 40, 34);
        TsSync sync = NewPushSync(editor, applier, out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordWriteBack("500", "acquired", 42, "40", "A · H @300s");
        sync.RecordWriteBack("500", "accepted", 42, "40", "A · H @300s");
        sync.RecordWriteBack("500", "desired", 42, "34", "A · H @300s");
        sync.RecordEdit(TsTable.Target, "g-1", "active", 1, "0", "B");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.Success, result.Outcome);
        Assert.Equal((42, 42, 42), applier.Rows[500]);          // writer leg applied the stamp
        (TsTable, string, string, object?) fieldWrite = Assert.Single(editor.Writes);
        Assert.Equal("g-1", fieldWrite.Item2);                  // field leg saw only the manual edit
        Assert.False(sync.IsDirty);
    }

    [Fact]
    public void Push_WriteBackRowGone_RetainsThatPlansEntries_AppliesTheRest()
    {
        RecordingEditor editor = new();
        StubWriteBackApplier applier = new();                   // no rows — plan 500 is gone remotely
        TsSync sync = NewPushSync(editor, applier, out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordWriteBack("500", "acquired", 42, "40", "A · H @300s");
        sync.RecordWriteBack("500", "accepted", 42, "40", "A · H @300s");
        sync.RecordEdit(TsTable.Target, "g-1", "active", 1, "0", "B");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.PartialFailure, result.Outcome);
        Assert.Equal(1, result.AppliedCount);                   // the manual edit
        Assert.Equal(2, sync.Journal.Count);                    // both stamp entries retained
        Assert.All(sync.Journal.Entries, e => Assert.Equal("500", e.Key));
    }

    // ---- review shaping (tasks 4.2 / 6.2 data) ------------------------------------------------------------

    [Fact]
    public void PreparePush_SplitsKinds_DecreasesFirst_FlagsStaleness()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);

        sync.RecordWriteBack("500", "acquired", 42, "40", "M 81 · Ha @300s");     // increase
        sync.RecordWriteBack("501", "acquired", 0, "31", "M 82 · O3 @300s");      // decrease — must list first
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "M 81 · Ha");

        // Remote changes after the pull → staleness flag.
        SyncTestEnv.CreateDb(sync.RemotePath, "night-2");
        File.SetLastWriteTimeUtc(sync.RemotePath, sync.Baseline!.RemoteLastWriteUtc.AddMinutes(9));

        PushReview review = sync.PreparePush(sync.ProbeRemote());
        Assert.Equal(3, review.CollapsedCount);
        Assert.True(review.RemoteChangedSinceBaseline);
        Assert.False(review.RemoteBusy);

        Assert.Equal(2, review.WriteBack.Count);
        Assert.True(review.WriteBack[0].IsDecrease);            // decreases first
        Assert.Equal("M 82 · O3 @300s", review.WriteBack[0].Label);
        Assert.Equal(0L, review.WriteBack[0].NewCount);
        Assert.Equal("31", review.WriteBack[0].OldCount);

        PushReviewFieldLine manual = Assert.Single(review.Manual);
        Assert.Equal("desired", manual.Column);
        Assert.Equal("20", manual.Old);
        Assert.Equal("25", manual.New);
    }

    [Fact]
    public void Push_CadenceBreakingField_RoutesThroughTheFieldEditor()
    {
        // The transactional cadence clear lives INSIDE the library's TrySetField, so the replay inherits it
        // with no push changes — this pins that a journaled cadence-breaking field reaches that call.
        RecordingEditor editor = new();
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "enabled", 0, "1", "A · H");

        Assert.Equal(PushOutcome.Success, sync.Push().Outcome);
        Assert.Contains(editor.Writes, w => w is { Key: "ep-1", Column: "enabled", Value: 0L });
    }

    [Fact]
    public void Push_OverrideOrderRefusalAtReplay_RetainsTheEntry()
    {
        RecordingEditor editor = new() { RefuseAll = RefusalReason.HasOverrideOrder };
        TsSync sync = NewPushSync(editor, new StubWriteBackApplier(), out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "enabled", 0, "1", "A · H");

        PushResult result = sync.Push();
        Assert.Equal(PushOutcome.PartialFailure, result.Outcome);   // loud, entry retained for a later push
        Assert.True(sync.IsDirty);
        Assert.Contains("HasOverrideOrder", Assert.Single(result.Failures).Detail);
    }

    [Fact]
    public void PreparePush_DesiredOnlyRaise_ShowsNoPhantomCountChange()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        // Counts already matched disk; only the desired ratchet was journaled.
        sync.RecordWriteBack("500", "desired", 42, "34", "M 81 · Ha @300s");

        PushReviewCountLine line = Assert.Single(sync.PreparePush(null).WriteBack);
        Assert.Null(line.NewCount);                             // no count pair — nothing to assert about TS counts
        Assert.Null(line.OldCount);
        Assert.False(line.IsDecrease);
        Assert.Equal("34", line.OldDesired);
        Assert.Equal(42L, line.NewDesired);
    }
}
