# busy-gate — one busy exclusion for bulk operations and row edits

## Why

Two independent code reviews (2026-07-24, `docs/2026-07-24-app-code-review.md` + a blind cross-check)
converged on the same correctness hole: the app's "IsLoading doubles as the load/push mutual exclusion"
convention is enforced only where it was hand-written. `RunVisibleTonightAsync` checks the flag but never
sets it, so Reload / Push / a second Find click interleave with a running pass; and **no row-edit path**
(`SetTargetEnabledAsync`, `SetPlanEnabledAsync`, `SetPlanDesiredAsync`, `SetTsFieldAsync`, flyout commits)
checks the flag at all, so a checkbox toggle can put a gate writer beside the load's write-back writer
(single-writer violation) or hold a connection open across the pull's atomic swap (sharing violation after
a complete backup). Separately, the visible-tonight pass opens a fresh SQLite connection per flip
(N `Task.Run` hops), which is both wasteful and the very thing that opens the interleaving window.

## What Changes

- **One busy gate, structurally enforced**: a check-and-set `TryBeginBusy()` / `EndBusy()` pair on
  `MainViewModel` (UI-thread atomic by construction) replaces the three hand-written
  `if (IsLoading) …; IsLoading = true` sites; `RunVisibleTonightAsync` joins it (today it only checks).
- **Row edits blocked while busy — both layers**: every grid edit surface (checkboxes, Desired boxes,
  edit glyphs, context-menu/flyout openers, the Visible-tonight Find button) disables via a bound
  `CanEdit` property (visible feedback), AND the `Set*Async` view-model funnel refuses with a status-line
  note when busy (the backstop — the invariant holds even if a future control forgets the binding).
- **Visible-tonight applies as one batch**: new `TsEditGate.ApplyManyAsync` runs the whole edit list in
  one worker invocation on one editor connection, returning per-edit outcomes; `ApplyAsync` becomes the
  one-element case (single code path). The pass's UI-thread re-entry window between flips disappears
  entirely, and an N-flip pass opens 1 connection instead of N.

Not in scope (next cluster): the push closing-pull misreport (journal cleared before the closing pull,
failure message claims otherwise) and the discard+cancelled-pull stale-grid session.

## Capabilities

### New Capabilities
- `busy-exclusion`: the one-at-a-time rule for db-touching work — bulk operations (load, push,
  visible-tonight) are mutually exclusive, row edits are refused and their surfaces disabled while a bulk
  operation runs, and the exclusion is check-and-set on the UI thread (structural, not by convention).

### Modified Capabilities
- `visible-tonight-toggle`: the pass SHALL hold the busy exclusion for its whole span (plan → apply) and
  SHALL apply its flips as a single off-UI-thread batch on one editor session — no UI-thread re-entry
  between flips, no concurrent second pass.

## Impact

- **App only, no library changes**: `ViewModels/MainViewModel.cs` (gate helpers, funnel guards, `CanEdit`),
  `Shared/TsEditGate.cs` (`ApplyManyAsync`), `MainWindow.xaml` / `MainWindow.xaml.cs` (IsEnabled bindings,
  flyout-opener guards).
- **Tests**: `MainViewModelTests` (gate exclusion, funnel refusal, visible-tonight under the gate),
  `TsEditGateTests` (`ApplyManyAsync` single-session batch, per-edit outcomes, one-element equivalence).
- **Docs**: `ARCHITECTURE.md` key-facts invariant gains the busy-exclusion line; CLAUDE.md condensed
  mirror updated in the same commit.
- **UX change (visible)**: edit controls grey out during a load/push/visible-tonight pass instead of
  looking live; a refused edit reports on the status line instead of silently doing nothing.
