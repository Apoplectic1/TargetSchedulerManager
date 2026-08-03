## MODIFIED Requirements

### Requirement: TS-backed rows offer two edit triggers
Target group rows with a TS key and filter rows with a TS plan key SHALL offer both an edit glyph revealed
on pointer hover and a right-click context menu item ("Edit target…" / "Edit exposure plan…"). Disk-only
rows (no TS key) SHALL offer neither **edit** trigger — no hover glyph and no edit menu items — but an
adoption-eligible disk-only row SHALL offer the right-click adoption action defined by the
`disk-row-adoption` capability (composed through the same additive, data-gated menu). Existing gestures
(expansion toggle, in-grid `desired` cell, `active` checkbox) SHALL be unaffected.

#### Scenario: TS-backed target row
- **WHEN** the pointer hovers a target group row whose `TsTargetKey` is non-null
- **THEN** the edit glyph appears, and right-click shows "Edit target…"

#### Scenario: Disk-only row
- **WHEN** the pointer hovers or right-clicks a row with no TS key
- **THEN** no glyph appears and no **edit** menu item is offered; the menu contains the adoption action exactly when the row is adoption-eligible, and nothing otherwise

### Requirement: Mosaic parents edit whole-mosaic knobs; panels edit as normal targets
A mosaic parent row (a grouping node with no TS target) SHALL offer the edit triggers when its TS project key
is present, opening a mosaic dialog with exactly two controls: a master "Enable all panels" checkbox
(fan-out `target.active` to every TS-backed panel, each write guarded + audited; indeterminate display when
panels disagree; a failed fan-out re-reads and displays the resulting partial state) and the TS project's
priority (one `project.priority` write — panels at priority Default inherit it in TS scoring). Panel
mini-header rows with a TS key SHALL offer the standard target editor ("Edit panel target…"). Both mosaic
dialog rows SHALL carry the leading per-field sync-direction mark like schema-generated field rows: the
master enable's mark is the union of the panels' `target.active` field states (tooltip listing per-panel
lines), the priority's mark resolves the project's `priority` field; marks refresh after each commit.

#### Scenario: Mosaic master enable with mixed panels
- **WHEN** a mosaic has some panels enabled and some disabled and the user opens the mosaic dialog
- **THEN** the master checkbox shows indeterminate; checking it writes `target.active = 1` to every TS-backed panel

#### Scenario: Panel target edit
- **WHEN** the user clicks the edit glyph on a TS-backed panel mini-header row
- **THEN** the standard target editor opens for that panel's TS target

#### Scenario: Fanned-out enable marks the master row
- **WHEN** two panels carry unpushed `active` writes and the user reopens the mosaic dialog
- **THEN** the master enable row shows `→` with a tooltip line per marked panel

#### Scenario: Project priority collision shows on its row
- **WHEN** the mosaic project's `priority` was changed on the rig and the user also has an unpushed
  priority write
- **THEN** the priority row shows `⇄` with both directions' lines

### Requirement: Triggers open the editor in a movable dialog seeded near the row
Either trigger SHALL open one movable dialog hosting `TsFieldsEditor` for the row's entity
(`TsTable.Target` + `TsTargetKey`, or `TsTable.ExposurePlan` + `PlanTsKey`), titled with the entity's
identity (target name; target · filter). The dialog SHALL open near the clicked row (clamped to the
window; centered as the fallback) and SHALL be repositionable by dragging any non-interactive spot.
Dismissing SHALL require no confirmation (per-field commit semantics) and SHALL leave grid scroll and
expansion state untouched. *(2026-08-03: converted from an anchored `Flyout` — a flyout renders in its own
framework-positioned popup window that no transform or offset write can move, field-verified three ways;
context menus and pickers stay flyouts, where movability is meaningless.)*

#### Scenario: Edit priority from the row
- **WHEN** the user right-clicks target "M 81", picks "Edit target…", and sets Priority to High
- **THEN** the dialog opens near the M 81 row, the write applies per the editor capability, and the grid does not reload or lose scroll position

#### Scenario: Filter-row editor
- **WHEN** the user clicks the edit glyph on the "M 81 · Ha" filter row
- **THEN** the dialog opens for that exposure plan showing Desired and Exposure seeded from the db

#### Scenario: The dialog is movable
- **WHEN** the open editor covers rows the user wants to compare against
- **THEN** dragging a non-interactive spot (title, label, blank space) repositions it; buttons and inputs
  keep their own gestures

## RENAMED Requirements

- FROM: `### Requirement: Triggers open the editor flyout anchored at the row`
- TO: `### Requirement: Triggers open the editor in a movable dialog seeded near the row`

## ADDED Requirements

### Requirement: The plan editor completes the capture spec with a write-through template section
The exposure-plan editor SHALL append an editable section for the capture columns of the template behind
the plan (gain, offset, bin), headed by the template's identity **and blast radius** ("template '<name>' —
used by N plan(s)"). An edit there SHALL be an ordinary template edit — written through the guarded gate
to the `exposuretemplate` row, journaled as a template change (so direction marks light every plan row
sharing the template), with the template's per-field marks on the section's rows. The section renders only
the capture columns; the full template form remains the "Edit template…" flyout.

#### Scenario: Gain edited from the plan flyout re-keys all users visibly
- **WHEN** the user opens the "M 81 · Ha" plan flyout and changes the template section's gain
- **THEN** the write lands on the shared template, and every filter row using that template marks `→`

#### Scenario: The blast radius is visible at the point of edit
- **WHEN** the plan flyout opens for a plan whose template backs 79 plans
- **THEN** the section header reads "template '<name>' — used by 79 plan(s)"
