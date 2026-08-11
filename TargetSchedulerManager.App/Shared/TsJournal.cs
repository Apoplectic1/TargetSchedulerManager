using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Core.Time;
using Astronomy.Diagnostics;

namespace TargetSchedulerManager.App.Shared;

/// <summary>Who authored a journaled local write: the user's own field edit, the automatic disk→TS
/// write-back stamp, or a row created locally (adoption). Push replays each through its owning mechanism
/// (field editor vs write-back writer vs the guarded insert) and the review dialog lists them in separate
/// sections.</summary>
internal enum TsEditKind { Manual, WriteBack, Insert }

/// <summary>
/// One verified local TS write, journaled for replay: the field coordinate (table, guid-or-Id key, column),
/// the absolute <paramref name="Value"/> written (never a delta — replay is idempotent and order-free after
/// collapse), the prior value as display text, and a grid-style label ("M 81 · Ha") for review lists.
/// An <see cref="TsEditKind.Insert"/> entry is a whole created row: <paramref name="Column"/> is the
/// <see cref="TsJournal.InsertColumn"/> pseudo-column, <paramref name="Value"/> the full column payload as
/// JSON, <paramref name="Old"/> null (nothing preceded a creation), and <see cref="RowGuid"/> the row's
/// minted guid — the cross-copy name the replay inserts under.
/// </summary>
internal sealed record TsJournalEntry(
    long Seq,
    TsEditKind Kind,
    TsTable Table,
    string Key,
    string Column,
    object? Value,
    string? Old,
    string Label,
    DateTimeOffset At)
{
    /// <summary>The created row's minted guid — non-null exactly on <see cref="TsEditKind.Insert"/> entries.</summary>
    public string? RowGuid { get; init; }
}

/// <summary>
/// The persisted session journal: every verified write to the local TS db appends one JSON line to a sidecar
/// beside the db, so unpushed work survives crashes and relaunches (guards carry facts). "Dirty" is defined as
/// this journal being non-empty — derived, never a separately stored flag that could disagree after a crash.
/// Push replays the <see cref="Collapse"/>d entries (last write per field, first write's Old for review);
/// entries whose replay fails are retained via <see cref="ReplaceAll"/>. Rewrites are crash-safe
/// (temp + atomic move). An unreadable line (torn by a crash mid-append) is skipped loudly — the local db
/// still holds the value, so nothing is silently lost locally.
/// <para><b>Durability boundary (2026-07-24, review M2):</b> an append is flushed to the OS before the
/// entry is visible in memory — entries survive a <em>process</em> crash. An OS/power failure can lose
/// the final line, and no flush here could close that: the SQLite commit and this append are two separate
/// durability events, never atomic with each other. The loss mode is bounded — the local db still holds
/// the write (the grid stays correct); only that entry's replay at push is lost.</para>
/// </summary>
internal sealed class TsJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // One coarse lock over the list + seq + file: appends come from Task.Run workers (a gate edit can commit
    // while the load's write-back step is stamping) and the UI thread's badge getter collapses concurrently.
    private readonly Lock _lock = new();
    private readonly string _path;
    private readonly List<TsJournalEntry> _entries = [];
    // Distinct field keys, maintained under _lock — so the badge's collapsed count is two field reads
    // instead of a full Collapse() (dictionaries + sort) on the UI thread per sync-state raise (review N2).
    private readonly HashSet<string> _fieldKeys = new(StringComparer.OrdinalIgnoreCase);
    // Each field's baseline: the FIRST journaled Old since the last push — the value a later write must
    // equal for the field to prune back to clean (noop-edit-pruning). Rebuilt with _fieldKeys everywhere.
    private readonly Dictionary<string, string?> _baselineOld = new(StringComparer.OrdinalIgnoreCase);
    private long _nextSeq = 1;

    // The portfolio clock seam (AL CONSUMERS.md clock convention) — entry timestamps are
    // provenance data, so they route through the injectable clock rather than the ambient one.
    private readonly IClock _clock;

    public TsJournal(string path, IClock? clock = null)
    {
        _path = path;
        _clock = clock ?? SystemClock.Instance;
        Load();
    }

    public bool IsEmpty
    {
        get { lock (_lock) return _entries.Count == 0; }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>What <see cref="Collapse"/>().Count would return — distinct fields with unpushed writes —
    /// without building the collapse (cheap enough for every badge refresh on the UI thread).</summary>
    public int CollapsedCount
    {
        get { lock (_lock) return _fieldKeys.Count; }
    }

    /// <summary>A snapshot of all entries, oldest first (persisted order).</summary>
    public IReadOnlyList<TsJournalEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    /// <summary>Appends one verified write, flushed to the OS before it appears in <see cref="Entries"/> —
    /// survives a process crash; see the class doc for the honest OS/power-loss boundary.
    /// <para>Net-no-op pruning: a write whose value equals the field's baseline (the first journaled Old
    /// since the last push, or this write's own <paramref name="old"/> on first touch) resolves the field
    /// back to clean — its entries are pruned (crash-safe rewrite) and null is returned, so no surface
    /// reading the journal ever sees a net-no-op field. Equality is the one invariant display-text rule;
    /// a formatting mismatch fails safe to retention.</para></summary>
    public TsJournalEntry? Append(TsEditKind kind, TsTable table, string key, string column, object? value, string? old, string label)
    {
        lock (_lock)
        {
            object? canonical = Canonicalize(value);
            string field = $"{table}|{key}|{column}";
            string? baseline = _baselineOld.TryGetValue(field, out string? first) ? first : old;
            if (string.Equals(TsValueText.From(canonical), baseline, StringComparison.Ordinal))
            {
                if (_fieldKeys.Contains(field))
                    ReplaceAllLocked([.. _entries.Where(e => !FieldKey(e).Equals(field, StringComparison.OrdinalIgnoreCase))]);
                return null;   // reverted to baseline (or first-touch same-value): nothing to push
            }

            TsJournalEntry entry = new(_nextSeq++, kind, table, key, column, canonical, old, label,
                new DateTimeOffset(_clock.UtcNow).ToLocalTime());
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            _entries.Add(entry);
            if (_fieldKeys.Add(field))
                _baselineOld[field] = old;
            return entry;
        }
    }

    /// <summary>Pseudo-column of an <see cref="TsEditKind.Insert"/> entry — a created row is one journal
    /// "field" (it collapses, counts, and retains like any field) whose value is the whole payload.</summary>
    public const string InsertColumn = "(insert)";

    /// <summary>Appends one verified row creation (same durability contract as <see cref="Append"/>). Insert
    /// entries have no baseline and never prune — a creation clears only by push or discard. The payload is
    /// the full column set the row was created with, as JSON; <paramref name="key"/> is the row's key in its
    /// table's own key space (target guid; plan local integer id) so marks and later field edits match it.</summary>
    public TsJournalEntry AppendInsert(TsTable table, string key, string payloadJson, string rowGuid, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowGuid);
        lock (_lock)
        {
            TsJournalEntry entry = new(_nextSeq++, TsEditKind.Insert, table, key, InsertColumn, payloadJson,
                Old: null, label, new DateTimeOffset(_clock.UtcNow).ToLocalTime())
            { RowGuid = rowGuid };
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            _entries.Add(entry);
            string field = FieldKey(entry);
            if (_fieldKeys.Add(field))
                _baselineOld[field] = null;
            return entry;
        }
    }

    /// <summary>One field's identity for collapse/retention: (table, key, column), case-insensitive.</summary>
    public static string FieldKey(TsJournalEntry e) => $"{e.Table}|{e.Key}|{e.Column}";

    /// <summary>
    /// The replay set: last write per (table, key, column) — absolute values make earlier writes to the same
    /// field dead — carrying the FIRST write's <see cref="TsJournalEntry.Old"/> (review shows original → final)
    /// and the last write's Kind/Seq/Value. Ordered by Seq.
    /// </summary>
    public List<TsJournalEntry> Collapse()
    {
        lock (_lock)
        {
            Dictionary<string, TsJournalEntry> last = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string?> firstOld = new(StringComparer.OrdinalIgnoreCase);
            foreach (TsJournalEntry e in _entries)
            {
                string field = FieldKey(e);
                if (!firstOld.ContainsKey(field))
                    firstOld[field] = e.Old;
                last[field] = e;
            }
            return [.. last.Select(kv => kv.Value with { Old = firstOld[kv.Key] }).OrderBy(e => e.Seq)];
        }
    }

    /// <summary>
    /// Push retention: drops every entry of an APPLIED field up to <paramref name="throughSeq"/> (the push's
    /// collapse snapshot); keeps failed fields' raw entries (their Old chain survives for the next review) and
    /// anything journaled after the snapshot — an edit landing mid-push must never be silently dropped by the
    /// post-push rewrite. Crash-safe rewrite.
    /// </summary>
    public void CommitPush(IReadOnlyCollection<string> appliedFieldKeys, long throughSeq)
    {
        HashSet<string> applied = new(appliedFieldKeys, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            List<TsJournalEntry> remaining =
                [.. _entries.Where(e => e.Seq > throughSeq || !applied.Contains(FieldKey(e)))];
            ReplaceAllLocked(remaining);
        }
    }

    /// <summary>Replaces the journal wholesale — an empty list clears the file. Crash-safe rewrite.</summary>
    public void ReplaceAll(IReadOnlyList<TsJournalEntry> entries)
    {
        lock (_lock)
            ReplaceAllLocked(entries);
    }

    /// <summary>Discards every entry (the user's deliberate Discard).</summary>
    public void Clear() => ReplaceAll([]);

    private void ReplaceAllLocked(IReadOnlyList<TsJournalEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(e => e.Seq));
        RebuildIndexesLocked();
        if (_entries.Count == 0)
        {
            File.Delete(_path);
            return;
        }
        string tmp = _path + ".tmp";
        File.WriteAllLines(tmp, _entries.Select(e => JsonSerializer.Serialize(e, Options)));
        File.Move(tmp, _path, overwrite: true);
    }

    // The derived per-field indexes (badge count + pruning baselines), recomputed from _entries wholesale —
    // the FIRST entry's Old per field is its baseline (matching Collapse()'s firstOld).
    private void RebuildIndexesLocked()
    {
        _fieldKeys.Clear();
        _baselineOld.Clear();
        foreach (TsJournalEntry e in _entries)
            if (_fieldKeys.Add(FieldKey(e)))
                _baselineOld[FieldKey(e)] = e.Old;
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        int lineNo = 0;
        foreach (string line in File.ReadAllLines(_path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                TsJournalEntry? entry = JsonSerializer.Deserialize<TsJournalEntry>(line, Options);
                if (entry is null)
                    throw new JsonException("null entry");
                _entries.Add(entry with { Value = Canonicalize(entry.Value) });
            }
            catch (Exception ex)
            {
                // A torn trailing line from a crash mid-append: the local db already holds the write, so the
                // only loss is this entry's replay — say so loudly rather than aborting the session.
                Log.Error($"journal line {lineNo} unreadable — skipped (its local write is intact but will not replay): {_path} ({ex.Message})");
            }
        }
        _nextSeq = _entries.Count == 0 ? 1 : _entries.Max(e => e.Seq) + 1;
        RebuildIndexesLocked();
    }

    // One canonical box per value shape — whole numbers as long (including whole-valued doubles: JSON writes
    // 300.0 as "300", so reload could not tell them apart anyway; SQLite column affinity restores REAL),
    // fractional reals as double, flags as 0/1 long — applied on append AND on reload (JSON boxes `object?`
    // as JsonElement), so a value read back from disk is indistinguishable from one just written and replay
    // never depends on the session's age. A value shape no TS column can hold is a contract violation:
    // throw, never coerce quietly.
    private static object? Canonicalize(object? value) => value switch
    {
        null => null,
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => 1L,
            JsonValueKind.False => 0L,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out long l) ? l : WholeOrDouble(e.GetDouble()),
            _ => throw new JsonException($"unsupported journal value kind {e.ValueKind}"),
        },
        bool b => b ? 1L : 0L,
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        float or double => WholeOrDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        string s => s,
        _ => throw new ArgumentException($"unsupported journal value type {value.GetType().Name}"),
    };

    private static object WholeOrDouble(double d) =>
        double.IsInteger(d) && d is >= long.MinValue and <= long.MaxValue ? (long)d : d;

    /// <summary>The one canonical-box rule, for consumers re-hydrating values that rode through JSON outside
    /// an entry — e.g. the columns of an insert entry's payload at replay.</summary>
    internal static object? CanonicalizeValue(object? value) => Canonicalize(value);
}
