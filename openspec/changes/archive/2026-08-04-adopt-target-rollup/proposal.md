# adopt-target-rollup

## Why

Adopting a multi-filter disk-only target into TS today takes one right-click + assignment dialog **per
cell** — a four-filter target is four full dialog round-trips, each re-picking the same project (locked
after the first only because the target then exists). The per-cell action is the right primitive for
touch-ups, but the common real case — "this whole target's history should be in TS" — deserves one
gesture. Design settled in explore mode 2026-08-04.

## What Changes

- The **target rollup row** offers a right-click adoption action whenever ≥1 of its child cells is
  individually eligible under the existing planner gate: "Add to TS…" when the TS target doesn't exist
  (creates it), "Add TS plans…" when it does (bulk-adopts the remaining unplanned cells).
- One **combined assignment dialog**: the project chosen once (locked to the owner when the TS target
  exists — same rule as today), then one row per eligible cell — filter/seconds/×count facts, its own
  template dropdown with the existing strict scope + preselect + non-pairing caution machinery, and an
  include checkbox (checked by default) so a single unwanted cell doesn't force Cancel.
- **Unservable cells** (empty template scope: no template of that filter/bin in the chosen project's
  profile, or non-square binning) render greyed with the reason and are excluded from the write; Accept
  proceeds with the servable, checked cells. Each skipped cell keeps its per-cell action for later.
- Switching the project **re-scopes every cell's candidate list** to the new profile (a cell servable in
  one project may be empty-scope in another).
- Accept writes **one atomic local batch**: the target payload (when creating) + all accepted
  born-complete plans through the existing gate insert path — one journal group, one close-time
  re-reconcile. A per-cell backstop refusal during build aborts the whole batch, naming the cell.
- Mosaic parents remain out of scope (consistent with the planner's existing panel exclusion); panel
  cells keep their per-cell action only.
- The per-cell adoption action is unchanged.

## Capabilities

### New Capabilities

_None — this extends the existing adoption capability._

### Modified Capabilities

- `disk-row-adoption`: the "always per-row, never a sweep" purpose framing is amended — adoption now has
  two grains, per-cell and per-target. Added requirements: the rollup context-menu action and its gating;
  the combined multi-cell assignment dialog (per-cell template assignment, include checkboxes, greyed
  unservable cells, project re-scoping); the multi-plan atomic batch. Existing per-cell requirements
  (eligibility gate, born-complete counts, non-pairing caution, sync-model semantics) are reused
  unchanged as the per-cell building blocks.

## Impact

- **App only** — no Library change: `AdoptionPlanner` (eligibility enumeration over a rollup's children,
  multi-cell facts, multi-plan build), `MainWindow.Flyouts.cs` (rollup menu entry),
  `MainWindow.Dialogs.cs` (combined dialog), `MainViewModel.Edits.cs` (the bulk adopt funnel + prompt
  hook). Write path (`TsEditGate.ApplyInsertAsync`), journal, marks, and re-reconcile are reused as-is.
- Tests: `TargetSchedulerManager.App.Tests` — planner enumeration/gating, multi-plan build, batch
  atomicity; existing per-cell tests unaffected.
- Docs riding the change: `SUBSYSTEMS.md` (adoption section), `UI.md` (dialog), `CHANGELOG.md`/`ROADMAP.md`.
