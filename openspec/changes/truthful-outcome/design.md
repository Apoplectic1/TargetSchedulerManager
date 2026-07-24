# truthful-outcome — design

## Context

Verified in the 2026-07-24 review cross-check (see proposal): the push flow can clear the journal and
then throw (closing pull), producing a "PUSH FAILED — edits stay journaled" report that is false in both
halves; and the Discard flow clears dirty state *before* the pull that gives the clearing its meaning,
so a cancelled pull strands the discarded values as clean-looking truth for the session.

Relevant current mechanics:
- `TsSync.Push` (`TsSync.cs:393–551`): replay legs → `Journal.CommitPush` (:493) → full-success path
  probes and `Pull(...)` (:517) catching **only** `OperationCanceledException`.
- `MainViewModel.PushAsync` catch (:578+) assumes any throw preceded the journal rewrite.
- `PrepareTsForLoadAsync` Discard case (:510–514): `Sync.Discard()` (journal + baseline cleared) →
  `TryPullAsync` → cancelled ⇒ `CancelledPullNote("edits discarded · ")` and the load proceeds on the
  old local copy.
- `Sync.Discard()` deliberately drops the baseline so a crash between discard and pull can't strand the
  discarded values behind a matching-baseline skip — a guard the new ordering makes unnecessary.

## Goals / Non-Goals

**Goals:**
- The user is never told a push failed when the remote writes landed; the journal's state and the
  report never contradict each other.
- Discard is atomic-with-its-pull from the user's viewpoint: either the pull lands and the edits are
  gone (physically replaced by the swap), or nothing changed and the edits are still visibly unpushed.

**Non-Goals:**
- Retrying the closing pull inside `Push` (the next open pulls fresh; no retry loops).
- Journal durability (review M2 — separate decision), `Push` decomposition (M1), the `TsFieldsEditor`
  double-commit race. All later items.
- Any change to the replay legs' failure semantics — partial failure, refusals, and retention are
  untouched.

## Decisions

### D1 — Contain every closing-pull failure inside `Push`; the outcome is still `Success`

The closing pull's `try` widens from `catch (OperationCanceledException)` to also
`catch (Exception ex)`: log `"PUSH applied but the closing pull failed — next open will pull fresh"`
(with the exception) and fall through to the same `Success` return the unreachable-remote branch uses.
Rationale: by that point the push **is** done — writes applied and verified, journal rewritten; the pull
is a convergence nicety whose absence the baseline rule already heals (remote mtime changed ⇒ next open
pulls). Cancellation keeps its own quieter log line.

`PushResult` gains `ClosingPullFailed` (bool, default false; per no-back-compat rule the record just
changes shape). `DescribePush` appends `" · closing pull failed — next open pulls fresh (see tsm.log)"`
when set. The cancelled case stays as today (not a failure — the user did it).

*Alternative considered*: re-order so `CommitPush` runs after the closing pull. Rejected — the journal
rewrite must happen exactly when the writes applied; deferring it couples journal truth to an unrelated
network operation and re-opens the inverse lie (writes applied, journal still claims dirty ⇒ re-push
replays onto a db that already has the values — harmless but false-dirty).

### D2 — `PushAsync`'s catch comment becomes true; wording sharpened

With D1, the remaining throw sources in `Push` all precede the journal rewrite (probe, applier
construction/execute, editor construction, `TrySetField` faults, and `CommitPush` itself — which, if it
throws, leaves the journal file still holding the entries, so "edits stay journaled" is again accurate:
absolute-value replay makes the re-push idempotent). The catch keeps its message, and its comment is
rewritten to say why it is now guaranteed ("every throw that can escape Push precedes the journal
rewrite — the closing pull is contained inside Push"). The open-with-dirty push path needs no change:
`Push` no longer throws for pull reasons.

### D3 — Discard reorders to pull-first; clearing keys off the pull landing

`PrepareTsForLoadAsync`'s Discard case becomes:

1. `TryPullAsync(probe)` — the ordinary cancellable pull, dirty state untouched.
2. **Landed** → `Sync.Discard()` (now journal-only, see below) → `"edits discarded · pulled fresh"`.
   The swap already physically replaced the discarded values; clearing the journal is bookkeeping.
3. **Cancelled** → `"discard not completed — unpushed edits kept"` — journal, baseline, badge, and
   marks all still intact; the session simply continues dirty (the user can re-attempt via Pull now or
   push instead).

`Sync.Discard()` shrinks to journal-clearing only. Its baseline-drop existed solely to protect the old
ordering's crash window (discard-then-crash-before-pull ⇒ matching baseline skips every future pull,
stranding the values). Pull-first removes that window: a crash after the pull landed but before
`Journal.Clear()` leaves a dirty journal over a fresh local db — the next open shows the dirty prompt
again, the user re-chooses discard, and clearing is instant. Truthful in every interleaving. The doc
comment on `Discard` is rewritten to state the new contract (call only after the discarding pull
landed).

*Known side effect (accepted)*: the discarding pull's inbound diff compares the old local (with the
discarded values) against the fresh copy, so discarded fields can surface as ← inbound marks for the
session — arguably informative ("BIRDWATCHER's value replaced yours"), unchanged from today's behavior.

### D4 — Tests pin the truth table

- `TsSyncTests`: closing pull throws (backup fails after replay) ⇒ `Success`, `ClosingPullFailed`,
  journal empty, remote writes applied; closing pull cancelled ⇒ `Success`, not flagged-failed (existing
  behavior, now asserted); replay-leg throw ⇒ journal intact (pins D2's premise).
- `MainViewModelTests` (busy-gate class or sibling): Discard + cancelled pull ⇒ journal still dirty,
  badge still shows unpushed, status says discard not completed; Discard + landed pull ⇒ journal empty.
  (The VM Discard test drives `PrepareTsForLoadAsync` via `LoadAsync` with stubbed sync hooks —
  reuse `SyncStubs`/`SyncTestEnv` fakes; if the pull path proves un-stubbable at VM level, pin the
  ordering at `TsSync` level and assert the VM strings separately.)

## Risks / Trade-offs

- [`Success` despite no closing pull means the grid shows pre-push local state until the next open]
  → same as today's unreachable-remote branch; the status line + badge say exactly that, and the local
  db already equals the remote for every journaled field.
- [Discard pull-first runs a pull while the journal is dirty — the very thing the dirty guard exists to
  prevent] → the guard's purpose is *unchosen* overwrites; here the user just chose discard. The prompt
  decision is the consent; the ordering change only moves when the bookkeeping happens.
- [Crash between discard-pull landing and `Journal.Clear()` re-prompts a discard the user already chose]
  → one extra click in a crash-recovery corner, in exchange for zero windows where state lies.
- [`PushResult` shape change ripples to every construction site] → record has 6 sites, all in
  `TsSync.cs`; default parameter keeps them one-word changes. No back-compat concerns (rule 15).

## Migration Plan

None — no persisted-state change. Clean rebuild.

## Open Questions

None blocking.
