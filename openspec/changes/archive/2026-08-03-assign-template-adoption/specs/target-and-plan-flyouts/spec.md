# target-and-plan-flyouts — delta for assign-template-adoption

## RENAMED Requirements
- FROM: `### Requirement: Triggers open the editor in a movable dialog seeded near the row`
- TO: `### Requirement: Triggers open the editor in a movable centered dialog`

## MODIFIED Requirements

### Requirement: Triggers open the editor in a movable centered dialog
Either trigger SHALL open one movable dialog hosting `TsFieldsEditor` for the row's entity
(`TsTable.Target` + `TsTargetKey`, or `TsTable.ExposurePlan` + `PlanTsKey`), titled with the entity's
identity (target name; target · filter). The dialog SHALL open **centered** and SHALL be repositionable by
dragging any non-interactive spot. Dismissing SHALL require no confirmation (per-field commit semantics)
and SHALL leave grid scroll and expansion state untouched. *(2026-08-03: converted from an anchored
`Flyout` — a flyout renders in its own framework-positioned popup window that no transform or offset write
can move, field-verified three ways; context menus and pickers stay flyouts, where movability is
meaningless. Open-near-the-row seeding was tried and retired the same day, user call: the ContentDialog
element is a full-window overlay whose visible box centers inside it, and translating it against an anchor
raced layout — twice field-failed with the box off-screen, an invisible modal eating every click.)*

#### Scenario: Edit priority from the row
- **WHEN** the user right-clicks target "M 81", picks "Edit target…", and sets Priority to High
- **THEN** the dialog opens centered, the write applies per the editor capability, and the grid does not reload or lose scroll position

#### Scenario: Filter-row editor
- **WHEN** the user clicks the edit glyph on the "M 81 · Ha" filter row
- **THEN** the dialog opens for that exposure plan showing Desired and Exposure seeded from the db

#### Scenario: The dialog is movable
- **WHEN** the open editor covers rows the user wants to compare against
- **THEN** dragging a non-interactive spot (title, label, blank space) repositions it; buttons and inputs
  keep their own gestures

### Requirement: Committed edits mirror in their grid cells in place
A committed, verified edit with an in-grid mirror (plan `desired`, plan exposure → the Seconds cell,
enable toggles) SHALL update the affected row's cells in place — no grid reload, so scroll position,
expansion state, and any in-progress edit survive — and the owning group/panel header aggregates SHALL
recompute at once. Change notifications SHALL be raised only for cells whose value actually changed.
An applied edit to a **cell-keying field** — plan exposure, template gain/offset/bin/default-exposure/
filter/name, target rotation — re-shapes the reconciliation (merged rows split, splits merge), which no
in-place mirror can express: when the editor dialog closes after such an edit, the grid SHALL
re-reconcile without a pull, so a row never keeps asserting a pairing the edit broke (obs 4798). The
in-place mirror still applies while the editor remains open.

#### Scenario: Desired commit updates the row and its header without a rebuild
- **WHEN** an inline Desired edit verifies against the local db
- **THEN** the row's Desired and Hours cells show the new values in place and the owning group header re-aggregates — the grid is not reloaded

#### Scenario: Exposure edit mirrors the Seconds cell at once
- **WHEN** a flyout exposure edit verifies, including a revert to the template default
- **THEN** the Seconds cell immediately shows the new effective value (resolved from the db when the caller does not know it), without waiting for the next reload

#### Scenario: A de-pairing exposure edit re-splits when the editor closes
- **WHEN** the user edits a merged Both row's exposure from 300 to 600 s over 300 s frames and closes the editor
- **THEN** the grid re-reconciles without a pull and the cell renders as its split — the TS plan and the disk frames on separate lines

#### Scenario: Non-keying edits never trigger the close-time reload
- **WHEN** an editor session commits only desired, enable, or moon-rule changes
- **THEN** closing the dialog reloads nothing — the in-place mirrors were the whole story
