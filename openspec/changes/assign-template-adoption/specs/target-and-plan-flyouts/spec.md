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
