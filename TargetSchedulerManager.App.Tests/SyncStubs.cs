using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.Tests;

/// <summary>Shared seams + fixtures for the sync-model tests: per-test temp dirs, a minimal real TS db (for
/// the pull's online-backup path), a recording <see cref="ITsEditor"/> (the push field leg), and an in-memory
/// <see cref="ITsWriteBackApplier"/> that mirrors the library writer's read→ratchet→apply→verify semantics.</summary>
internal static class SyncTestEnv
{
    /// <summary>A fresh writable directory per test — journal/state sidecars and db files land here.</summary>
    public static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tsm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A <see cref="TsSync"/> over temp paths whose push seams throw — for tests that never push.</summary>
    public static TsSync NewSync(out string dir)
    {
        dir = NewDir();
        return new TsSync(
            Path.Combine(dir, "remote.sqlite"),
            Path.Combine(dir, "local.sqlite"),
            _ => throw new InvalidOperationException("no editor expected in this test"),
            _ => throw new InvalidOperationException("no write-back applier expected in this test"));
    }

    /// <summary>Creates a real (minimal) SQLite db at <paramref name="path"/> with one marker row — enough
    /// for the online-backup pull to copy and the test to fingerprint which content the local file holds.</summary>
    public static void CreateDb(string path, string marker)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS marker (v TEXT); DELETE FROM marker; INSERT INTO marker (v) VALUES ($v);";
        cmd.Parameters.AddWithValue("$v", marker);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the marker row back (null when the file/table is absent).</summary>
    public static string? ReadMarker(string path)
    {
        if (!File.Exists(path)) return null;
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT v FROM marker LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }
}

/// <summary>Records every TrySetField for exact replay assertions; keys in <see cref="MissingKeys"/> report
/// row-not-found; <see cref="RefuseAll"/> refuses every write with the given reason.</summary>
internal sealed class RecordingEditor : ITsEditor
{
    public List<(TsTable Table, string Key, string Column, object? Value)> Writes { get; } = [];
    public HashSet<string> MissingKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public RefusalReason RefuseAll { get; set; } = RefusalReason.None;
    public int TrySetFieldCalls { get; private set; }

    public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value)
    {
        TrySetFieldCalls++;
        if (RefuseAll != RefusalReason.None)
            return (null, RefuseAll);
        if (MissingKeys.Contains(tsKey))
            return (new FieldEditResult(RowFound: false, OldValue: null, Verified: false), RefusalReason.None);
        Writes.Add((table, tsKey, column, value));
        return (new FieldEditResult(RowFound: true, OldValue: "old", Verified: true), RefusalReason.None);
    }

    public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column) => (false, null);
    public bool IsFieldAvailable(TsTable table, string column) => true;
    public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey) => (false, null);

    /// <summary>Recorded insert batches (the push insert leg / the gate's local insert). Parent guid values
    /// listed in <see cref="UnresolvedParentGuids"/> report an unresolved-parent rollback;
    /// <see cref="FailInsertVerify"/> reports a landed-but-unverified row. Minted row ids count up from 700.</summary>
    public List<TsRowInsert> Inserts { get; } = [];
    public HashSet<string> UnresolvedParentGuids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FailInsertVerify { get; set; }
    private long _nextRowId = 700;

    public (InsertOutcome? Outcome, RefusalReason Refusal) TryInsertRows(IReadOnlyList<TsRowInsert> rows)
    {
        if (RefuseAll != RefusalReason.None)
            return (null, RefuseAll);
        string[] parents = ["targetid", "exposureTemplateId", "projectid"];
        RowInsertResult[] results = new RowInsertResult[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            string? unresolved = rows[i].Payload
                .Where(kv => parents.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)
                    && kv.Value is string guid && UnresolvedParentGuids.Contains(guid))
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (unresolved is not null)
            {
                for (int n = 0; n < rows.Count; n++)
                    results[n] ??= new RowInsertResult(RowId: 0, Verified: false);
                results[i] = new RowInsertResult(RowId: 0, Verified: false, UnresolvedParentColumn: unresolved);
                return (new InsertOutcome(Applied: false, results), RefusalReason.None);
            }
            results[i] = new RowInsertResult(_nextRowId++, Verified: !FailInsertVerify);
        }
        Inserts.AddRange(rows);
        return (new InsertOutcome(Applied: true, results), RefusalReason.None);
    }

    public void Dispose() { }
}

/// <summary>In-memory stand-in for the library writer: <see cref="Rows"/> is the db (planId → counts); Execute
/// mirrors the real read→diff→apply→verify shape, including the desired ratchet (max of current desired and the
/// disk count) and a verify failure for a missing row.</summary>
internal sealed class StubWriteBackApplier : ITsWriteBackApplier
{
    public Dictionary<long, (int Acquired, int Accepted, int Desired)> Rows { get; } = [];
    public bool Sidecar { get; set; }
    public bool ReadOnlyFile { get; set; }
    public bool ColumnsPresent { get; set; } = true;
    public int ApplyCalls { get; private set; }

    public bool HasRequiredColumns => ColumnsPresent;
    public bool IsReadOnly => ReadOnlyFile;
    public bool HasOpenSidecar => Sidecar;

    public WriteBackResult Execute(WriteBackPlan plan, bool apply)
    {
        if (apply) ApplyCalls++;
        List<WriteBackChange> changes = [];
        List<WriteBackVerifyFailure> failures = [];
        foreach (PlannedWrite w in plan.Writes)
        {
            bool found = Rows.TryGetValue(w.TsExposurePlanId, out (int Acquired, int Accepted, int Desired) r);
            (int acq, int acc, int des) = found ? r : (-1, -1, -1);
            WriteBackChange change = new(
                w.TsExposurePlanId, w.TargetName, w.Filter, w.Purpose, w.PlanSeconds,
                acq, acc, des, w.DiskCount, Math.Max(des, w.DiskCount));
            changes.Add(change);
            if (apply)
            {
                if (found)
                    Rows[w.TsExposurePlanId] = (change.NewCount, change.NewCount, change.NewDesired);
                else
                    failures.Add(new WriteBackVerifyFailure(w.TsExposurePlanId, w.DiskCount, -1, -1));
            }
        }
        return new WriteBackResult(changes, apply, failures);
    }

    public void Dispose() { }
}
