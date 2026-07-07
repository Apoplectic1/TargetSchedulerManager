using System.Globalization;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using Microsoft.Data.Sqlite;

namespace TargetSchedulerManager.App.Shared;

/// <summary>One write-back plan's collapsed journal state for the push review: the count change when one was
/// journaled (decreases listed first — a 0 from a scan miss is the dangerous half) and/or the desired raise.
/// A desired-only raise (counts already matched disk) carries no count pair — the review must not assert a
/// count change that never happened.</summary>
internal sealed record PushReviewCountLine(
    string Label, string? OldCount, long? NewCount, string? OldDesired, long? NewDesired, bool IsDecrease);

/// <summary>One collapsed manual field edit for the push review: original → final value as display text.</summary>
internal sealed record PushReviewFieldLine(string Label, string Column, string? Old, string New);

/// <summary>Everything the push (and open-with-dirty) dialog shows: the collapsed journal split into the
/// write-back summary and the manual edits, plus the staleness/busy facts from the latest remote probe.</summary>
internal sealed record PushReview(
    IReadOnlyList<PushReviewCountLine> WriteBack,
    IReadOnlyList<PushReviewFieldLine> Manual,
    bool RemoteChangedSinceBaseline,
    bool RemoteBusy,
    DateTimeOffset? OldestEditAt,
    int CollapsedCount);

internal enum PushOutcome
{
    /// <summary>Every collapsed entry applied + verified; the journal cleared and a fresh pull re-baselined.</summary>
    Success,
    /// <summary>Some entries applied; the failures were retained in the journal and reported.</summary>
    PartialFailure,
    /// <summary>Nothing journaled — no-op.</summary>
    NothingToPush,
    /// <summary>BIRDWATCHER did not answer the probe; nothing was written.</summary>
    Unreachable,
    /// <summary>A remote open sidecar (NINA imaging?) refused the whole push; nothing was written.</summary>
    RefusedBusy,
    /// <summary>The remote db refused structurally (schema/read-only); nothing was written.</summary>
    Refused,
}

/// <summary>One failed replay entry, for the loud push summary.</summary>
internal sealed record PushFailure(string Label, string Detail);

/// <summary>The outcome of one push: what applied, what failed (and stayed journaled), and whether the
/// closing pull ran (local now mirrors the remote, including its overnight accruals).</summary>
internal sealed record PushResult(
    PushOutcome Outcome,
    int AppliedCount,
    IReadOnlyList<PushFailure> Failures,
    bool PulledFresh,
    string? RefusalDetail = null);

/// <summary>
/// The sync-model orchestrator, one per session: BIRDWATCHER is read at <b>pull</b> and written only at
/// <b>push</b>; every edit in between hits the local db and journals. Owns the two paths, the timeout-guarded
/// remote probe, the persisted baseline (<see cref="TsSyncState"/>) and journal (<see cref="TsJournal"/>),
/// the pull/skip decision, and the push replay.
/// <para>
/// Push is a <b>journal replay, never a file copy</b>: manual field edits go per-field through the library's
/// guarded read-back-verified editor; write-back entries re-execute the write-back contract per plan through
/// the library writer (acquired = accepted = journaled disk count; desired ratchets against the <em>remote</em>
/// desired, so a goal someone raised on the rig is never lowered by replay). Only journaled fields are touched —
/// everything BIRDWATCHER accrued since the pull (NINA's nightly counts, acquiredimage history, XFM grades) is
/// structurally untouched. A fully-applied push ends in a fresh pull, so the one baseline invariant holds:
/// <b>a baseline is recorded exactly when the local copy mirrors the remote</b>.
/// </para>
/// UI-free and single-caller like the rest of Shared\: the view-model serializes calls; no locking. Machine/
/// network policy, so it lives in the App's <c>Shared\</c> folder, never the consumer-neutral library.
/// </summary>
internal sealed class TsSync
{
    private readonly TsSyncState _state;
    private readonly Func<string, ITsEditor> _editorFactory;
    private readonly Func<string, ITsWriteBackApplier> _applierFactory;
    private readonly TimeSpan _probeTimeout;

    public TsSync(
        string remotePath,
        string localPath,
        Func<string, ITsEditor> editorFactory,
        Func<string, ITsWriteBackApplier> applierFactory,
        TimeSpan? probeTimeout = null)
    {
        RemotePath = remotePath;
        LocalPath = localPath;
        _editorFactory = editorFactory;
        _applierFactory = applierFactory;
        _probeTimeout = probeTimeout ?? TsDatabaseResolver.DefaultProbeTimeout;
        _state = TsSyncState.Load(localPath + ".tsm-sync.json");
        Journal = new TsJournal(localPath + ".tsm-edits.jsonl");
    }

    /// <summary>The real session: the live BIRDWATCHER db, the local working copy, and the production adapters.</summary>
    public static TsSync CreateDefault() => new(
        DevDefaults.TsDatabaseLive, DevDefaults.TsDatabase,
        path => new TsEditorAdapter(path), path => new TsWriteBackAdapter(path));

    public string RemotePath { get; }

    /// <summary>The local working copy — the only db the load and every edit touch.</summary>
    public string LocalPath { get; }

    public TsJournal Journal { get; }

    /// <summary>Unpushed local writes exist (journal non-empty) — derived, never a stored flag.</summary>
    public bool IsDirty => !Journal.IsEmpty;

    public TsBaseline? Baseline => _state.Baseline;

    /// <summary>The latest probe's result: null = BIRDWATCHER unreachable as of that probe.</summary>
    public TsDbStat? LastProbe { get; private set; }

    /// <summary>True once any probe ran this session — distinguishes "unreachable" from "not asked yet".</summary>
    public bool HasProbed { get; private set; }

    /// <summary>True when the latest probe answered (reachability as displayed, not re-checked).</summary>
    public bool RemoteReachable => LastProbe is not null;

    /// <summary>Stats the remote db under the probe timeout (blocking up to ~1.5 s — call off the UI thread).</summary>
    public TsDbStat? ProbeRemote()
    {
        HasProbed = true;
        return LastProbe = TsDatabaseResolver.Stat(RemotePath, _probeTimeout);
    }

    /// <summary>
    /// The skip rule (D1): pull unless the baseline proves the local copy current — remote size + mtime equal
    /// the recorded values AND no remote sidecar exists (under WAL, content can change in <c>-wal</c> without
    /// touching the main file's mtime, so sidecar-present means "ambiguous → pull"). Unbaselined always pulls.
    /// </summary>
    public bool ShouldPull(TsDbStat probe) =>
        probe.HasSidecar
        || _state.Baseline is not { } b
        || b.RemoteLength != probe.Length
        || b.RemoteLastWriteUtc != probe.LastWriteUtc;

    /// <summary>Pulls only when <see cref="ShouldPull"/> says the local copy may be stale; true when it pulled.</summary>
    public bool PullIfChanged(TsDbStat probe)
    {
        if (!ShouldPull(probe))
        {
            Log.Info($"PULL skipped — baseline matches ({probe.Length:N0} bytes @ {probe.LastWriteUtc:u}, no sidecar)");
            return false;
        }
        Pull(probe);
        return true;
    }

    /// <summary>
    /// Copies the remote db over the local one via the SQLite online backup API — a consistent snapshot even
    /// while NINA holds the file (unlike a raw file copy, which can tear). Records the baseline from the
    /// PRE-pull probe: a write landing during the copy makes the content newer than the baseline, which can
    /// only cause an extra pull next open, never a false skip. Throws on failure (fail loud — the caller's
    /// load surfaces it); pooling is off so no SMB handle outlives the copy.
    /// </summary>
    public void Pull(TsDbStat probe)
    {
        using SqliteConnection source = new(new SqliteConnectionStringBuilder
        {
            DataSource = RemotePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = LocalPath,
            Mode = SqliteOpenMode.ReadWriteCreate,   // first run may have no local copy yet
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        // The sidecar rule forces a pull exactly when NINA is mid-transaction — give the backup's lock
        // acquisition the same patience the reader/writer get instead of failing on the first SQLITE_BUSY.
        using (SqliteCommand pragma = source.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout = 2000;";
            pragma.ExecuteNonQuery();
        }
        source.BackupDatabase(destination);

        _state.Record(new TsBaseline(probe.Length, probe.LastWriteUtc, DateTimeOffset.Now));
        Log.Info($"PULL {RemotePath} -> {LocalPath} ({probe.Length:N0} bytes, remote mtime {probe.LastWriteUtc:u})");
    }

    /// <summary>Journals one verified manual field edit (the gate calls this after read-back verification).</summary>
    public void RecordEdit(TsTable table, string key, string column, object? value, string? old, string label) =>
        Journal.Append(TsEditKind.Manual, table, key, column, value, old, label);

    /// <summary>Journals one verified write-back column stamp (the post-load write-back step calls this).</summary>
    public void RecordWriteBack(string tsPlanKey, string column, object? value, string? old, string label) =>
        Journal.Append(TsEditKind.WriteBack, TsTable.ExposurePlan, tsPlanKey, column, value, old, label);

    /// <summary>Opens the write-back applier on the local db (the post-load stamping step).</summary>
    public ITsWriteBackApplier CreateLocalWriteBackApplier() => _applierFactory(LocalPath);

    /// <summary>The user's deliberate Discard: drop every unpushed edit (the caller then pulls fresh).</summary>
    public void Discard()
    {
        Log.Info($"DISCARD {Journal.Count} unpushed journal entries (user chose discard-and-pull)");
        Journal.Clear();
    }

    /// <summary>Shapes the collapsed journal + the probe's facts into the push/open-with-dirty review.</summary>
    public PushReview PreparePush(TsDbStat? probe)
    {
        List<TsJournalEntry> collapsed = Journal.Collapse();
        List<PushReviewFieldLine> manual = [.. collapsed
            .Where(e => e.Kind == TsEditKind.Manual)
            .Select(e => new PushReviewFieldLine(e.Label, e.Column, e.Old, FormatValue(e.Value)))];

        List<PushReviewCountLine> writeBack = [];
        foreach (IGrouping<string, TsJournalEntry> plan in collapsed
            .Where(e => e.Kind == TsEditKind.WriteBack)
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            // The count pair only when a count column was journaled — a desired-only raise (counts already
            // matched disk) must not display as a phantom acquired change.
            TsJournalEntry? count =
                plan.FirstOrDefault(e => string.Equals(e.Column, "acquired", StringComparison.OrdinalIgnoreCase))
                ?? plan.FirstOrDefault(e => string.Equals(e.Column, "accepted", StringComparison.OrdinalIgnoreCase));
            TsJournalEntry? desired = plan.FirstOrDefault(e =>
                string.Equals(e.Column, "desired", StringComparison.OrdinalIgnoreCase));
            long? newCount = count is null ? null : Convert.ToInt64(count.Value, CultureInfo.InvariantCulture);
            bool isDecrease = newCount is { } n && long.TryParse(count!.Old, out long oldCount) && n < oldCount;
            writeBack.Add(new PushReviewCountLine(
                (count ?? desired ?? plan.First()).Label, count?.Old, newCount,
                desired?.Old, desired is null ? null : Convert.ToInt64(desired.Value, CultureInfo.InvariantCulture),
                isDecrease));
        }
        writeBack = [.. writeBack
            .OrderByDescending(l => l.IsDecrease)   // decreases first — the dangerous half
            .ThenBy(l => l.Label, StringComparer.OrdinalIgnoreCase)];

        return new PushReview(
            writeBack, manual,
            RemoteChangedSinceBaseline: probe is not null && _state.Baseline is { } b
                && (b.RemoteLength != probe.Length || b.RemoteLastWriteUtc != probe.LastWriteUtc),
            RemoteBusy: probe?.HasSidecar == true,
            OldestEditAt: Journal.IsEmpty ? null : Journal.Entries.Min(e => e.At),
            CollapsedCount: collapsed.Count);
    }

    /// <summary>
    /// The push: probe (unreachable/busy refuse the whole push before any write), then replay the collapsed
    /// journal — write-back plans first through the library writer, then manual field edits in seq order
    /// through the guarded field editor (so an explicit later desired edit outranks the writer's ratchet).
    /// Per-entry failures (row gone, verify mismatch) are reported loudly and their entries retained; a fully
    /// applied push clears the journal and ends in a fresh pull that re-records the baseline.
    /// </summary>
    public PushResult Push()
    {
        List<TsJournalEntry> collapsed = Journal.Collapse();
        if (collapsed.Count == 0)
            return new PushResult(PushOutcome.NothingToPush, 0, [], PulledFresh: false);

        TsDbStat? probe = ProbeRemote();
        if (probe is null)
        {
            Log.Warn("PUSH refused — BIRDWATCHER unreachable");
            return new PushResult(PushOutcome.Unreachable, 0, [], PulledFresh: false);
        }
        if (probe.HasSidecar)
        {
            Log.Warn("PUSH refused — remote TS db has an open sidecar (NINA imaging?)");
            return new PushResult(PushOutcome.RefusedBusy, 0, [], PulledFresh: false);
        }

        HashSet<long> failedSeqs = [];
        List<PushFailure> failures = [];
        List<TsJournalEntry> writeBack = [.. collapsed.Where(e => e.Kind == TsEditKind.WriteBack)];
        List<TsJournalEntry> fields = [.. collapsed.Where(e => e.Kind == TsEditKind.Manual)];

        // ---- write-back leg: re-execute the write-back contract per journaled plan on the remote ----------
        if (writeBack.Count > 0)
        {
            using ITsWriteBackApplier applier = _applierFactory(RemotePath);
            if (!applier.HasRequiredColumns)
                return RefusedStructurally("remote TS db schema incompatible (exposureplan columns missing)");
            if (applier.IsReadOnly)
                return RefusedStructurally("remote TS db file is read-only");
            if (applier.HasOpenSidecar)
            {
                Log.Warn("PUSH refused — remote TS db has an open sidecar (NINA imaging?)");
                return new PushResult(PushOutcome.RefusedBusy, 0, [], PulledFresh: false);
            }

            List<PlannedWrite> writes = [];
            Dictionary<long, IGrouping<string, TsJournalEntry>> byPlanId = [];
            foreach (IGrouping<string, TsJournalEntry> plan in writeBack.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                TsJournalEntry count = CountEntry(plan);
                if (!long.TryParse(plan.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long planId))
                {
                    // Our own journal wrote this key from WriteBackChange.TsExposurePlanId — non-integer is a
                    // contract violation; keep the entries and shout rather than guessing.
                    Fail(plan, $"journaled write-back key '{plan.Key}' is not a TS plan id");
                    continue;
                }
                byPlanId[planId] = plan;
                writes.Add(new PlannedWrite(
                    planId, Guid.Empty, count.Label, Filter: "", FilterPurpose.Light, PlanSeconds: 0,
                    DiskCount: checked((int)Convert.ToInt64(count.Value, CultureInfo.InvariantCulture))));
            }

            if (writes.Count > 0)
            {
                WriteBackResult result = applier.Execute(new WriteBackPlan(writes, [], [], 0), apply: true);
                foreach (WriteBackVerifyFailure f in result.VerifyFailures)
                {
                    IGrouping<string, TsJournalEntry> plan = byPlanId[f.TsExposurePlanId];
                    Fail(plan, f.ActualAcquired < 0
                        ? "TS plan row no longer exists on BIRDWATCHER"
                        : $"verify failed: expected {f.Expected}, remote reads {f.ActualAcquired}/{f.ActualAccepted}");
                }
            }
        }

        // ---- field leg: per-field guarded replay, seq order --------------------------------------------
        if (fields.Count > 0)
        {
            using ITsEditor editor = _editorFactory(RemotePath);
            foreach (TsJournalEntry e in fields)
            {
                if (failedSeqs.Contains(e.Seq))
                    continue;   // already failed by an aborting refusal below
                (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(e.Table, e.Key, e.Column, e.Value);
                if (refusal != RefusalReason.None)
                {
                    Fail([e], $"refused: {refusal}");
                    if (refusal is RefusalReason.SchemaIncompatible or RefusalReason.ReadOnly or RefusalReason.OpenSidecar)
                    {
                        // A whole-db refusal fails every remaining field the same way — stop hammering.
                        foreach (TsJournalEntry rest in fields.Where(f => f.Seq > e.Seq && !failedSeqs.Contains(f.Seq)))
                            Fail([rest], $"not attempted — push aborted on {refusal}");
                        break;
                    }
                }
                else if (result is not { Succeeded: true })
                {
                    Fail([e], result is { RowFound: false }
                        ? "row no longer exists on BIRDWATCHER"
                        : "read-back did not verify");
                }
            }
        }

        // Seq-aware retention: applied fields' entries drop (up to this push's snapshot), failed fields keep
        // their raw Old chain, and any edit journaled DURING the push survives untouched.
        int applied = collapsed.Count - failedSeqs.Count;
        Journal.CommitPush(
            [.. collapsed.Where(e => !failedSeqs.Contains(e.Seq)).Select(TsJournal.FieldKey)],
            collapsed[^1].Seq);

        if (failures.Count > 0)
        {
            Log.Error($"PUSH partial: {applied}/{collapsed.Count} applied; {failures.Count} FAILED and retained in the journal");
            return new PushResult(PushOutcome.PartialFailure, applied, failures, PulledFresh: false);
        }
        if (!Journal.IsEmpty)
        {
            // New edits landed mid-push: pulling now would overwrite their local values while they wait to
            // replay — skip the closing pull; the dirty badge shows them and the next push/open converges.
            Log.Warn($"PUSH applied {applied} field(s); {Journal.Count} edit(s) landed during the push — closing pull skipped");
            return new PushResult(PushOutcome.Success, applied, [], PulledFresh: false);
        }

        // Full success: pull fresh so the local copy also gains everything BIRDWATCHER accrued since the last
        // pull, and the baseline invariant (recorded ⇔ local mirrors remote) is restored in one place.
        TsDbStat? post = ProbeRemote();
        if (post is not null)
            Pull(post);
        else
            Log.Warn("PUSH applied but BIRDWATCHER dropped before the closing pull — next open will pull fresh");
        Log.Info($"PUSH applied {applied} field(s) to {RemotePath}");
        return new PushResult(PushOutcome.Success, applied, [], PulledFresh: post is not null);

        void Fail(IEnumerable<TsJournalEntry> entries, string detail)
        {
            foreach (TsJournalEntry e in entries)
            {
                if (!failedSeqs.Add(e.Seq))
                    continue;
                failures.Add(new PushFailure(e.Label, $"{e.Column}: {detail}"));
                Log.Error($"PUSH failed for \"{e.Label}\" {e.Table}.{e.Column}: {detail}");
            }
        }

        PushResult RefusedStructurally(string detail)
        {
            Log.Error($"PUSH refused — {detail}");
            return new PushResult(PushOutcome.Refused, 0, [], PulledFresh: false, RefusalDetail: detail);
        }
    }

    // A write-back plan group's count entry: acquired when journaled, else accepted, else desired (a
    // desired-only raise still carries the disk count — the ratchet only ever raises TO the count).
    private static TsJournalEntry CountEntry(IGrouping<string, TsJournalEntry> plan) =>
        plan.FirstOrDefault(e => string.Equals(e.Column, "acquired", StringComparison.OrdinalIgnoreCase))
        ?? plan.FirstOrDefault(e => string.Equals(e.Column, "accepted", StringComparison.OrdinalIgnoreCase))
        ?? plan.First();

    private static string FormatValue(object? value) =>
        value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
}
