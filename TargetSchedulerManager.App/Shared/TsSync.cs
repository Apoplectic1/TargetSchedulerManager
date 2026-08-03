using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace TargetSchedulerManager.App.Shared;

/// <summary>One write-back plan's collapsed journal state for the push review: the count change when one was
/// journaled (decreases listed first — a 0 from a scan miss is the dangerous half) and/or the desired raise.
/// A desired-only raise (counts already matched disk) carries no count pair — the review must not assert a
/// count change that never happened.</summary>
internal sealed record PushReviewCountLine(
    string Label, string? OldCount, long? NewCount, string? OldDesired, long? NewDesired, bool IsDecrease);

/// <summary>One collapsed manual field edit for the push review: original → final value as display text.</summary>
internal sealed record PushReviewFieldLine(string Label, string Column, string? Old, string New);

/// <summary>One row the push will CREATE on the remote: the entity's identity plus a compact summary of the
/// human-meaningful payload values — a reviewer sees exactly which rows come into existence before confirming.</summary>
internal sealed record PushReviewCreateLine(string Label, string Entity, string Summary);

/// <summary>Everything the push (and open-with-dirty) dialog shows: the collapsed journal split into the
/// creates, the write-back summary, and the manual edits, plus the staleness/busy facts from the latest
/// remote probe.</summary>
internal sealed record PushReview(
    IReadOnlyList<PushReviewCreateLine> Creates,
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
/// closing pull ran (local now mirrors the remote, including its overnight accruals).
/// <see cref="ClosingPullFailed"/> marks a push whose writes all landed but whose closing pull faulted —
/// still a SUCCESS (the journal cleared when the writes verified; the next open pulls fresh), the flag
/// only lets the status line say so.</summary>
internal sealed record PushResult(
    PushOutcome Outcome,
    int AppliedCount,
    IReadOnlyList<PushFailure> Failures,
    bool PulledFresh,
    string? RefusalDetail = null,
    bool ClosingPullFailed = false);

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

    /// <summary>The session's inbound set (what BIRDWATCHER changed relative to what this install last saw),
    /// fed by every pull's diff and masked by write-back stamps — the ← half of the grid's direction marks.</summary>
    public TsInboundStore Inbound { get; } = new();

    /// <summary>Unpushed local writes exist (journal non-empty) — derived, never a stored flag.</summary>
    public bool IsDirty => !Journal.IsEmpty;

    public TsBaseline? Baseline => _state.Baseline;

    /// <summary>The latest probe's result: null = BIRDWATCHER unreachable as of that probe.</summary>
    public TsDbStat? LastProbe { get; private set; }

    /// <summary>True once any probe ran this session — distinguishes "unreachable" from "not asked yet".</summary>
    public bool HasProbed { get; private set; }

    /// <summary>True when the latest probe answered (reachability as displayed, not re-checked).</summary>
    public bool RemoteReachable => LastProbe is not null;

    /// <summary>Stats the remote db under the probe timeout, blocking up to ~1.5 s — the push replay's
    /// form (it runs wholly on a worker). Async callers use <see cref="ProbeRemoteAsync"/>.</summary>
    public TsDbStat? ProbeRemote()
    {
        HasProbed = true;
        return LastProbe = TsDatabaseResolver.Stat(RemotePath, _probeTimeout);
    }

    /// <summary>The await-friendly probe (review N8): no thread parks for the timeout, and a UI-thread
    /// caller's continuation writes <see cref="LastProbe"/>/<see cref="HasProbed"/> back on the UI thread —
    /// the same thread that reads them for the badge.</summary>
    public async Task<TsDbStat?> ProbeRemoteAsync()
    {
        TsDbStat? probe = await TsDatabaseResolver.StatAsync(RemotePath, _probeTimeout);
        HasProbed = true;
        return LastProbe = probe;
    }

    /// <summary>
    /// The skip rule (D1): pull unless the baseline proves the local copy current — remote size + mtime equal
    /// the recorded values AND no remote sidecar exists (under WAL, content can change in <c>-wal</c> without
    /// touching the main file's mtime, so sidecar-present means "ambiguous → pull"). Unbaselined always pulls.
    /// </summary>
    public bool ShouldPull(TsDbStat probe) =>
        probe.HasSidecar || !BaselineMatches(probe);

    // The one definition of "the baseline still matches this probe": the skip rule reads it straight
    // (no baseline ⇒ no match ⇒ pull), the push review's staleness warning negates it BEHIND its own
    // has-a-baseline guard (no baseline ⇒ nothing to have changed *since* ⇒ silent). One comparison,
    // two consumers with opposite null postures — keep the guard at the consumer, never in here.
    private bool BaselineMatches(TsDbStat probe) =>
        _state.Baseline is { } b
        && b.RemoteLength == probe.Length
        && b.RemoteLastWriteUtc == probe.LastWriteUtc;

    /// <summary>Pulls only when <see cref="ShouldPull"/> says the local copy may be stale; true when it pulled.</summary>
    public bool PullIfChanged(TsDbStat probe, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        if (!ShouldPull(probe))
        {
            // The verified skip proves local == remote exactly as strongly as a pull would (that is why it
            // may skip) — refresh RecordedAt so the badge's "synced" time reads "last proven in sync", not
            // "last copy". The remote stats are the comparison key; the probe just showed them unchanged.
            _state.Record(new TsBaseline(probe.Length, probe.LastWriteUtc, DateTimeOffset.Now));
            Log.Info($"PULL skipped — baseline matches ({probe.Length:N0} bytes @ {probe.LastWriteUtc:u}, no sidecar); RecordedAt refreshed");
            return false;
        }
        Pull(probe, progress, cancel);
        return true;
    }

    /// <summary>
    /// The torn-local gate: a <c>-journal</c>/<c>-wal</c> beside the local db is a writer that died
    /// mid-transaction (e.g. the process killed during a pull) — a read-only open can never recover it, and
    /// the baseline skip rule would preserve it forever (it validates remote-vs-baseline, never local
    /// health). Heals by discarding the local copy, its sidecars, and the baseline; the caller MUST then
    /// pull before any local read. The edit journal (<c>.tsm-edits.jsonl</c>) is deliberately untouched:
    /// unpushed edits survive the heal and replay at push. True when torn state was found and cleared.
    /// </summary>
    public bool HealTornLocal()
    {
        string? sidecar =
            File.Exists(LocalPath + "-journal") ? "-journal" :
            File.Exists(LocalPath + "-wal") ? "-wal" : null;
        if (sidecar is null)
            return false;
        Log.Error($"LOCAL TORN — {LocalPath} has a hot {sidecar}; discarding the local copy + baseline and re-pulling");
        SqliteConnection.ClearAllPools();
        File.Delete(LocalPath);
        File.Delete(LocalPath + "-journal");
        File.Delete(LocalPath + "-wal");
        File.Delete(LocalPath + "-shm");
        _state.Clear();
        return true;
    }

    /// <summary>
    /// Refreshes the local db from the remote via the SQLite online backup API — a consistent snapshot even
    /// while NINA holds the file (unlike a raw file copy, which can tear). Atomic w.r.t. the local db: the
    /// backup lands in a temp sibling and is swapped over the local db only on completion, so a process
    /// death at ANY moment leaves the previous local db intact (the 2026-07-23 kill-mid-pull incident can't
    /// recur). The copy reports a percentage through <paramref name="progress"/> and stops between chunks
    /// when <paramref name="cancel"/> fires — a cancelled pull discards the temp file, records no baseline,
    /// and leaves the previous local db untouched (the OperationCanceledException surfaces to the caller).
    /// Records the baseline from the PRE-pull probe: a write landing during the copy makes the content newer
    /// than the baseline, which can only cause an extra pull next open, never a false skip. Throws on
    /// failure (fail loud — the caller's load surfaces it); pooling is off so no SMB handle outlives the copy.
    /// </summary>
    public void Pull(TsDbStat probe, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        string tmp = LocalPath + ".pull-tmp";
        SweepPullTmp(tmp);   // a dead pull's leftovers — its swap never ran, so they're garbage

        // Inbound-diff half 1: what the local copy holds now IS "what the user last saw" — snapshot it before
        // the swap replaces it. A first-ever pull has nothing previously seen, so it diffs nothing.
        Dictionary<TsTable, Dictionary<string, Dictionary<string, string?>>>? before =
            File.Exists(LocalPath) ? TsInboundDiff.Snapshot(LocalPath) : null;

        // The start line is the pull's only trace if it is interrupted — without it a killed pull is
        // invisible in the log (how the incident stayed undiagnosable).
        Log.Info($"PULL starting ({probe.Length:N0} bytes from {RemotePath})");
        Stopwatch sw = Stopwatch.StartNew();
        int lastPercent = 0;
        try
        {
            BackupTo(tmp, percent => { lastPercent = percent; progress?.Report(percent); }, cancel);
        }
        catch (OperationCanceledException)
        {
            SweepPullTmp(tmp);
            Log.Info($"PULL cancelled at {lastPercent}% — tmp discarded, local copy untouched");
            throw;
        }
        catch
        {
            SweepPullTmp(tmp);
            throw;
        }

        // The swap: same directory ⇒ same volume ⇒ atomic replace. Pooled reader/editor handles on the
        // local path would fail it with a sharing violation — close them first.
        SqliteConnection.ClearAllPools();
        File.Move(tmp, LocalPath, overwrite: true);

        // Half 2: diff the fresh copy against the pre-pull snapshot and union into the session's inbound set
        // (sticky — a push's closing pull diffs near-empty and leaves the open's entries standing).
        if (before is not null)
        {
            List<TsInboundChange> arrived = TsInboundDiff.Diff(before, TsInboundDiff.Snapshot(LocalPath));
            Inbound.Apply(arrived);
            if (arrived.Count > 0)
                Log.Info($"PULL inbound diff: {arrived.Count} field(s) arrived changed from BIRDWATCHER");
        }

        _state.Record(new TsBaseline(probe.Length, probe.LastWriteUtc, DateTimeOffset.Now));
        Log.Info($"PULL {RemotePath} -> {LocalPath} ({probe.Length:N0} bytes, remote mtime {probe.LastWriteUtc:u}) in {sw.Elapsed.TotalSeconds:0.0} s");
    }

    /// <summary>Chunked online backup of the remote db into <paramref name="destinationPath"/>, reporting
    /// whole percents and honoring <paramref name="cancel"/> between chunks —
    /// <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/> is all-or-nothing (no progress, no
    /// cancellation), so this steps <c>sqlite3_backup</c> directly.</summary>
    private void BackupTo(string destinationPath, Action<int> percent, CancellationToken cancel)
    {
        using SqliteConnection source = new(new SqliteConnectionStringBuilder
        {
            DataSource = RemotePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using SqliteConnection destination = new(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        // The sidecar rule forces a pull exactly when NINA is mid-transaction — give the backup's lock
        // acquisition the same patience the reader/writer get instead of failing on the first SQLITE_BUSY.
        using (SqliteCommand pragma = source.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMs};";
            pragma.ExecuteNonQuery();
        }

        using sqlite3_backup backup = raw.sqlite3_backup_init(destination.Handle, "main", source.Handle, "main");
        if (backup is null || backup.IsInvalid)
            throw new SqliteException(
                $"backup init failed: {raw.sqlite3_errmsg(destination.Handle).utf8_to_string()}",
                raw.sqlite3_errcode(destination.Handle));

        int busyRetries = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            int rc = raw.sqlite3_backup_step(backup, BackupPagesPerStep);
            int total = raw.sqlite3_backup_pagecount(backup);
            if (total > 0)
                percent((int)(100L * (total - raw.sqlite3_backup_remaining(backup)) / total));
            if (rc == raw.SQLITE_DONE)
                return;
            if (rc == raw.SQLITE_OK)
            {
                busyRetries = 0;
                continue;
            }
            if ((rc is raw.SQLITE_BUSY or raw.SQLITE_LOCKED) && ++busyRetries <= MaxBusyRetries)
            {
                // Cancel-aware nap: a fired token wakes immediately and the loop-top throw exits.
                cancel.WaitHandle.WaitOne(RetrySleepMs);
                continue;
            }
            throw new SqliteException($"backup step failed (rc={rc})", rc);
        }
    }

    /// <summary>~2 MB per chunk at TS's 4 KB page size — small enough for smooth percents + prompt cancel,
    /// large enough that chunking adds no measurable overhead.</summary>
    private const int BackupPagesPerStep = 512;

    // One "2 s of patience" (review N5 — was magic twins): the busy-timeout pragma and the step-retry
    // loop derive from the same pair, so a future tuning changes one constant.
    private const int BusyTimeoutMs = 2000;
    private const int RetrySleepMs = 50;
    private const int MaxBusyRetries = BusyTimeoutMs / RetrySleepMs;

    private static void SweepPullTmp(string tmp)
    {
        File.Delete(tmp);
        File.Delete(tmp + "-journal");
        File.Delete(tmp + "-wal");
        File.Delete(tmp + "-shm");
    }

    /// <summary>Journals one verified manual field edit (the gate calls this after read-back verification).</summary>
    public void RecordEdit(TsTable table, string key, string column, object? value, string? old, string label) =>
        Journal.Append(TsEditKind.Manual, table, key, column, value, old, label);

    /// <summary>Journals one verified row creation (the gate's insert path calls this after the local insert
    /// landed and verified). <paramref name="key"/> is the row's key in its table's own key space;
    /// <paramref name="rowGuid"/> is the minted cross-copy name the push replays the insert under.</summary>
    public void RecordInsert(TsTable table, string key, string payloadJson, string rowGuid, string label) =>
        Journal.AppendInsert(table, key, payloadJson, rowGuid, label);

    /// <summary>Journals one verified write-back column stamp (the post-load write-back step calls this).
    /// An <c>acquired</c>/<c>accepted</c> stamp also masks the plan's inbound actuals: disk supersedes the
    /// rig's totals, so the row's mark reads → (never ⇄, never a stale ← after push). A desired ratchet does
    /// not mask — a rig-side goal change stays a visible inbound fact.</summary>
    public void RecordWriteBack(string tsPlanKey, string column, object? value, string? old, string label)
    {
        Journal.Append(TsEditKind.WriteBack, TsTable.ExposurePlan, tsPlanKey, column, value, old, label);
        if (column.Equals("acquired", StringComparison.OrdinalIgnoreCase)
            || column.Equals("accepted", StringComparison.OrdinalIgnoreCase))
            Inbound.MaskPlanActuals(tsPlanKey);
    }

    /// <summary>Opens the write-back applier on the local db (the post-load stamping step).</summary>
    public ITsWriteBackApplier CreateLocalWriteBackApplier() => _applierFactory(LocalPath);

    /// <summary>The user's deliberate Discard bookkeeping: drop every unpushed journal entry. Call only
    /// AFTER the discarding pull landed — the swap already physically replaced the discarded values, so
    /// this clears the journal that described them. The baseline stays: that pull just recorded it.
    /// (Pull-first removed the old ordering's crash window — discard-then-crash-before-pull could strand
    /// the discarded values behind a matching baseline; now a crash before this call just leaves a dirty
    /// journal over the fresh copy, and the next open re-prompts.)</summary>
    public void Discard()
    {
        Log.Info($"DISCARD {Journal.Count} unpushed journal entries (the discarding pull landed)");
        Journal.Clear();
    }

    /// <summary>Shapes the collapsed journal + the probe's facts into the push/open-with-dirty review.</summary>
    public PushReview PreparePush(TsDbStat? probe)
    {
        List<TsJournalEntry> collapsed = Journal.Collapse();
        // The creates section: each insert entry named by its entity identity with the human-meaningful
        // payload values (guid/profile/parent references are correlation plumbing, not review content).
        List<PushReviewCreateLine> creates = [.. collapsed
            .Where(e => e.Kind == TsEditKind.Insert)
            .Select(e => new PushReviewCreateLine(e.Label,
                e.Table switch
                {
                    TsTable.Target => "target",
                    TsTable.ExposureTemplate => "template",
                    _ => "exposure plan",
                },
                SummarizeInsert(e)))];

        // Old/new render sentinel-aware (a plan exposure of −1 reads "template default", never a raw −1
        // that looks like an ID); the replayed VALUE stays canonical — display only.
        List<PushReviewFieldLine> manual = [.. collapsed
            .Where(e => e.Kind == TsEditKind.Manual)
            .Select(e => new PushReviewFieldLine(e.Label, e.Column,
                TsValueText.ForField(e.Table, e.Column, e.Old),
                TsValueText.ForField(e.Table, e.Column, FormatValue(e.Value)) ?? "null"))];

        List<PushReviewCountLine> writeBack = [];
        foreach (IGrouping<string, TsJournalEntry> plan in collapsed
            .Where(e => e.Kind == TsEditKind.WriteBack)
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            // One selection rule with the replay (CountEntry): the entry it returns IS the count —
            // unless it fell through to the desired-only case, where no count pair displays (a
            // desired-only raise means the counts already matched disk; showing one would be a phantom
            // acquired change).
            TsJournalEntry entry = CountEntry(plan);
            TsJournalEntry? count =
                entry.Column.Equals("desired", StringComparison.OrdinalIgnoreCase) ? null : entry;
            TsJournalEntry? desired = plan.FirstOrDefault(e =>
                string.Equals(e.Column, "desired", StringComparison.OrdinalIgnoreCase));
            long? newCount = count is null ? null : Convert.ToInt64(count.Value, CultureInfo.InvariantCulture);
            bool isDecrease = newCount is { } n && long.TryParse(count!.Old, out long oldCount) && n < oldCount;
            writeBack.Add(new PushReviewCountLine(
                entry.Label, count?.Old, newCount,
                desired?.Old, desired is null ? null : Convert.ToInt64(desired.Value, CultureInfo.InvariantCulture),
                isDecrease));
        }
        writeBack = [.. writeBack
            .OrderByDescending(l => l.IsDecrease)   // decreases first — the dangerous half
            .ThenBy(l => l.Label, StringComparer.OrdinalIgnoreCase)];

        return new PushReview(
            creates, writeBack, manual,
            RemoteChangedSinceBaseline: probe is not null && _state.Baseline is not null && !BaselineMatches(probe),
            RemoteBusy: probe?.HasSidecar == true,
            OldestEditAt: Journal.IsEmpty ? null : Journal.Entries.Min(e => e.At),
            CollapsedCount: collapsed.Count);
    }

    /// <summary>
    /// The push: probe (unreachable/busy refuse the whole push before any write), then replay the collapsed
    /// journal in three legs — inserts first (created rows land remotely by guid, targets before plans, with
    /// any journaled field edits on a created row FOLDED into its INSERT payload so the row lands with final
    /// values — a remote UPDATE keyed by the row's local id would miss, the ids diverge), then write-back
    /// plans through the library writer, then manual field edits in seq order through the guarded field
    /// editor (so an explicit later desired edit outranks the writer's ratchet). Per-entry failures (row
    /// gone, parent gone, verify mismatch) are reported loudly and their entries retained; a fully applied
    /// push clears the journal and ends in a fresh pull that re-records the baseline.
    /// </summary>
    public PushResult Push(IProgress<int>? pullProgress = null, CancellationToken pullCancel = default)
    {
        List<TsJournalEntry> collapsed = Journal.Collapse();
        if (collapsed.Count == 0)
            return new PushResult(PushOutcome.NothingToPush, 0, [], PulledFresh: false);

        if (ProbePushPreconditions() is { } refused)
            return refused;

        // Partition around the inserts: field entries addressing a created row fold into its INSERT; the
        // remaining entries replay through the classic legs untouched.
        List<TsJournalEntry> inserts = [.. collapsed
            .Where(e => e.Kind == TsEditKind.Insert)
            // A created plan's references must exist first: templates, then targets, then plans.
            .OrderBy(e => e.Table switch { TsTable.ExposureTemplate => 0, TsTable.Target => 1, _ => 2 })
            .ThenBy(e => e.Seq)];
        HashSet<string> insertRowKeys = new(inserts.Select(e => RowKey(e.Table, e.Key)), StringComparer.OrdinalIgnoreCase);
        ILookup<string, TsJournalEntry> folded = collapsed
            .Where(e => e.Kind != TsEditKind.Insert && insertRowKeys.Contains(RowKey(e.Table, e.Key)))
            .ToLookup(e => RowKey(e.Table, e.Key), StringComparer.OrdinalIgnoreCase);
        List<TsJournalEntry> rest = [.. collapsed
            .Where(e => e.Kind != TsEditKind.Insert && !insertRowKeys.Contains(RowKey(e.Table, e.Key)))];

        PushReplayState state = new();
        (PushResult? insertRefusal, bool insertsApplied) = ReplayInsertLeg(inserts, folded, state);
        if (insertRefusal is not null)
            return insertRefusal;
        if (ReplayWriteBackLeg(rest, state, degradeStructural: insertsApplied) is { } structuralRefusal)
            return structuralRefusal;
        ReplayFieldLeg(rest, state);

        return CommitAndClose(collapsed, state, pullProgress, pullCancel);
    }

    /// <summary>One row's identity across kinds — the fold key tying field entries to their insert.</summary>
    private static string RowKey(TsTable table, string key) => $"{table}|{key}";

    /// <summary>Mutable state of one push replay — which seqs failed and why — passed to each leg so the
    /// orchestrator's flow stays linear and no closure captures accumulate invisibly.</summary>
    private sealed class PushReplayState
    {
        public HashSet<long> FailedSeqs { get; } = [];
        public List<PushFailure> Failures { get; } = [];

        /// <summary>Marks entries failed (deduped by seq) and logs each — the one failure-recording rule.</summary>
        public void Fail(IEnumerable<TsJournalEntry> entries, string detail)
        {
            foreach (TsJournalEntry e in entries)
            {
                if (!FailedSeqs.Add(e.Seq))
                    continue;
                Failures.Add(new PushFailure(e.Label, $"{e.Column}: {detail}"));
                Log.Error($"PUSH failed for \"{e.Label}\" {e.Table}.{e.Column}: {detail}");
            }
        }
    }

    /// <summary>Probe-time refusals: an unreachable or busy remote refuses the whole push before any write.</summary>
    private PushResult? ProbePushPreconditions()
    {
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
        return null;
    }

    /// <summary>The insert leg: replays each created row as a remote INSERT through the guarded primitive —
    /// parent references travel as guids (the journaled payload's form), so the remote resolves them against
    /// its own integer ids; the remote autoincrement mints the row's id and the guid is the correlation name.
    /// Folded field entries were already merged into the payload by the caller's partition; they succeed or
    /// fail with their insert. Non-null result = a whole-db refusal met before anything applied (the push
    /// stops, nothing written); once a row landed, later trouble degrades to per-entry failures.</summary>
    private (PushResult? Refusal, bool AnyApplied) ReplayInsertLeg(
        List<TsJournalEntry> inserts, ILookup<string, TsJournalEntry> folded, PushReplayState state)
    {
        if (inserts.Count == 0)
            return (null, false);

        using ITsEditor editor = _editorFactory(RemotePath);
        bool anyApplied = false;
        foreach (TsJournalEntry ins in inserts)
        {
            List<TsJournalEntry> group = [ins, .. folded[RowKey(ins.Table, ins.Key)]];
            Dictionary<string, object?> payload = InsertPayload(ins);
            foreach (TsJournalEntry f in folded[RowKey(ins.Table, ins.Key)])
                payload[f.Column] = f.Value;   // the row lands with its final values — no UPDATE follows

            (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([new TsRowInsert(ins.Table, payload)]);
            if (refusal is RefusalReason.SchemaIncompatible or RefusalReason.ReadOnly or RefusalReason.OpenSidecar)
            {
                // Session-level guards are open-time facts, so this fires on the FIRST row — nothing applied
                // yet and the whole push can refuse honestly. Defensively: once a row landed, a whole-db
                // refusal degrades to not-attempted failures instead of claiming "nothing was written".
                if (!anyApplied)
                {
                    if (refusal == RefusalReason.OpenSidecar)
                    {
                        Log.Warn("PUSH refused — remote TS db has an open sidecar (NINA imaging?)");
                        return (new PushResult(PushOutcome.RefusedBusy, 0, [], PulledFresh: false), false);
                    }
                    return (RefusedStructurally($"remote TS db refused row creation: {refusal}"), false);
                }
                state.Fail(group, $"not attempted — push aborted on {refusal}");
                continue;
            }
            if (refusal != RefusalReason.None)
            {
                state.Fail(group, $"refused: {refusal}");
                continue;
            }
            if (outcome is { Applied: true } && outcome.Rows[0].Succeeded)
            {
                anyApplied = true;
                continue;
            }
            state.Fail(group, outcome is { Applied: false }
                ? $"parent row not found on BIRDWATCHER by guid ({outcome.Rows[0].UnresolvedParentColumn})"
                : "read-back did not verify");
        }
        return (null, anyApplied);
    }

    /// <summary>An insert entry's payload, re-hydrated from its journaled JSON to canonical value boxes. A
    /// malformed payload is our own journal's contract violation — throw (the push escapes with the journal
    /// intact), never guess.</summary>
    private static Dictionary<string, object?> InsertPayload(TsJournalEntry entry)
    {
        if (entry.Value is not string json || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"insert entry for \"{entry.Label}\" has no payload");
        Dictionary<string, JsonElement> raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? throw new InvalidOperationException($"insert entry for \"{entry.Label}\" has a null payload");
        Dictionary<string, object?> payload = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string column, JsonElement value) in raw)
            payload[column] = TsJournal.CanonicalizeValue(value);
        return payload;
    }

    // The creates section's summary: the payload's human-meaningful values, in payload order. Correlation
    // plumbing (guid, profile, parent references — guids or ids, either way not review content) stays out;
    // a template's long policy tail (moon avoidance, twilight, dither…) is cloned config, so its summary
    // keeps only the identity + capture values the creation is FOR.
    private static string SummarizeInsert(TsJournalEntry entry)
    {
        Dictionary<string, object?> payload = InsertPayload(entry);
        IEnumerable<KeyValuePair<string, object?>> shown;
        if (entry.Table == TsTable.ExposureTemplate)
        {
            string[] keep = ["name", "filtername", "gain", "offset", "bin", "defaultexposure"];
            shown = keep.Where(payload.ContainsKey).Select(k => new KeyValuePair<string, object?>(k, payload[k]));
        }
        else
        {
            string[] plumbing = ["guid", "profileId", "targetid", "exposureTemplateId", "projectid"];
            shown = payload.Where(kv => !plumbing.Contains(kv.Key, StringComparer.OrdinalIgnoreCase));
        }
        return string.Join(", ", shown
            .Select(kv => $"{kv.Key} {TsValueText.ForField(entry.Table, kv.Key, FormatValue(kv.Value)) ?? "null"}"));
    }

    /// <summary>The write-back leg: re-executes the write-back contract per journaled plan on the remote.
    /// Non-null = a whole-db structural refusal (nothing was written — the push stops); null = leg
    /// completed, with any per-plan failures recorded in <paramref name="state"/>. When the insert leg
    /// already applied rows (<paramref name="degradeStructural"/>), a structural refusal must not claim
    /// "nothing was written" — it degrades to failing every remaining entry as not-attempted and the push
    /// commits what landed.</summary>
    private PushResult? ReplayWriteBackLeg(List<TsJournalEntry> collapsed, PushReplayState state, bool degradeStructural)
    {
        List<TsJournalEntry> writeBack = [.. collapsed.Where(e => e.Kind == TsEditKind.WriteBack)];
        if (writeBack.Count == 0)
            return null;

        using ITsWriteBackApplier applier = _applierFactory(RemotePath);
        string? structural =
            !applier.HasRequiredColumns ? "remote TS db schema incompatible (exposureplan columns missing)" :
            applier.IsReadOnly ? "remote TS db file is read-only" :
            applier.HasOpenSidecar ? "remote TS db has an open sidecar (NINA imaging?)" : null;
        if (structural is not null)
        {
            if (degradeStructural)
            {
                state.Fail(collapsed.Where(e => !state.FailedSeqs.Contains(e.Seq)), $"not attempted — {structural}");
                return null;
            }
            if (applier.HasOpenSidecar)
            {
                Log.Warn("PUSH refused — remote TS db has an open sidecar (NINA imaging?)");
                return new PushResult(PushOutcome.RefusedBusy, 0, [], PulledFresh: false);
            }
            return RefusedStructurally(structural);
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
                state.Fail(plan, $"journaled write-back key '{plan.Key}' is not a TS plan id");
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
                state.Fail(plan, f.ActualAcquired < 0
                    ? "TS plan row no longer exists on BIRDWATCHER"
                    : $"verify failed: expected {f.Expected}, remote reads {f.ActualAcquired}/{f.ActualAccepted}");
            }
        }
        return null;
    }

    /// <summary>The field leg: per-field guarded replay in seq order, with the whole-db-refusal abort
    /// cascade (fail every remaining field as not-attempted instead of hammering a dead db).</summary>
    private void ReplayFieldLeg(List<TsJournalEntry> collapsed, PushReplayState state)
    {
        List<TsJournalEntry> fields = [.. collapsed.Where(e => e.Kind == TsEditKind.Manual)];
        if (fields.Count == 0)
            return;

        using ITsEditor editor = _editorFactory(RemotePath);
        foreach (TsJournalEntry e in fields)
        {
            if (state.FailedSeqs.Contains(e.Seq))
                continue;   // already failed by an aborting refusal below
            (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(e.Table, e.Key, e.Column, e.Value);
            if (refusal != RefusalReason.None)
            {
                state.Fail([e], $"refused: {refusal}");
                if (refusal is RefusalReason.SchemaIncompatible or RefusalReason.ReadOnly or RefusalReason.OpenSidecar)
                {
                    // A whole-db refusal fails every remaining field the same way — stop hammering.
                    foreach (TsJournalEntry rest in fields.Where(f => f.Seq > e.Seq && !state.FailedSeqs.Contains(f.Seq)))
                        state.Fail([rest], $"not attempted — push aborted on {refusal}");
                    break;
                }
            }
            else if (result is not { Succeeded: true })
            {
                state.Fail([e], result is { RowFound: false }
                    ? "row no longer exists on BIRDWATCHER"
                    : "read-back did not verify");
            }
        }
    }

    /// <summary>Seq-aware journal retention, then the outcome: partial failure / mid-push edits (closing
    /// pull skipped) / full success ending in the CONTAINED closing pull — the one place the baseline
    /// invariant is restored, and a pull fault is reported on the result, never thrown
    /// (<see cref="PushResult.ClosingPullFailed"/>).</summary>
    private PushResult CommitAndClose(
        List<TsJournalEntry> collapsed, PushReplayState state,
        IProgress<int>? pullProgress, CancellationToken pullCancel)
    {
        // Applied fields' entries drop (up to this push's snapshot), failed fields keep their raw Old
        // chain, and any edit journaled DURING the push survives untouched.
        int applied = collapsed.Count - state.FailedSeqs.Count;
        Journal.CommitPush(
            [.. collapsed.Where(e => !state.FailedSeqs.Contains(e.Seq)).Select(TsJournal.FieldKey)],
            collapsed[^1].Seq);

        if (state.Failures.Count > 0)
        {
            Log.Error($"PUSH partial: {applied}/{collapsed.Count} applied; {state.Failures.Count} FAILED and retained in the journal");
            return new PushResult(PushOutcome.PartialFailure, applied, state.Failures, PulledFresh: false);
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
        // The token cancels only this pull, never the replay above (push writes are never interrupted); a
        // cancelled or unreachable closing pull just means the next open pulls fresh.
        TsDbStat? post = ProbeRemote();
        bool pulledFresh = false;
        bool closingPullFailed = false;
        if (post is not null)
        {
            try
            {
                Pull(post, pullProgress, pullCancel);
                pulledFresh = true;
            }
            catch (OperationCanceledException)
            {
                Log.Warn("PUSH applied; closing pull cancelled — next open will pull fresh");
            }
            catch (Exception ex)
            {
                // The push is DONE by here — writes applied and verified, journal rewritten. A
                // closing-pull fault (network drop mid-backup, swap failure) must never masquerade as a
                // push failure: contain it, flag it, and let the baseline rule heal the convergence gap
                // (the push changed the remote mtime, so the next open pulls fresh).
                closingPullFailed = true;
                Log.Error("PUSH applied but the closing pull failed — next open will pull fresh", ex);
            }
        }
        else
        {
            Log.Warn("PUSH applied but BIRDWATCHER dropped before the closing pull — next open will pull fresh");
        }
        Log.Info($"PUSH applied {applied} field(s) to {RemotePath}");
        return new PushResult(PushOutcome.Success, applied, [], PulledFresh: pulledFresh,
            ClosingPullFailed: closingPullFailed);
    }

    private static PushResult RefusedStructurally(string detail)
    {
        Log.Error($"PUSH refused — {detail}");
        return new PushResult(PushOutcome.Refused, 0, [], PulledFresh: false, RefusalDetail: detail);
    }

    // A write-back plan group's count entry: acquired when journaled, else accepted, else desired (a
    // desired-only raise still carries the disk count — the ratchet only ever raises TO the count).
    // ONE rule, two consumers: the replay executes it and PreparePush's review displays it (deriving
    // "desired-only" from the returned column) — the review can never show what the replay won't do.
    private static TsJournalEntry CountEntry(IGrouping<string, TsJournalEntry> plan) =>
        plan.FirstOrDefault(e => string.Equals(e.Column, "acquired", StringComparison.OrdinalIgnoreCase))
        ?? plan.FirstOrDefault(e => string.Equals(e.Column, "accepted", StringComparison.OrdinalIgnoreCase))
        ?? plan.First();

    // A review line must show SOMETHING — the shared rule's null passes through as the literal "null".
    private static string FormatValue(object? value) => TsValueText.From(value) ?? "null";
}
