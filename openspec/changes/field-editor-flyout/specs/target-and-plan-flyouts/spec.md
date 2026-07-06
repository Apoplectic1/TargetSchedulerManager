# Spec: target-and-plan-flyouts

Part 1 surfaces: where the schema-driven editor appears and how it is invoked from the reconciliation grid.

## ADDED Requirements

### Requirement: TS-backed rows offer two edit triggers
Target group rows with a TS key and filter rows with a TS plan key SHALL offer both an edit glyph revealed
on pointer hover and a right-click context menu item ("Edit target…" / "Edit exposure plan…"). Disk-only
rows (no TS key) SHALL offer neither trigger. Existing gestures (expansion toggle, in-grid `desired` cell,
`active` checkbox) SHALL be unaffected.

#### Scenario: TS-backed target row
- **WHEN** the pointer hovers a target group row whose `TsTargetKey` is non-null
- **THEN** the edit glyph appears, and right-click shows "Edit target…"

#### Scenario: Disk-only row
- **WHEN** the pointer hovers or right-clicks a row with no TS key
- **THEN** no glyph appears and no edit menu item is offered

### Requirement: Triggers open the editor flyout anchored at the row
Either trigger SHALL open one flyout anchored to the row, hosting `TsFieldsEditor` for the row's entity
(`TsTable.Target` + `TsTargetKey`, or `TsTable.ExposurePlan` + `PlanTsKey`), titled with the entity's
identity (target name; target · filter). Dismissing the flyout SHALL require no confirmation (per-field
commit semantics) and SHALL leave grid scroll and expansion state untouched.

#### Scenario: Edit priority from the row
- **WHEN** the user right-clicks target "M 81", picks "Edit target…", and sets Priority to High
- **THEN** the flyout opens anchored at the M 81 row, the write applies per the editor capability, and the grid does not reload or lose scroll position

#### Scenario: Filter-row flyout
- **WHEN** the user clicks the edit glyph on the "M 81 · Ha" filter row
- **THEN** the flyout opens for that exposure plan showing Desired and Exposure seeded from the db

### Requirement: The context menu is the extension point for future row actions
The right-click menu SHALL be structured so additional entity actions (Part 3 "Edit template…", future
cadence actions) can be appended per row type without redesign — one menu per row type, items gated by
key/data presence.

#### Scenario: Menu composition today
- **WHEN** the user right-clicks a TS-backed filter row
- **THEN** the menu contains exactly the Part 1 item(s) for that row type, and the mechanism supports adding items gated by row data
