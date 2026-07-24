# busy-gate — design

## Context

The documented concurrency model is "the UI thread serializes commands; workers do I/O" — but the
serialization is by convention: three hand-written `if (IsLoading) return; IsLoading = true;` sites, one
of which (`RunVisibleTonightAsync`, `MainViewModel.cs:333`) only checks and never sets, and a row-edit
funnel (`SetTargetEnabledAsync` / `SetPlanEnabledAsync` / `SetPlanDesiredAsync` / `SetTsFieldAsync` and
the flyout commit paths) that never consults the flag at all. Consequences verified in review
(`docs/2026-07-24-app-code-review.md` C1/M5 + the blind cross-check's grid-gating finding):

- Reload / Push / a second Find click interleave with a running visible-tonight pass (the pass awaits
  per flip, freeing the UI thread between edits).
- A row edit during a load puts a `TsEditGate` writer beside `WriteBackStep`'s writer on the local db
  (single-writer violation), or holds a pooled connection across the pull's
  `ClearAllPools(); File.Move(...)` swap (sharing violation after a complete backup).
- The pass opens a fresh SQLite connection per flip: N flips = N `Task.Run` hops + N connection opens,
  and the awaits between them are what open the interleaving window.

Constraint: row-level edit controls live inside `DataTemplate`s, where `x:Bind` scopes to the row
object — a per-control binding cannot see the ViewModel's properties.

## Goals / Non-Goals

**Goals:**
- The busy exclusion becomes structural: impossible for a future bulk command to forget, one place to
  read.
- No two writers can touch the local db concurrently — bulk-vs-bulk *and* edit-vs-bulk.
- Visible-tonight applies as one worker batch on one editor session (removes the window *and* the N×
  connection cost).
- Busy state is visible: edit surfaces grey out instead of looking live.

**Non-Goals:**
- The push closing-pull misreport and discard+cancelled-pull stale grid (next cluster — truthful-outcome).
- The `TsFieldsEditor` intra-flyout double-commit race (same open flyout, two rapid confirms) — the busy
  gate does not serialize edits against each other, only against bulk operations. Separate, smaller fix.
- Journal durability (review M2) and the `Push` decomposition (M1).

## Decisions

### D1 — One check-and-set pair on the ViewModel, UI-thread atomic

`TryBeginBusy()` / `EndBusy()` private helpers on `MainViewModel`:

- `TryBeginBusy()`: on the UI thread, refuses if `IsLoading` **or an edit is in flight** (see D3); else
  sets `IsLoading = true`, raises sync state, returns true. Check-and-set is atomic because only the UI
  thread calls it — no lock, same invariant as today, now in one place.
- `EndBusy()`: `IsLoading = false` + `RaiseSyncState()` (the pattern `PushAsync`'s `finally` already
  uses).
- `LoadAsync`, `PushAsync`, `RunVisibleTonightAsync` all adopt the pair. Any future bulk command joins
  by construction (there is no other way to set `IsLoading`).
- **Trailing reloads stay outside the scope**: `RunVisibleTonightAsync`'s closing
  `LoadAsync(PullPolicy.Never)` moves after `EndBusy()` (exactly how `PushAsync` already sequences its
  post-push reload) — under the gate it would silently no-op. The gap between `EndBusy` and the reload
  is benign: anything that sneaks in takes the gate itself.
- Status-line ordering preserved: the pass's summary line is set after the closing reload, as today.

*Alternative considered*: a `SemaphoreSlim(1,1)`. Rejected — adds an async-locking idiom the codebase
deliberately avoids; the UI-thread-serialized model is sound, it just needs to be un-forgettable.

### D2 — Row edits refused in the funnel, surfaces disabled in the view

Two layers, per the user's decision (belt and suspenders):

- **VM backstop**: every edit entry point (`SetTargetEnabledAsync`, `SetPlanEnabledAsync`,
  `SetPlanDesiredAsync`, `SetPlanExposureAsync`-equivalents, `SetTsFieldAsync` — enumerate by grepping
  `_gate.ApplyAsync` callers) starts with a shared `RefuseIfBusy(what)` guard: when `IsLoading`, set a
  status-line note ("busy — edit not applied; retry after the load finishes"), log, return false. Callers
  already treat `false` as revert — the NumberBox snaps back, checkboxes restore — so refusal is loud,
  not silent (guards carry facts).
- **Visible disable**: a `CanEdit => !IsLoading` VM property (raised alongside `IsLoading`). Because
  `DataTemplate` `x:Bind` can't reach the VM, row surfaces disable at the **ListView level** — one
  `IsEnabled="{x:Bind ViewModel.CanEdit, Mode=OneWay}"` on the grid — plus per-button bindings on the
  page-scope toolbar: Find, Reload, Pull now, Templates…; `CanPush` becomes
  `Sync.IsDirty && !IsLoading`. Stays enabled: search, filters, Expand/Collapse all, Ambiguities…
  (read-only), Cancel pull (bound to `IsPulling` — it is the escape hatch during a pull).

*Alternative considered*: per-row `IsEditable` INPC property swept over `_allRows` on busy transitions —
keeps scrolling live while busy. Rejected for now: churns every row type for a state lasting seconds;
the ListView-level disable is one binding. Revisit only if the frozen-scroll moment ever grates.

*Known residual*: a flyout already open when busy begins can still confirm — the funnel guard is the
layer that catches it (status note + revert). That is the backstop doing its job, not a hole.

### D3 — In-flight edit counter closes the reverse window

The funnel guard stops edits starting during a bulk op; the reverse — a bulk op starting while an edit's
worker is still writing — needs the other direction. An `_editsInFlight` int on the VM, incremented
before each `await _gate.Apply…` and decremented after (both on the UI thread — no interlocking needed);
`TryBeginBusy()` refuses while it is non-zero with a status note ("edit in flight — try again"). Windows
closed in both directions, still zero locks.

*Alternative considered*: have the load await quiescence instead of refusing. Rejected — refusal is
simpler, honest, and the retry costs one click; silent waiting hides state.

### D4 — `ApplyManyAsync`: one worker, one editor session, per-edit outcomes

New in `TsEditGate` (Shared):

- `record TsFieldEdit(TsTable Table, string Key, string Column, object? Value, string Label)` — the
  gate-level edit unit.
- `Task<IReadOnlyList<EditOutcome>> ApplyManyAsync(IReadOnlyList<TsFieldEdit> edits)`: single
  `Task.Run`, one `ITsEditor` from the factory for the whole batch, applying today's `ApplyAsync` body
  per edit (guard-check → verify → `RecordEdit` journal → per-edit `EditOutcome`); a thrown edit yields
  `Failed` for that edit and the batch continues (same per-edit isolation as today's loop).
- `ApplyAsync` delegates to `ApplyManyAsync([single])` — one code path, existing `TsEditGateTests`
  exercise both shapes.
- `RunVisibleTonightAsync` maps `plan.Edits` → `TsFieldEdit` and collapses its loop to a single await:
  no UI-thread re-entry between flips at all, 1 connection instead of N. Per-flip failure counting and
  logging preserved from the returned outcome list.

*Alternative considered*: keep per-edit `ApplyAsync` and rely on D1 alone. Rejected — correct but leaves
the N× connection cost and keeps a long-lived busy scope made of many small hops; the batch makes the
busy scope one hop.

## Risks / Trade-offs

- [ListView disabled while busy blocks scrolling/expansion for the duration] → loads are seconds
  (load-split was retired because the scan is fast); the pull already occupies the status line;
  revisit with the per-row property only if it grates in real use.
- [`ApplyManyAsync` changes mid-pass UI cadence — no per-flip yields] → nothing user-visible was
  rendered per flip anyway (no per-edit badge raise in the pass today); the closing reload shows the
  end state, as before.
- [`CanPush` gaining `&& !IsLoading` greys Push during load] → intended; `PushAsync`'s own gate already
  refused, the button now says so.
- [A refused edit reverts a control the user just touched] → status line explains why in the same
  gesture; strictly better than today's silent interleave.
- [Two entry stacks (funnel guard + gate) could drift] → the guard lives in one `RefuseIfBusy` helper;
  tasks include a test asserting every `_gate.ApplyAsync`/`ApplyManyAsync` caller in `MainViewModel`
  passes through it (by exercising each public setter under busy).

## Migration Plan

None — no persisted state changes, no library changes, no schema. Clean rebuild per project rule.

## Open Questions

None blocking. (Surface-selection judgment calls — e.g. whether Templates… greys — resolved above;
adjust at review if the running app suggests otherwise.)
