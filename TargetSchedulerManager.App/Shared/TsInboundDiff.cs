using System.Globalization;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;

namespace TargetSchedulerManager.App.Shared;

/// <summary>One observed inbound field difference: BIRDWATCHER's value for (table, key, column) arrived
/// different from what the local copy held before the pull. Values are invariant display strings (the marks
/// UI only shows them — nothing is ever written from an inbound observation). A row that exists remotely but
/// not locally is reported once with <see cref="TsInboundDiff.NewRowColumn"/>.</summary>
internal sealed record TsInboundChange(TsTable Table, string Key, string Column, string? Old, string? New);

/// <summary>
/// The session's inbound set: what BIRDWATCHER changed relative to what this TSM install last saw, per
/// (table, key, column) with old→new display values. In memory only — inbound is per-invocation information
/// by definition, so it starts empty each launch and is never persisted. Each pull's diff unions in (latest
/// observation wins per field) so a mid-session Pull-now accumulates instead of erasing the open's info, and
/// the near-empty diff of a push's closing pull leaves earlier entries standing. Write-back masks the
/// acquired/accepted entries its stamps supersede (<see cref="MaskPlanActuals"/>).
/// Locked coarsely like <see cref="TsJournal"/>: pulls and write-back run on worker threads while the UI
/// thread reads <see cref="Snapshot"/> for the marks sweep.
/// </summary>
internal sealed class TsInboundStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<TsTable, Dictionary<string, Dictionary<string, (string? Old, string? New)>>> _byTable = [];

    public bool IsEmpty
    {
        get { lock (_lock) return _byTable.Count == 0; }
    }

    /// <summary>Unions one pull's diff in: a repeated (table, key, column) is replaced by the newer observation.</summary>
    public void Apply(IReadOnlyList<TsInboundChange> changes)
    {
        lock (_lock)
        {
            foreach (TsInboundChange c in changes)
            {
                if (!_byTable.TryGetValue(c.Table, out Dictionary<string, Dictionary<string, (string?, string?)>>? keys))
                    _byTable[c.Table] = keys = new(StringComparer.OrdinalIgnoreCase);
                if (!keys.TryGetValue(c.Key, out Dictionary<string, (string?, string?)>? columns))
                    keys[c.Key] = columns = new(StringComparer.OrdinalIgnoreCase);
                columns[c.Column] = (c.Old, c.New);
            }
        }
    }

    /// <summary>
    /// The actuals override: a disk-derived write-back stamp supersedes whatever BIRDWATCHER's
    /// <c>acquired</c>/<c>accepted</c> were, so both are dropped from the plan's inbound entries — the row
    /// reads → while unpushed and goes clean (never a stale ←) once pushed. Other columns on the plan —
    /// including <c>desired</c> — are untouched: a rig-side goal change stays visible (⇄).
    /// </summary>
    public void MaskPlanActuals(string planKey)
    {
        lock (_lock)
        {
            if (!_byTable.TryGetValue(TsTable.ExposurePlan, out Dictionary<string, Dictionary<string, (string?, string?)>>? keys)
                || !keys.TryGetValue(planKey, out Dictionary<string, (string?, string?)>? columns))
                return;
            columns.Remove("acquired");
            columns.Remove("accepted");
            if (columns.Count == 0)
                keys.Remove(planKey);
            if (keys.Count == 0)
                _byTable.Remove(TsTable.ExposurePlan);
        }
    }

    /// <summary>A flat copy for the marks resolver (safe to read while a pull unions on another thread).</summary>
    public IReadOnlyList<TsInboundChange> Snapshot()
    {
        lock (_lock)
        {
            List<TsInboundChange> all = [];
            foreach ((TsTable table, Dictionary<string, Dictionary<string, (string? Old, string? New)>> keys) in _byTable)
                foreach ((string key, Dictionary<string, (string? Old, string? New)> columns) in keys)
                    foreach ((string column, (string? old, string? newValue)) in columns)
                        all.Add(new TsInboundChange(table, key, column, old, newValue));
            return all;
        }
    }
}

/// <summary>
/// The pull-time differ behind the ← marks: snapshots the local db's diffable fields before the backup
/// overwrites it, again after, and reports the field-level differences. The diffable set is authored, not
/// discovered — exactly the columns TSM displays or edits (the same semantics-live-in-code convention as
/// <see cref="TsEditableSchema"/>) — so TS-internal bookkeeping can never produce noise marks. Pure
/// observation: a table or column absent from a given db (marker test dbs, TS schema drift) is skipped
/// silently, never an abort, and nothing here ever writes.
/// </summary>
internal static class TsInboundDiff
{
    /// <summary>Pseudo-column reported once for a row that exists remotely but not locally.</summary>
    public const string NewRowColumn = "(new)";

    // The diffable set: key column + compared columns per table. Column names follow the TS schema exactly
    // (see TargetSchedulerReader's explicit SELECTs); the key spaces match the journal's — target and project
    // guid, plan/template integer Id as a string. (Project keys come from TargetResolver.Provenance, which
    // returns the TS guid; a guid-less project is simply not observed, since it could never be marked
    // either.) The template columns are derived from the editable schema (not a second hand-written list)
    // so ← coverage can never drift from what the flyout edits.
    private static readonly (TsTable Table, string KeyColumn, string[] Columns)[] FieldSet =
    [
        (TsTable.Target, "guid", ["active", "priority", "rotation", "name", "ra", "dec"]),
        (TsTable.ExposurePlan, "Id",
            ["desired", "acquired", "accepted", "exposure", "exposureTemplateId", "enabled"]),
        (TsTable.Project, "guid",
            ["state", "priority", "minimumtime", "minimumaltitude", "maximumaltitude", "usecustomhorizon",
             "horizonoffset", "meridianwindow", "ditherevery", "enablegrader", "smartexposureorder",
             "flatshandling", "filterswitchfrequency"]),
        (TsTable.ExposureTemplate, "Id",
            [.. TsEditableSchema.For(TsTable.ExposureTemplate).Select(f => f.Column)]),
    ];

    /// <summary>Reads the diffable fields of every table present in the db at <paramref name="path"/>:
    /// table → key → column → invariant display value.</summary>
    public static Dictionary<TsTable, Dictionary<string, Dictionary<string, string?>>> Snapshot(string path)
    {
        Dictionary<TsTable, Dictionary<string, Dictionary<string, string?>>> snapshot = [];
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();

        foreach ((TsTable table, string keyColumn, string[] columns) in FieldSet)
        {
            string tableName = TsEditableSchema.TableName(table);
            HashSet<string> present = TableColumns(connection, tableName);
            if (!present.Contains(keyColumn))
                continue;   // table absent (or keyless) on this db — not a TS db, or drift: observe nothing
            string[] cols = [.. columns.Where(present.Contains)];
            if (cols.Length == 0)
                continue;
            // Id-keyed tables also capture guid (never diffs — a row's guid is immutable): it lets Diff
            // correlate rows whose integer id changed between snapshots — a locally created row comes back
            // from its push renumbered by the remote's autoincrement, and a local-minted id can even collide
            // with a remote-minted id for a DIFFERENT row. Correlating by the cross-copy name prevents both
            // a phantom "new row" and a cross-row field diff.
            if (!keyColumn.Equals("guid", StringComparison.OrdinalIgnoreCase)
                && present.Contains("guid") && !cols.Contains("guid", StringComparer.OrdinalIgnoreCase))
                cols = [.. cols, "guid"];

            Dictionary<string, Dictionary<string, string?>> rows = new(StringComparer.OrdinalIgnoreCase);
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {keyColumn}, {string.Join(", ", cols)} FROM {tableName};";
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (Display(reader.GetValue(0)) is not string key)
                    continue;   // a keyless row can never be marked — skip, observation only
                Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Length; i++)
                    values[cols[i]] = Display(reader.GetValue(i + 1));
                rows[key] = values;
            }
            snapshot[table] = rows;
        }
        return snapshot;
    }

    /// <summary>
    /// The differences <paramref name="after"/> (the fresh pull) carries relative to <paramref name="before"/>
    /// (what the user last saw): changed values per column present in both snapshots, one
    /// <see cref="NewRowColumn"/> entry per remotely-added row. Remotely-deleted rows report nothing (their
    /// grid rows vanish at reload), and a column absent from either side is skipped.
    /// </summary>
    public static List<TsInboundChange> Diff(
        Dictionary<TsTable, Dictionary<string, Dictionary<string, string?>>> before,
        Dictionary<TsTable, Dictionary<string, Dictionary<string, string?>>> after)
    {
        List<TsInboundChange> changes = [];
        foreach ((TsTable table, Dictionary<string, Dictionary<string, string?>> afterRows) in after)
        {
            before.TryGetValue(table, out Dictionary<string, Dictionary<string, string?>>? beforeRows);
            Dictionary<string, Dictionary<string, string?>>? beforeByGuid = null;   // built on first need
            foreach ((string key, Dictionary<string, string?> afterColumns) in afterRows)
            {
                // Correlate guid-first: a key match only counts when the guids agree (or either side has no
                // guid data — then the key is all there is). A guid found under a DIFFERENT key is the same
                // row renumbered (a pushed local creation coming back with the remote's minted id) — diff its
                // fields under the new key instead of reporting a phantom new row.
                string? afterGuid = afterColumns.GetValueOrDefault("guid");
                Dictionary<string, string?>? beforeColumns = null;
                if (beforeRows is not null && beforeRows.TryGetValue(key, out Dictionary<string, string?>? byKey)
                    && (afterGuid is null || byKey.GetValueOrDefault("guid") is not string beforeGuid
                        || string.Equals(beforeGuid, afterGuid, StringComparison.OrdinalIgnoreCase)))
                {
                    beforeColumns = byKey;
                }
                else if (afterGuid is not null && beforeRows is not null)
                {
                    beforeByGuid ??= IndexByGuid(beforeRows);
                    beforeColumns = beforeByGuid.GetValueOrDefault(afterGuid);
                }
                if (beforeColumns is null)
                {
                    changes.Add(new TsInboundChange(table, key, NewRowColumn, null, "row"));
                    continue;
                }
                foreach ((string column, string? newValue) in afterColumns)
                {
                    if (!beforeColumns.TryGetValue(column, out string? oldValue))
                        continue;   // column arrived with the new schema — nothing was previously seen
                    if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                        changes.Add(new TsInboundChange(table, key, column, oldValue, newValue));
                }
            }
        }
        return changes;
    }

    // The before-rows re-keyed by guid, for the renumbered-row correlation (rows without a guid value can
    // never correlate this way and are skipped).
    private static Dictionary<string, Dictionary<string, string?>> IndexByGuid(
        Dictionary<string, Dictionary<string, string?>> rows)
    {
        Dictionary<string, Dictionary<string, string?>> byGuid = new(StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, string?> columns in rows.Values)
            if (columns.GetValueOrDefault("guid") is string guid)
                byGuid[guid] = columns;
        return byGuid;
    }

    private static HashSet<string> TableColumns(SqliteConnection connection, string tableName)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));
        return columns;
    }

    // One invariant display string per value shape, applied to BOTH snapshots — so equality is exact and a
    // whole-valued REAL can't ghost-diff against its integer spelling.
    private static string? Display(object value) => value switch
    {
        null or DBNull => null,
        double d => d.ToString("0.######", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
