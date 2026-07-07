# Proposal: project-settings-flyout

## Why

Project-level scheduling knobs (state, priority, altitude/horizon limits, minimum time, meridian window,
dither cadence, grader, flats handling) are today editable only for mosaic parents — and there only priority —
while every other TS surface TSM manages is already flyout-editable. Projects are a column, not rows, so
Part 2's open question was the anchor; that and the `project.state` hazard are now settled (2026-07-06):
right-click is the anchor, and the TS source clone proves `state` needs no special machinery.

## What Changes

- **Right-click "Edit project…" on any TS-backed row** (target groups, panels, plan rows — everything already
  carries `ProjectTsKey`): opens the existing schema-generated `TsFieldsEditor` flyout for `TsTable.Project`,
  titled with the project name. No new visual chrome; the hover glyph stays single-purpose per row.
- **All 12 cadence-safe project fields become editable**, including `state`: verified against the TS source
  clone that TS does **not** stamp `ActiveDate`/`InactiveDate` on state transitions (schema setters,
  planner, and its Database Manager `Save()` are all plain writes — the recorded "date-stamping" gotcha was
  stale), so `state` is an ordinary enum edit via the existing `ProjectState` map. `filterswitchfrequency`
  remains auto-excluded (cadence-breaking, parked change's territory).
- **TS's one cross-field save rule replicated as warn-never-block**: TS refuses to save a project when
  `MinimumTime > 2 × MeridianWindow` ("project will never be selected for imaging"). Per-field commit can't
  reasonably block (the user may be mid-way through fixing both fields), so committing either field while the
  pair is invalid surfaces a caution — guards carry facts, buttons carry decisions.
- Edits commit per-field through the existing guarded gate → **local db + journal → reviewed push** (the
  sync model): zero new write machinery.
- The mosaic parent's "Edit mosaic project…" flyout (master enable + priority) stays as-is.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `target-and-plan-flyouts`: adds the project edit trigger (right-click on TS-backed rows → project flyout)
  and the min-time/meridian-window cross-field warning requirement.

## Impact

- **TSM app only**, and small: `MainWindow.Row_RightTapped` gains one gated menu item per row shape;
  `ShowEditFlyoutAsync` already handles any `TsTable`; `MainViewModel.SetTsFieldAsync` is the generic commit
  path. The cross-field warn needs the flyout commit callback to peek at the sibling field's current value
  (both are in the seed dictionary / committed values).
- **No library changes**: `TsEditableSchema`'s 13 project rows (12 after the cadence filter) and
  `EnumValues("ProjectState"/"ProjectPriority")` ship today.
- **Sync model**: project edits journal and replay like any field edit — nothing new to verify there beyond
  one seam test that a project-field commit journals with the project key.
- **Tests**: menu-gating logic (ProjectTsKey null ⇒ no item) is code-behind (app-verified); the warn rule and
  the journal seam are unit-testable.
