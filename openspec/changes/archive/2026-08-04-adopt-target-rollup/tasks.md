# adopt-target-rollup — tasks

## 1. Planner (pure) — `TargetSchedulerManager.App/Services/AdoptionPlanner.cs`

- [x] 1.1 Add the bulk records: `BulkAdoptionFacts` (shared header + target-creation facts + per-project
  options), the generalized per-project option carrying per-cell `(Candidates, PreselectIndex)` lists,
  and `BulkAdoptionChoice(Project, accepted (Row, Template) pairs)` (design D1/D4)
- [x] 1.2 `EligibleCells(TargetGroupRow, TsPlanData)`: the rollup's children filtered by the existing
  `IsEligible`, grid order preserved; empty for mosaic parents (design D2)
- [x] 1.3 `GetBulkFacts`: project situation resolved once (locked owner via any child's TS target, else
  pickable projects + target-creation facts from the disk target — reusing the per-cell refusal wording),
  then `ListCandidates` per cell × per project, precomputed (design D1)
- [x] 1.4 `BuildBulk`: target payload once when creating (rotation seed = first included cell in grid
  order expressing a sky rotation, design D6), then the existing per-cell plan payload per accepted cell;
  one `AdoptionPlan.Rows` list; any per-cell structural refusal aborts naming the cell (design D5)
  *(shipped as `BulkAdoptionPlan` — `AdoptionPlan.Template` is singular; shared payload builders extracted
  from `Build` so both grains use one code path)*

## 2. VM funnel — `TargetSchedulerManager.App/ViewModels/MainViewModel.Edits.cs`

- [x] 2.1 `IsTargetAdoptable(TargetGroupRow)` menu gate (≥1 eligible cell against the retained load) and
  the target-exists split for the menu label
- [x] 2.2 `AdoptTargetAsync(TargetGroupRow)` mirroring `AdoptRowAsync`: busy exclusion → `GetBulkFacts`
  → `BulkAdoptPrompt` hook → `BuildBulk` → gate insert → no-pull reload; refusals via
  `AdoptRefusalPrompt`; `ADOPT` logging with cell count (design D7)

## 3. UI — `MainWindow.Flyouts.cs` / `MainWindow.Dialogs.cs`

- [x] 3.1 Rollup context-menu entry in `Row_RightTapped`'s `TargetGroupRow` case (non-mosaic): "Add to
  TS…" / "Add TS plans…" gated by `IsTargetAdoptable`, Add glyph, routed through the VM funnel
- [x] 3.2 Factor the per-cell dialog row (template combo + caution binding) into a helper shared with the
  single-cell dialog (design D3) *(shipped as `AssignmentRowControls` in `MainWindow.Dialogs.cs`)*
- [x] 3.3 `ShowBulkAdoptDialogAsync`: header (create-vs-add wording, project picker/locked line,
  target-creation facts), scrollable cell list — facts + include checkbox (default checked) + template
  combo + caution per servable cell, disabled row + reason for empty-scope cells; project switch swaps
  every row's precomputed candidate list; Accept enabled iff ≥1 included servable cell; wire as
  `BulkAdoptPrompt` (design D3/D4)

## 4. Tests — `TargetSchedulerManager.App.Tests`

- [x] 4.1 Planner: `EligibleCells` (grid order, split/planned/mosaic exclusions, zero-eligible),
  `GetBulkFacts` (locked vs picker, per-cell×per-project candidates, refusal wording), `BuildBulk`
  (target-once + N plans, rotation tie-break, exposure sentinel per cell, per-cell refusal aborts whole)
- [x] 4.2 VM funnel tests — scope adjusted during implementation: gating (`IsTargetAdoptable`), cancel
  writes nothing, structural refusal surfaces through `AdoptRefusalPrompt` (all in
  `BulkAdoptionTests.cs`). The accept leg ends in the funnel's closing `LoadAsync` (a real disk scan —
  the injectable loader seam is the deferred M2 item), so it goes to field verification exactly as the
  per-cell funnel did; unchecked/unservable exclusion is dialog behavior (visual verify)

## 5. Docs + verify (same commit as code)

- [x] 5.1 `SUBSYSTEMS.md` adoption section: the two grains; `UI.md`: the combined dialog; `CHANGELOG.md`
  + `ROADMAP.md` current-status line
- [x] 5.2 At sync/archive: amend the main spec's Purpose ("always per-row, never a sweep" → two grains)
  per the delta's note *(done at archive, 2026-08-04)*
- [x] 5.3 Build + full app test suite green (377 passed, 13 new); visual verification (dialog layout,
  re-scope behavior, checkbox flow, push round-trip) handed to the user per VERIFICATION.md
