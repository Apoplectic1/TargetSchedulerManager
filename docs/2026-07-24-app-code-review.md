# 2026-07-24 — App code review (tsmui, full `TargetSchedulerManager.App` sweep)

**Scope.** Every `.cs` under `TargetSchedulerManager.App` (31 files, ~300 KB) plus a structural pass over
`TargetSchedulerManager.App.Tests`. The sibling `..\Library` repo (`Astronomy.Catalog` etc.) was **not**
reviewed — it is a separate repo and was not in scope for this pass. Review lenses, per request:
maintainability/AI-readability, DRY, performance/memory, modern C# idioms, concurrency.

**Overall verdict.** This is an unusually healthy codebase. Intent-carrying XML docs on nearly every public
member, invariants written down where they are enforced, sealed-hierarchy outcomes (`EditOutcome`),
test seams (`ITsEditor`, `ITsWriteBackApplier`, `SyncStubs`) at exactly the right boundaries, and modern
idioms already in use (collection expressions, `System.Threading.Lock`, records, primary constructors,
pattern matching). The findings below are prioritized; only one is a genuine correctness risk.

---

## Critical

### C1 — `RunVisibleTonightAsync` runs outside the `IsLoading` mutual exclusion it depends on

`MainViewModel.cs` (~line 331). `LoadAsync` and `PushAsync` both use the documented convention —
*"IsLoading doubles as the load/push mutual exclusion (both check-and-set synchronously on the UI
thread)"* — by **checking and setting** `IsLoading`. The Visible-tonight pass only checks:

```csharp
public async Task RunVisibleTonightAsync(TimeSpan minDuration, double horizonAltitudeDeg)
{
    if (_isLoading)
        return;                    // checks…
    // …but never sets IsLoading = true
    foreach (VisibleTonightEdit edit in plan.Edits)
    {
        EditOutcome outcome = await _gate.ApplyAsync(...);   // UI thread yields between edits
        ...
    }
```

Because the method awaits between edits, the UI stays live for the whole pass. Consequences:

* **Reload/Pull-now mid-pass** — `Reload_Click` starts `LoadAsync` (its `IsLoading` check passes), so the
  write-back step stamps the local db on a worker thread while gate edits are still applying on other
  worker threads. That interleaves two writers on the local db mid-pass — exactly the situation the
  "single writer per db" invariant (CLAUDE.md → *Single writer + WAL*) exists to prevent. SQLite's
  busy-timeout will usually serialize the file writes, but the load can resolve a half-flipped snapshot,
  and write-back can journal stamps computed against rows the pass is concurrently flipping.
* **Double-click** — a second Visible-tonight press starts a second concurrent pass over the same plan
  (`_isLoading` is still false), double-journaling flips.
* **Push mid-pass** — `PushAsync` can start replaying a journal the pass is still appending to
  (`TsJournal` handles this safely seq-wise, but the push review the user confirms no longer matches
  what gets pushed).

**Fix.** Route every long-running, edit-capable operation through one busy gate. A small scope helper
keeps the check-and-set atomic-on-UI-thread and impossible to forget on the next command:

```csharp
// MainViewModel — one gate for load / push / visible-tonight (any future bulk command joins it).
private bool TryBeginBusy()
{
    if (IsLoading) return false;   // UI thread: check-and-set is atomic by construction
    IsLoading = true;
    return true;
}

private void EndBusy()
{
    IsLoading = false;
    RaiseSyncState();
}

public async Task RunVisibleTonightAsync(TimeSpan minDuration, double horizonAltitudeDeg)
{
    if (!TryBeginBusy()) return;
    try
    {
        if (_lastLoad is not LoadResult load) { StatusText = "no load yet — nothing to reconcile"; return; }
        VisibleTonightPlan plan = PlanVisibleTonight(load, minDuration, horizonAltitudeDeg);   // extracted, see below
        int failed = await ApplyVisibleTonightEditsAsync(plan);                                // extracted, see M5
        ReportVisibleTonight(plan, failed);
    }
    finally
    {
        EndBusy();
    }
    if (/* plan applied any edits */ _reloadAfterBusy)
        await LoadAsync(PullPolicy.Never);   // after EndBusy, so the reload's own gate engages normally
}
```

(`LoadAsync`/`PushAsync` switch their manual `if (IsLoading) return; IsLoading = true;` to the same
`TryBeginBusy()`; the trailing reload stays outside the scope exactly as `PushAsync` already sequences its
post-push `LoadAsync`.) This closes all three interleavings with no new locking — the invariant stays
"UI thread serializes", it just becomes structurally enforced.

---

## Moderate

### M1 — `TsSync.Push` is a ~160-line monolith with two replay legs and closure-based state

`TsSync.cs` lines 393–551. `Push` currently holds: the probe/refusal gate, the write-back replay leg,
the field replay leg (with its own abort cascade), journal retention, and the closing-pull decision —
plus two local functions (`Fail`, `RefusedStructurally`) mutating captured state (`failedSeqs`,
`failures`). Every one of those is individually well-commented, but a reader (human or LLM) must hold
the whole method to know which leg mutated what. This is the file most likely to be edited under
pressure (push bugs are the scary ones), so it benefits most from single-purpose helpers.

**Fix — one orchestrator, one type carrying the replay state, one method per leg:**

```csharp
/// <summary>Mutable state of one push replay: which seqs failed and why. Passed to each leg so the
/// orchestrator's flow stays linear and no closure captures accumulate invisibly.</summary>
private sealed class PushReplayState
{
    public HashSet<long> FailedSeqs { get; } = [];
    public List<PushFailure> Failures { get; } = [];

    public void Fail(IEnumerable<TsJournalEntry> entries, string detail)
    {
        foreach (TsJournalEntry e in entries)
        {
            if (!FailedSeqs.Add(e.Seq)) continue;
            Failures.Add(new PushFailure(e.Label, $"{e.Column}: {detail}"));
            Log.Error($"PUSH failed for \"{e.Label}\" {e.Table}.{e.Column}: {detail}");
        }
    }
}

public PushResult Push(IProgress<int>? pullProgress = null, CancellationToken pullCancel = default)
{
    List<TsJournalEntry> collapsed = Journal.Collapse();
    if (collapsed.Count == 0)
        return new PushResult(PushOutcome.NothingToPush, 0, [], PulledFresh: false);

    if (ProbePushPreconditions() is PushResult refused)
        return refused;

    PushReplayState state = new();
    PushResult? structuralRefusal = ReplayWriteBackLeg(collapsed, state);
    if (structuralRefusal is not null)
        return structuralRefusal;
    ReplayFieldLeg(collapsed, state);

    return CommitAndClose(collapsed, state, pullProgress, pullCancel);
}

/// <summary>Probe-time refusals: unreachable or busy remote refuses the whole push before any write.</summary>
private PushResult? ProbePushPreconditions() { /* the probe + HasSidecar block, verbatim */ }

/// <summary>Re-executes the write-back contract per journaled plan on the remote; null = leg completed
/// (possibly with per-plan failures recorded), non-null = whole-db structural refusal.</summary>
private PushResult? ReplayWriteBackLeg(List<TsJournalEntry> collapsed, PushReplayState state) { ... }

/// <summary>Per-field guarded replay in seq order, with the whole-db-refusal abort cascade.</summary>
private void ReplayFieldLeg(List<TsJournalEntry> collapsed, PushReplayState state) { ... }

/// <summary>Seq-aware journal retention, then the closing pull (full success only) — the one place the
/// baseline invariant is restored.</summary>
private PushResult CommitAndClose(
    List<TsJournalEntry> collapsed, PushReplayState state,
    IProgress<int>? pullProgress, CancellationToken pullCancel) { ... }
```

Behavior-preserving (the bodies move verbatim); each helper is now individually nameable in a diff,
a test failure, or an LLM context window. `TsSyncTests` should pass unchanged — that's the verification.

### M2 — `TsJournal.Append` durability is weaker than its contract states; file I/O runs inside the lock the UI thread takes

`TsJournal.cs` lines 75–85. The doc comment promises *"persisted (flushed line) before the entry is
visible in memory"*, and the class doc says appends land on disk before visibility. `File.AppendAllText`
flushes the .NET stream but does **not** force the OS write-through — after a power loss (the exact
crash class the journal exists for), the tail line can be lost even though the entry was "visible" and
the local db write it records survived. The `Load` path already tolerates a torn line loudly, so the
window is small, but the contract and the code should agree — either soften the doc, or make the write
honest:

```csharp
public TsJournalEntry Append(...)
{
    lock (_lock)
    {
        TsJournalEntry entry = new(_nextSeq++, kind, table, key, column, Canonicalize(value), old, label, DateTimeOffset.Now);
        string line = JsonSerializer.Serialize(entry, Options) + Environment.NewLine;
        using (FileStream fs = new(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            fs.Write(Encoding.UTF8.GetBytes(line));
            fs.Flush(flushToDisk: true);   // the actual guarantee the doc claims
        }
        _entries.Add(entry);
        return entry;
    }
}
```

Second half of the same finding: every `Append`/`ReplaceAllLocked` does disk I/O while holding `_lock`,
and the **UI thread** takes that same lock via `SyncBadgeText → Journal.Collapse()` and `IsEmpty`. A
slow disk moment during a write-back burst (dozens of appends) can stall badge reads — i.e., the UI
thread blocks on another thread's file I/O. With `Flush(true)` this gets slightly worse per append.
Cheap containment that keeps the coarse-lock design: maintain the collapsed count as a field updated
inside the lock, so the UI-thread reads never contend on I/O-length critical sections (see N2 for the
`SyncBadgeText` half).

### M3 — `ReconciliationRow`'s 24-parameter positional constructor, echoed three times in `BuildRows`

`ReconciliationRow.cs` lines 18–47; `ReconciliationLoader.cs` lines 196–263 (`BothRow`/`TsRow`/`DiskRow`
plus the two inline `new ReconciliationRow(...)` sites). Twenty-four positional parameters — including
adjacent runs of same-typed values (`int planSeconds, int diskSeconds`, `int? desired, int? acquired,
int? accepted, int disk, int planCount`) — is the single riskiest shape in the app for both humans and
LLMs: a transposed pair compiles clean and renders as a subtly wrong grid. The call sites mitigate with
named arguments, but only partially (`c.Desired, c.Acquired, c.Accepted, c.Disk, c.PlanCount` is
positional). The three local row factories also re-thread the same eight identity/panel/key arguments
through every call.

**Fix — group the parameters into cohesive records; identity flows once.** This also collapses the
duplication between the three factories:

```csharp
/// <summary>The target/panel/TS-key identity shared by every row of one emit — built once per
/// EmitRows call instead of re-threaded through 24-parameter calls.</summary>
public sealed record RowIdentity(
    string Target, string Project, RowSource Source,
    string? PanelKey, string? PanelLabel, RowSource? PanelSource,
    bool Enabled, string? TsTargetKey, Guid TargetId, string? ProjectTsKey);

/// <summary>One row's plan/disk numbers — the columns, in column order.</summary>
public sealed record RowNumbers(
    int PlanSeconds, int DiskSeconds, int? Desired, int? Acquired, int? Accepted,
    int Disk, int PlanCount, double? PlanHours, double? DiskHours);

public sealed class ReconciliationRow(
    RowIdentity id, string filter, string purpose, RowPlane plane, RowNumbers numbers,
    string badge, bool isFlagged,
    bool secondsMixed = false, bool isDetail = false,
    IReadOnlyList<ReconciliationRow>? detail = null,
    string? planTsKey = null, bool? planEnabled = null) : INotifyPropertyChanged
{
    public string Target { get; } = id.Target;
    // …existing property surface unchanged, so XAML bindings and tests keep compiling.
}
```

```csharp
// BuildRows: identity is constructed once per EmitRows and the factories shrink to their real content.
RowIdentity id = new(groupName, project, source, panelKey, panelLabel, panelSource,
                     tc.Enabled, tc.TsTargetKey, tc.TargetId, tc.ProjectTsKey);

ReconciliationRow TsRow(ReconciliationCell c, bool isDetail) => new(
    id, c.Filter, c.Purpose.ToString(), RowPlane.Ts,
    new RowNumbers(c.Seconds, 0, c.Desired, c.Acquired, c.Accepted, 0, c.PlanCount,
                   c.Seconds > 0 ? c.Desired * c.Seconds / 3600.0 : null, null),
    badge, flagged, isDetail: isDetail, planTsKey: c.PlanTsKey, planEnabled: c.PlanEnabled);
```

The public property surface is unchanged, so this is mechanical; `BuildRowsTests` verifies it.

### M4 — `MainViewModel` carries six concerns in one ~990-line class

`MainViewModel.cs`. The class is well-sectioned internally, but it now owns: (1) the filter/group/sort
pipeline, (2) sync orchestration UI (`PrepareTsForLoadAsync`, pull UI, push), (3) the ambiguity-report
surface, (4) the Visible-tonight command, (5) the template picker surface, (6) the marks sweep. Each is
individually clean; together they make the file the largest context-window burden in the repo, and every
new command lands here by gravity (Visible-tonight already did — see C1 for what that cost).

**Fix — partial-class split first (zero-risk), extraction second (when a section grows again).** The
lowest-cost move that pays immediately for both humans and LLMs:

```
ViewModels\MainViewModel.cs           // state, ctor, INPC plumbing, filter pipeline (the core)
ViewModels\MainViewModel.Sync.cs      // PrepareTsForLoadAsync, WithPullUiAsync, PushAsync, badge text
ViewModels\MainViewModel.Edits.cs     // Set*Async, ApplyOutcome, RecomputeOwners, marks sweep
ViewModels\MainViewModel.Reports.cs   // ambiguity surface, templates surface, visible-tonight command
```

Same type, no behavior change, no binding churn — but a task like "fix the push flow" now loads a
300-line file instead of a 990-line one. If the template/ambiguity surfaces grow, promote them to
injected collaborator classes (`TemplateSurface`, `AmbiguitySurface`) mirroring how `TsEditGate` was
extracted; the constructor seam for tests already exists.

### M5 — Visible-tonight replays N edits as N `Task.Run` hops, each opening a fresh editor

`MainViewModel.RunVisibleTonightAsync` + `TsEditGate.ApplyAsync`. Each flip round-trips
UI-thread → thread-pool → **new SQLite connection** (`_editorFactory` per call) → dispose → UI thread.
A pass that flips 80 targets/projects performs 80 connection opens and 160 context switches, and the
UI-thread hops between awaits are what open the C1 interleaving window in the first place. Batch the
whole pass into one worker invocation with one editor:

```csharp
// TsEditGate — the batch counterpart of ApplyAsync: one worker, one connection, per-edit outcomes.
/// <summary>Applies many field edits in one editor session, off the UI thread; each verified write
/// journals individually (same contract as ApplyAsync, N times cheaper).</summary>
public Task<IReadOnlyList<EditOutcome>> ApplyManyAsync(
    IReadOnlyList<(TsTable Table, string Key, string Column, object? Value, string Label)> edits) =>
    Task.Run<IReadOnlyList<EditOutcome>>(() =>
    {
        List<EditOutcome> outcomes = new(edits.Count);
        using ITsEditor editor = _editorFactory(_sync.LocalPath);
        foreach ((TsTable table, string key, string column, object? value, string label) in edits)
            outcomes.Add(ApplyOne(editor, table, key, column, value, label));   // body of today's ApplyAsync try-block
        return outcomes;
    });
```

`ApplyAsync` becomes `ApplyManyAsync([one])` — one code path, exercised by the existing
`TsEditGateTests`. The visible-tonight loop collapses to a single await, which also makes C1's busy
scope airtight (no UI-thread re-entry between edits at all).

### M6 — Duplicated selection/equality rules inside `TsSync`

Two small DRY slips in the same file, both in invariant-bearing code:

1. **Count-entry selection.** `PreparePush` (lines 361–363) inlines
   `acquired ?? accepted` while `CountEntry` (lines 555–558) implements
   `acquired ?? accepted ?? First()`. Today the review and the replay agree only because `PreparePush`
   independently handles the desired-only case; the next column added to write-back has to be fixed in
   two places or the review shows something the replay doesn't do.
2. **Baseline equality.** `ShouldPull` (lines 141–145) and `PreparePush`'s
   `RemoteChangedSinceBaseline` (lines 379–380) each spell out
   `b.RemoteLength != probe.Length || b.RemoteLastWriteUtc != probe.LastWriteUtc`.

```csharp
/// <summary>The one definition of "the baseline still matches this probe" — used by the skip rule
/// and (negated) by the push review's staleness warning.</summary>
private bool BaselineMatches(TsDbStat probe) =>
    _state.Baseline is { } b
    && b.RemoteLength == probe.Length
    && b.RemoteLastWriteUtc == probe.LastWriteUtc;

public bool ShouldPull(TsDbStat probe) => probe.HasSidecar || !BaselineMatches(probe);
// PreparePush: RemoteChangedSinceBaseline: probe is not null && !BaselineMatches(probe)
```

For (1), have `PreparePush` call `CountEntry(plan)` and derive "this group is desired-only" from
`count.Column == "desired"` rather than re-querying the group — one selection rule, two consumers.

### M7 — The edit-flyout commit router is an inline lambda of stringly special cases; the schema clamp is written twice

`MainWindow.ShowEditFlyoutAsync` (lines 337–407) embeds the routing of four special columns
(`enabled`, `active`, `desired`, `exposure`) to their mirror-aware setters inside a closure that also
captures the pair-warn state. Meanwhile `TsFieldsEditor` duplicates the Min/Max/Whole clamp verbatim in
`BuildNumber` (lines 152–155) and `BuildSentinelNumber` (lines 250–253), and `BuildSentinelNumber`
itself is ~100 lines of interleaved checkbox/box event logic.

```csharp
// MainWindow — the routing table as a named method: one place that says "these columns have in-grid mirrors".
private Task<bool> CommitTsFieldAsync(
    TsTable table, string key, string title, TargetGroupRow? group, ReconciliationRow? row,
    string column, object? value) => (row, group, column.ToLowerInvariant()) switch
{
    ({ } r, _, "enabled") => ViewModel.SetPlanEnabledAsync(r, Convert.ToInt64(value) != 0),
    (_, { } g, "active")  => ViewModel.SetTargetEnabledAsync(g, Convert.ToInt64(value) != 0),
    ({ } r, _, "desired") => ViewModel.SetPlanDesiredAsync(r, Convert.ToInt32(value)),
    ({ } r, _, "exposure") => CommitExposureAsync(r, value),
    _ => ViewModel.SetTsFieldAsync(table, key, column, value, title),
};
```

```csharp
// TsFieldsEditor — the clamp, once:
private static double ClampToSchema(TsField field, double wanted)
{
    if (field.Min is double min && wanted < min) wanted = min;
    if (field.Max is double max && wanted > max) wanted = max;
    return field.Type == TsFieldType.Whole ? Math.Round(wanted) : wanted;
}
```

For `BuildSentinelNumber`, extract a small private `SentinelCell` class holding the checkbox, box, and
`effective` state as fields, with `OnUseDefaultChecked` / `OnValueConfirmed` as named methods — the
three event lambdas become one-liners delegating to it, and the sentinel rules become unit-nameable.

---

## Minor / Nitpick

### N1 — `ApplyFilters` re-trims the search text once per row

`MainViewModel.cs` line 904: `q = q.Where(r => r.Matches(_searchText.Trim()))` — the lambda runs per
row, so `Trim()` allocates per row per keystroke. Hoist it:

```csharp
if (!string.IsNullOrWhiteSpace(_searchText))
{
    string needle = _searchText.Trim();
    q = q.Where(r => r.Matches(needle));
}
```

Relatedly, the full rebuild (group + sort + `ObservableCollection` replacement + marks sweep) runs on
every keystroke. At a few hundred rows this is fine; if the library grows, a ~150 ms debounce on the
`SearchText` setter is the first knob, not a smarter pipeline.

### N2 — `SyncBadgeText` runs `Journal.Collapse()` on the UI thread on every sync-state raise

`MainViewModel.cs` line 140. `Collapse()` takes the journal lock, builds two dictionaries and a sorted
list — per badge refresh, on the UI thread, and `RaiseSyncState` fires after every applied edit (so a
write-back burst does it dozens of times). Cache the collapsed count in `TsJournal` (update it inside
the lock on `Append`/`ReplaceAllLocked`) and expose `int CollapsedCount { get; }` — the badge read
becomes two field reads, and the UI thread stops contending on the journal lock during bursts (M2's
second half).

### N3 — Fire-and-forget `_ =` discards swallow unexpected exceptions

`MainWindow.xaml.cs` (`Reload_Click`, `Push_Click`, `EditMenuItem`, flyout openers, the `Loaded`
handler). The awaited VM methods catch their own failures, but anything thrown *outside* those guards
(a resource lookup in flyout construction, a bug in dialog code) dies as an unobserved task exception —
invisible in tsm.log, the one place this project promises failures land. One helper aligns the
code-behind with the fail-loud doctrine:

```csharp
private static async void FireAndLog(Func<Task> action, string what)
{
    try { await action(); }
    catch (Exception ex) { Log.Error($"{what} failed unhandled", ex); }
}
// usage: private void Push_Click(object s, RoutedEventArgs e) => FireAndLog(ViewModel.PushAsync, "push");
```

### N4 — `FormatValue` exists twice

`TsSync.FormatValue` (line 560) and `SyncMarks.FormatValue` (line 152) are the same
invariant-`Convert.ToString` rule (they differ only in the null spelling, `"null"` vs `null` — worth a
deliberate look at whether that difference is intended). One `TsValueText.From(object?)` helper in
`Shared\` makes the journal-value display convention single-sourced.

### N5 — Backup busy-retry constants are magic twins

`TsSync.BackupTo`: `PRAGMA busy_timeout = 2000` and the `40 × 50 ms` retry loop encode the same "2 s of
patience" independently (the comment says so). Derive one from the other
(`MaxBusyRetries = BusyTimeoutMs / RetrySleepMs`) so a future tuning changes one constant. Also, the
`Thread.Sleep(50)` doesn't observe `cancel` mid-sleep — worst-case 50 ms extra cancel latency; harmless,
but `cancel.WaitHandle.WaitOne(50)` gets both for free.

### N6 — `RecomputeOwners` scans all groups per inline edit

`MainViewModel.cs` line 822: `_groups.FirstOrDefault(g => g.Children.Contains(row))` is
O(groups × children) per committed Desired/exposure edit. Fine at current scale; if it ever shows up in
a trace, build a `Dictionary<ReconciliationRow, TargetGroupRow>` during `ApplyFilters` (which already
touches every row).

### N7 — Naming-convention drift in `DiagnosticsWindow`

`mId`, `mOwner`, `sCurrent` (ported TP conventions) vs. `_camelCase` everywhere else in the repo. Purely
cosmetic for humans; for LLM edits, mixed conventions in one repo measurably increase the chance a
generated diff invents the wrong style. Worth normalizing on the next touch, not as its own commit.
Same category: `MainWindow.xaml.cs` lines 74–77 duplicate the same two-line comment back-to-back.

### N8 — `TsDatabaseResolver.Stat` blocks a caller thread for the probe timeout

By design (documented: abandoned worker, hard timeout), and every call site wraps it in `Task.Run`. If
it ever needs to be await-friendly: `probe.WaitAsync(timeout)` (`TimeoutException` → null) keeps the
abandon semantics without a blocked thread-pool thread. Not worth changing until a caller wants
`ProbeRemoteAsync` directly.

### N9 — `AmbiguityReport.Build` is a ~170-line straight line

It reads top-to-bottom well and is pure, so this is the mildest of the monolith findings — but the
section builders are already natural seams. `BuildIdentitySection(report, held)`,
`BuildDuplicateSection(...)`, `BuildPlanSection(...)`, `BuildTemplateSection(graph)` returning
`List<string>` each would let the assembly read as a table of contents, and each check becomes testable
without composing the whole markdown. `PlannedOnlyTwins`' O(n²) pair scan is fine at planned-only-target
counts; no action.

### N10 — Idiom polish (already strong; these are the leftovers)

* `TsEditGate`, `VisibleRowTree`, `SyncMarks` still use explicit constructor-assignment where a primary
  constructor would match the file's own style (`TsEditGate(TsSync sync, Func<string, ITsEditor> editorFactory)`
  → `class TsEditGate(TsSync sync, Func<string, ITsEditor> editorFactory)`).
* `ReconciliationRow`/`AggregateHeaderRow` INPC backing fields (`_markGlyph`, `_isExpanded`) are
  candidates for the C# `field` keyword once the team adopts it — removes six backing-field
  declarations without changing semantics.
* `ScanLibraryAsync` correctly uses `ConfigureAwait(false)`; the view-model deliberately does not
  (it needs the UI context back) — correct on both sides, worth a one-line comment on the VM side so a
  future "add ConfigureAwait everywhere" sweep doesn't break `Progress`/property raises.
* Performance posture overall is healthy: hot paths are I/O-bound (SMB stat, SQLite backup, disk scan),
  per-load allocations are proportional to row count, and there is no boxing-in-a-loop or
  string-concat-in-a-loop pattern worth a `Span<T>` conversion today. `NaturalComparer` is already
  allocation-free. The only allocation churn worth watching is N1/N2.

---

## Concurrency summary (requested lens, consolidated)

The documented model — *UI thread serializes commands; workers do I/O; `TsJournal`/`TsInboundStore` are
the only cross-thread mutables and are coarsely locked* — is sound and mostly enforced. The three
places the enforcement is by-convention rather than by-construction:

1. **C1** — Visible-tonight doesn't take the busy gate (the one real hole; fix above).
2. `TsSync.LastProbe`/`HasProbed` are written from `Task.Run` workers and read from the UI thread
   without synchronization. Safe under the "one command at a time" contract (reference/bool writes,
   happens-before via the awaited task), but that contract is exactly what C1 shows can slip — after
   the C1 fix this is fine as-is; worth one doc line on the properties.
3. `WithPullUiAsync`'s `_pullCts` handoff (`Cancel` from UI, dispose at scope exit) is safe only
   because both run on the UI thread — also worth saying in its comment, since it looks racy to a
   reviewer until traced.

No deadlock potential found (no lock-then-await, no sync-over-async on the UI thread —
`TsDatabaseResolver.Stat`'s blocking wait always runs on a worker).

## Suggested order of attack

1. **C1 + M5 together** (one busy gate + `ApplyManyAsync`) — closes the correctness hole and makes the
   pass faster; `TsEditGateTests`/`MainViewModelTests` cover the seams.
2. **M6** (baseline/count-entry helpers) — 20 minutes, removes two invariant-drift traps in the push path.
3. **M1** (`Push` decomposition) — mechanical, behavior-preserving, `TsSyncTests` green = done.
4. **M3** (row parameter objects) — mechanical but wide; do it in one sitting with `BuildRowsTests` open.
5. **M2, M4, M7** as the files are next touched; **N-items** opportunistically.
