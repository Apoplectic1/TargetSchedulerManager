# 2026-07-24 — Re-check of the app code review (verification pass)

**Scope.** Verification of every finding from `2026-07-24-app-code-review.md` against the updated sources
(all files touched today after the review, including the new `MainViewModel.*.cs` partials,
`CommitChain.cs`, `TsValueText.cs`, and the new/updated test files). Each item was re-read in the new
code, not assumed from commit intent.

**Verdict up front: 14 of 18 findings fully resolved, 1 resolved by a documented engineering decision,
3 deliberately deferred exactly as the review recommended. The critical concurrency hole (C1) is closed
and regression-tested. No new defects were introduced by the fixes — and the closing-pull containment
work actually fixed a latent bug the original review under-called.** The remaining open items are all
cosmetic-to-minor and listed at the end.

---

## Critical

### C1 — Visible-tonight outside the busy exclusion → **FIXED, and hardened beyond the recommendation**

`RunVisibleTonightAsync` now acquires the same gate as load and push (`TryBeginBusy()` /
`EndBusy()` in `MainViewModel.cs`), and the fix goes further than the review asked:

* **One gate, structurally enforced.** `TryBeginBusy`/`EndBusy` are the only writers of `IsLoading`;
  `LoadAsync`, `PushAsync`, and `RunVisibleTonightAsync` all route through them. A future bulk command
  can't forget the gate — it has to call it.
* **The reverse window is closed too.** The review only covered "bulk op starts while idle edits could
  arrive"; the fix also covers "bulk op starts while an *edit's worker still holds a db connection*"
  (`_gateWorkInFlight` + `WithEditInFlightAsync`), refusing the bulk operation loudly for that moment.
  That interleaving wasn't in the original review — good catch.
* **Edits are refused under busy at the funnel**, independent of UI disabling (`RefuseIfBusy` in
  `MainViewModel.Edits.cs` on every `Set*Async`), with `CanEdit` as the visible-feedback binding —
  invariant and feedback correctly separated.
* **The batch replay removes the re-entry seams entirely.** `TsEditGate.ApplyManyAsync` runs the whole
  pass on one worker with one editor session (M5), so there are no UI-thread hops mid-pass at all; the
  closing reload runs after `EndBusy` and takes the gate itself.
* **Regression-tested**: `MainViewModelBusyGateTests` covers check-and-set/release, every setter refused
  under busy (asserting zero editor opens and an empty journal), the in-flight-edit block with a blocking
  editor, exclusion held across the visible-tonight batch (second pass refused outright, one editor
  session), and gate release on the no-load early return.

One residual nuance, correctly handled: the early `return`s inside the busy scope release via `finally`,
and the definite-assignment of `plan`/`failed` is compiler-verified. No issues found.

## Moderate

### M1 — `TsSync.Push` monolith → **FIXED (decomposition as proposed), plus a real bug fix beyond it**

`Push` is now a linear orchestrator over `ProbePushPreconditions()` → `ReplayWriteBackLeg()` →
`ReplayFieldLeg()` → `CommitAndClose()`, with `PushReplayState` replacing the closure-captured
`failedSeqs`/`failures` — the exact shape the review sketched, bodies preserved.

The extra: `CommitAndClose` now **contains a non-cancellation closing-pull fault** and reports it as
`PushResult.ClosingPullFailed` instead of letting it throw. In the old code, a network drop during the
closing pull would escape `Push` *after* the journal rewrite — and `PushAsync`'s catch would tell the
user "edits stay journaled, re-push after fixing the cause," which at that point was false (the journal
was already cleared, the writes already applied). The original review flagged the closing pull only in
passing; this fix closes a genuine wrong-message path. `PushAsync`'s catch comment now states the
restored invariant precisely ("every throw that can escape Push precedes the journal rewrite"), and
`Push_ClosingPullFails_StillSuccess_JournalClearedAndFlagged`, `Push_ClosingPullCancelled_Success_NotFlaggedAsFailed`,
`Push_ReplayLegThrows_EscapesWithJournalIntact`, and `PushAsync_ClosingPullFailure_ReportsHonestSuccess`
pin all four quadrants.

### M2 — Journal durability contract + lock contention → **RESOLVED (documented boundary + N2 cache)**

The fix chose the *other* branch the review offered: instead of `Flush(flushToDisk: true)`, the class
doc now states the honest boundary — appends survive a **process** crash; an OS/power failure can lose
the final line; and no flush could make the SQLite commit and the journal append atomic with each other
anyway, so the loss mode is bounded to one entry's replay (the local db stays correct, and `Load`
already skips a torn line loudly). That reasoning is sound — arguably sounder than adding write-through,
which would have bought partial protection at per-append cost while the two-durability-events gap
remained. Contract and code now agree, which was the actual defect.

The UI-thread contention half is addressed via the `_fieldKeys` set and `CollapsedCount` (see N2);
`CollapsedCount_TracksDistinctFields_ThroughAppendCommitAndReload` covers append, commit-push retention,
clear, and reload paths.

### M3 — 24-parameter `ReconciliationRow` constructor → **FIXED as proposed**

`RowIdentity` (10 identity fields, built once per `EmitRows` and shared by every row of the emit) and
`RowNumbers` (the numeric columns, with a doc comment mandating named arguments — and every
`BuildRows` site does use them). The constructor is down to 12 parameters of distinct roles; the
transposition-hazard runs are gone; the public property surface is unchanged so bindings and tests
carried over. The three row factories (`BothRow`/`TsRow`/`DiskRow`) now read as their actual content.

### M4 — `MainViewModel` breadth → **FIXED as proposed**

Split into the exact four partials suggested: core (state, busy gate, filter pipeline, ~370 lines),
`.Sync.cs` (load/pull/push surface), `.Edits.cs` (Set*Async funnel + marks sweep), `.Reports.cs`
(ambiguity/templates/visible-tonight). Each part opens with a scope comment naming what lives where,
and the core's class doc records the layout — an LLM (or a human) landing in any part can orient
without loading the others.

### M5 — Per-edit editor opens in the visible-tonight replay → **FIXED as proposed**

`TsEditGate.ApplyManyAsync(IReadOnlyList<TsFieldEdit>)`: one worker invocation, one editor session,
outcomes aligned by index, per-edit fault isolation, and a defined editor-cannot-open semantic (every
unattempted edit fails loudly, nothing journaled). `ApplyAsync` is now the one-element case — one code
path, as recommended. `TsEditGateTests` grew the matching four tests
(`ApplyMany_OneEditorSession_OutcomesAlignByIndex`, `_ThrowingEdit_FailsOnlyItself`,
`_EditorCannotOpen_FailsEveryEdit_NothingJournaled`, `_OneElement_MatchesApplyAsync`).

### M6 — Duplicated selection/equality rules in `TsSync` → **FIXED as proposed**

`BaselineMatches(probe)` is now the one comparison, consumed by `ShouldPull` and (negated, behind its
own has-a-baseline guard) by `PreparePush` — and the comment explains the two consumers' opposite null
postures, which is exactly the kind of invariant note that prevents the next regression.
`PreparePush` now calls `CountEntry(plan)` and derives "desired-only" from the returned column, so the
review can no longer display what the replay wouldn't do. New tests pin the seam:
`PreparePush_DesiredOnlyRaise_ShowsNoPhantomCountChange`, `_AcquiredPlusDesiredGroup_KeepsTheCountPair`,
`_NoBaseline_MakesNoStalenessClaim`.

### M7 — Inline commit router + duplicated clamp → **FIXED (router + clamp); one sub-item deferred**

`TryCommitMirroredField` is the named routing table (null = fall through to the generic path, with the
pair-warn bookkeeping correctly left with its captures), `CommitExposureAsync` carries the sentinel
mirror rule, and `ClampToSchema` is the one clamp — with a comment explaining why the sentinel bypasses
it. Deferred: `BuildSentinelNumber` is still ~100 lines of interleaved checkbox/box event logic; the
suggested `SentinelCell` extraction remains open (minor — see "Still open" below).

**Bonus beyond the review:** `CommitChain` (new, `Shared\CommitChain.cs`) serializes async commits per
editing surface — the flyout form (`TsFieldsEditor._chain`) and the inline Desired boxes
(`MainWindow._desiredCommits`). This closes a race the original review missed: two rapid confirmations
could overlap write + read-back verify, and the first's verify could see the second's value and
spuriously revert a good edit. The implementation is correct for its stated UI-thread-confined contract
(tail-swap without locking, per-commit fault isolation via the swallowed `await prev`), and
`CommitChainTests` covers ordering, no-overlap, fault isolation, and per-caller results.

## Minor / Nitpick

* **N1** (per-row `Trim()`) — **fixed**; needle hoisted with a comment citing the review. Debounce not
  added, which matches the review's "only if the library grows" framing.
* **N2** (badge `Collapse()` on the UI thread) — **fixed**; `Journal.CollapsedCount` backed by a
  `_fieldKeys` set maintained under the lock; `SyncBadgeText` reads it.
* **N3** (fire-and-forget swallowing exceptions) — **fixed**; `FireAndLog` wraps every `_ =` site
  including menu items and the `Loaded` handler. (Remaining micro-hole: `TargetEnable_Click`,
  `PlanEnable_Click`, `Desired_Committed`, and the two flyout control lambdas are still plain
  `async void`; everything they await handles its own failures, so only a trivial exception in the
  revert lines themselves could escape. Not worth churn unless you want uniformity.)
* **N4** (duplicate `FormatValue`) — **fixed**; `TsValueText.From` with the null-spelling difference
  now *deliberate and documented* at both call sites (push review shows `"null"`, tooltip passes null
  through). The review had asked whether that difference was intended; the code now answers.
* **N5** (magic busy-retry twins; cancel-blind sleep) — **fixed**;
  `MaxBusyRetries = BusyTimeoutMs / RetrySleepMs`, pragma built from the constant, and
  `cancel.WaitHandle.WaitOne(RetrySleepMs)` for the cancel-aware nap.
* **N6** (`RecomputeOwners` linear scan) — **unchanged, correctly**: the review said defer until it
  shows in a trace.
* **N7** (naming drift, duplicated comment) — **fixed**; `DiagnosticsWindow` is `_camelCase`
  throughout, `MainWindow`'s duplicated comment removed.
* **N8** (`Stat` blocking wait) — **unchanged, correctly**: the review said leave until a caller wants
  `ProbeRemoteAsync`.
* **N9** (`AmbiguityReport.Build` length) — **unchanged**; still the mildest monolith, still worth the
  section-builder split on next touch.
* **N10** (idiom polish) — **mostly done**: `TsEditGate` and `VisibleRowTree` use primary constructors;
  `LoadAsync` now carries the explicit "deliberately NO ConfigureAwait(false) — don't fix this in a
  sweep" comment the review asked for. `SyncMarks` kept its explicit private constructor, which is the
  right call given its static-factory shape. `field` keyword not adopted (fine).

## New-defect sweep over the changed code

Checked each fix for regressions; all clean, two changes worth affirming as *improvements* rather than
risks:

1. **Discard reordered to pull-first** (`PrepareTsForLoadAsync` + `TsSync.Discard`). The old order
   (clear journal + baseline, then pull) had a crash window the old code compensated for by dropping
   the baseline; the new order makes the discard true only when the swap has physically replaced the
   values — a cancelled discard-pull now changes *nothing* (journal, baseline, badge, marks all
   intact), and a crash between pull and bookkeeping just re-prompts. This is strictly safer than both
   the old code and the review's snippet, and `Discard_RunsAfterThePull_SoNoInterruptedPullCanStrandLocalValues`
   plus `Discard_ClearsJournalOnly_BaselineStays` pin it.
2. **`CanPush` now includes `!_isLoading`** — raised correctly, since `TryBeginBusy`/`EndBusy` both call
   `RaiseSyncState()`.
3. `CommitChain._tail` holds only the latest task (no unbounded chain retention); UI-thread confinement
   is documented and matches every call site. The cross-row serialization of all Desired boxes through
   one chain is a deliberate, documented millisecond-scale cost.
4. `WithEditInFlightAsync`'s `++/--` are UI-thread-only and balanced in `finally`; the startup load
   cannot be spuriously refused.
5. `PushReplayState` is instantiated per push — no shared mutable state across pushes.

## Still open (all minor, none urgent)

1. **`BuildSentinelNumber` (~100 lines)** — the `SentinelCell` extraction from M7 remains the one
   maintainability item of any substance left; do it on the next touch of `TsFieldsEditor`.
2. **N9** — `AmbiguityReport.Build` section-builder split, on next touch.
3. **N6 / N8** — parked by design; revisit only on evidence.
4. Housekeeping: this re-check staged temporary copies of your sources into
   `_to_delete\tsm-recheck-staging-copies\` at the repo root (a workaround for a file-sync cache; the
   session can't delete files on your machine). Delete that folder at your convenience — nothing in it
   is needed.

## Bottom line

The fix pass is thorough and disciplined: every accepted finding was implemented at or beyond the
recommended shape, each fix carries a comment citing the review item (which keeps the audit trail
readable), two adjacent latent bugs the review didn't fully call (the closing-pull wrong-message path,
the rapid-commit verify race) were found and fixed along the way, and every behavioral change landed
with pinning tests (`MainViewModelBusyGateTests`, `CommitChainTests`, and the expanded
`TsSyncTests`/`TsJournalTests`/`TsEditGateTests`/`TsPullHardeningTests`). Run the full suite to confirm
green on your machine; from a code-reading standpoint, this codebase is in better shape than the
original review's already-positive baseline.
