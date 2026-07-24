using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The persisted session journal in isolation: append/reload round-trip (JSON-lines sidecar), the
// last-write-per-field collapse with first-write Old, retention, and crash tolerance.
public class TsJournalTests
{
    private static string NewPath() => Path.Combine(SyncTestEnv.NewDir(), "local.sqlite.tsm-edits.jsonl");

    [Fact]
    public void Append_Persist_Reload_RoundTripsValuesAndSeq()
    {
        string path = NewPath();
        TsJournal journal = new(path);
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "rotation", 12.5, "0", "A");
        journal.Append(TsEditKind.Manual, TsTable.ExposureTemplate, "t-1", "name", "Ha 300", "Ha", "tpl");
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "exposure", null, "-1", "A · H");

        TsJournal reloaded = new(path);
        Assert.Equal(4, reloaded.Count);
        Assert.Equal(25L, reloaded.Entries[0].Value);          // ints round-trip as long
        Assert.Equal("20", reloaded.Entries[0].Old);
        Assert.Equal(12.5, reloaded.Entries[1].Value);
        Assert.Equal("Ha 300", reloaded.Entries[2].Value);
        Assert.Null(reloaded.Entries[3].Value);
        Assert.Equal(TsTable.Target, reloaded.Entries[1].Table);
        Assert.Equal(TsEditKind.Manual, reloaded.Entries[0].Kind);

        // Seq continues after reload — no reuse.
        TsJournalEntry next = reloaded.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 1, "0", "A");
        Assert.Equal(5, next.Seq);
    }

    [Fact]
    public void CollapsedCount_TracksDistinctFields_ThroughAppendCommitAndReload()
    {
        // The badge's cached count (review N2) must always equal Collapse().Count.
        string path = NewPath();
        TsJournal journal = new(path);
        Assert.Equal(0, journal.CollapsedCount);

        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 20, "10", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");   // same field — collapses
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 0, "1", "A");
        Assert.Equal(2, journal.CollapsedCount);
        Assert.Equal(journal.Collapse().Count, journal.CollapsedCount);

        // Retention keeps only the failed field's entries.
        journal.CommitPush([TsJournal.FieldKey(journal.Entries[0])], journal.Entries[^1].Seq);
        Assert.Equal(1, journal.CollapsedCount);

        Assert.Equal(1, new TsJournal(path).CollapsedCount);   // reload rebuilds the cache

        journal.Clear();
        Assert.Equal(0, journal.CollapsedCount);
    }

    [Fact]
    public void Collapse_LastWritePerField_KeepsFirstOld()
    {
        TsJournal journal = new(NewPath());
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 20, "10", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 0, "1", "A");

        List<TsJournalEntry> collapsed = journal.Collapse();
        Assert.Equal(2, collapsed.Count);
        TsJournalEntry desired = collapsed.Single(e => e.Column == "desired");
        Assert.Equal(25L, desired.Value);                      // last write wins
        Assert.Equal("10", desired.Old);                       // review shows original → final
    }

    [Fact]
    public void Collapse_AcrossKinds_LastWriterOwnsTheField()
    {
        TsJournal journal = new(NewPath());
        journal.Append(TsEditKind.WriteBack, TsTable.ExposurePlan, "500", "desired", 42, "34", "A · H @300s");
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "500", "desired", 50, "42", "A · H");

        TsJournalEntry winner = Assert.Single(journal.Collapse());
        Assert.Equal(TsEditKind.Manual, winner.Kind);          // the explicit edit outranks the stamp
        Assert.Equal(50L, winner.Value);
        Assert.Equal("34", winner.Old);
    }

    [Fact]
    public void ReplaceAll_RetainsOnlyGiven_AndPersists()
    {
        string path = NewPath();
        TsJournal journal = new(path);
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        TsJournalEntry keep = journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 1, "0", "B");

        journal.ReplaceAll([keep]);
        Assert.Equal(1, journal.Count);
        Assert.Equal("g-1", Assert.Single(new TsJournal(path).Entries).Key);
    }

    [Fact]
    public void CommitPush_DropsApplied_KeepsFailedAndMidPushNewcomers()
    {
        string path = NewPath();
        TsJournal journal = new(path);
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 20, "10", "A · H");   // seq 1, applied
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");   // seq 2, applied
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 1, "0", "B");                 // seq 3, failed
        // The push snapshot ends at seq 3; this edit lands mid-push and must survive the rewrite.
        journal.Append(TsEditKind.Manual, TsTable.ExposurePlan, "ep-1", "desired", 30, "25", "A · H");   // seq 4

        journal.CommitPush([$"{TsTable.ExposurePlan}|ep-1|desired"], throughSeq: 3);

        Assert.Equal(2, journal.Count);
        Assert.Contains(journal.Entries, e => e is { Seq: 3, Key: "g-1" });          // failed field retained
        Assert.Contains(journal.Entries, e => e is { Seq: 4, Value: 30L });          // newcomer retained
        Assert.Equal(2, new TsJournal(path).Count);                                  // …and persisted
    }

    [Fact]
    public void Clear_EmptiesAndDeletesTheSidecar()
    {
        string path = NewPath();
        TsJournal journal = new(path);
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 1, "0", "A");

        journal.Clear();
        Assert.True(journal.IsEmpty);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TornTrailingLine_IsSkipped_ValidEntriesLoad()
    {
        string path = NewPath();
        TsJournal journal = new(path);
        journal.Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", 1, "0", "A");
        File.AppendAllText(path, "{\"Seq\": 2, \"Kind\": \"Manu");   // crash mid-append

        TsJournal reloaded = new(path);
        Assert.Equal(1, reloaded.Count);                        // the torn line is dropped, loudly logged
        Assert.False(reloaded.IsEmpty);
    }

    [Fact]
    public void BoolValues_NormalizeToIntegers()
    {
        string path = NewPath();
        new TsJournal(path).Append(TsEditKind.Manual, TsTable.Target, "g-1", "active", true, "0", "A");
        Assert.Equal(1L, Assert.Single(new TsJournal(path).Entries).Value);   // SQLite flag columns take 0/1
    }
}
