# busy-gate — tasks

## 1. Gate primitives (TDD: tests first per group)

- [x] 1.1 `TsEditGate`: add `TsFieldEdit` record + `ApplyManyAsync(IReadOnlyList<TsFieldEdit>)` — one
      `Task.Run`, one editor from the factory for the whole batch, per-edit outcomes; a thrown edit
      yields `Failed` for that edit and the batch continues. `ApplyAsync` delegates to
      `ApplyManyAsync([single])`.
- [x] 1.2 `TsEditGateTests`: batch uses exactly one factory invocation; per-edit outcomes align with
      inputs (mixed Applied/Refused/Failed); a throwing edit fails only itself; one-element
      `ApplyManyAsync` ≡ `ApplyAsync` (journal + outcome identical).
- [x] 1.3 `MainViewModel`: add `TryBeginBusy()` / `EndBusy()` (per design D1 — refuses on `IsLoading`
      or `_editsInFlight > 0`, raises sync state both ways) and `CanEdit => !IsLoading` raised on every
      `IsLoading` transition. `CanPush` becomes `Sync.IsDirty && !IsLoading`.

## 2. Adopt the gate

- [x] 2.1 `LoadAsync` and `PushAsync` switch their manual `if (IsLoading)…; IsLoading = true` /
      `finally` blocks to `TryBeginBusy()` / `EndBusy()` — behavior-preserving (post-push reload stays
      outside the scope, as today).
- [x] 2.2 `RunVisibleTonightAsync`: acquire via `TryBeginBusy()`; map `plan.Edits` → `TsFieldEdit` and
      apply via one `await _gate.ApplyManyAsync(...)`; release in `finally`; closing
      `LoadAsync(PullPolicy.Never)` moves after `EndBusy()`; status-summary ordering preserved (set
      after the reload). Per-flip failure count/log from the outcome list.
- [x] 2.3 `MainViewModelTests`: visible-tonight holds the exclusion (a `LoadAsync`/`PushAsync`/second
      pass started mid-pass is refused, zero double-journal); the closing reload still runs; failure
      count surfaces in the summary.

## 3. Row-edit gating (funnel + in-flight counter)

- [x] 3.1 `MainViewModel`: `RefuseIfBusy(what)` helper (status note + log + false) at the top of every
      edit entry point — enumerate by grepping `_gate.ApplyAsync` / `ApplyManyAsync` callers
      (`SetTargetEnabledAsync`, `SetPlanEnabledAsync`, `SetPlanDesiredAsync`, `SetTsFieldAsync`, and any
      exposure/template setters found).
- [x] 3.2 `MainViewModel`: `_editsInFlight` counter incremented/decremented (UI thread) around every
      funnel `await _gate.Apply…`; `TryBeginBusy()` refuses while non-zero with a status note.
- [x] 3.3 `MainViewModelTests`: every public setter refuses under busy (returns false, nothing
      journaled, status notes why); `TryBeginBusy` refuses while an edit is in flight and succeeds after
      it completes.

## 4. Visible disable (XAML)

- [x] 4.1 `MainWindow.xaml`: `IsEnabled="{x:Bind ViewModel.CanEdit, Mode=OneWay}"` on the row ListView
      and on Find, Reload, Pull now, Templates…; Push already binds `CanPush` (now busy-aware). Leave
      enabled: search, filters, Expand/Collapse all, Ambiguities…, Cancel pull.
- [x] 4.2 Build + full test run (slnx-only — per VERIFICATION.md, never csproj-direct).

## 5. Docs + wrap-up

- [x] 5.1 `ARCHITECTURE.md` key facts + CLAUDE.md condensed mirror: add the busy-exclusion invariant
      (bulk ops mutually exclusive, edits refused while busy, both structural); note
      `ApplyManyAsync` as the gate's batch path. ROADMAP recently-shipped digest line. Same commit as
      the code.
- [x] 5.2 Human verification pass (user-run): grid greys during load/pull/pass; refused-edit status
      note reads well; Cancel pull reachable mid-pull; visible-tonight summary unchanged.
