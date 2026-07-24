# target-and-plan-flyouts Specification

## Purpose

Where the schema-driven editor appears and how it is invoked from the reconciliation grid: edit triggers
on TS-backed rows, a row-anchored flyout host, and the mosaic parent/panel special case.

## Requirements

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

### Requirement: Mosaic parents edit whole-mosaic knobs; panels edit as normal targets
A mosaic parent row (a grouping node with no TS target) SHALL offer the edit triggers when its TS project key
is present, opening a mosaic flyout with exactly two controls: a master "Enable all panels" checkbox
(fan-out `target.active` to every TS-backed panel, each write guarded + audited; indeterminate display when
panels disagree; a failed fan-out re-reads and displays the resulting partial state) and the TS project's
priority (one `project.priority` write — panels at priority Default inherit it in TS scoring). Panel
mini-header rows with a TS key SHALL offer the standard target flyout ("Edit panel target…").

#### Scenario: Mosaic master enable with mixed panels
- **WHEN** a mosaic has some panels enabled and some disabled and the user opens the mosaic flyout
- **THEN** the master checkbox shows indeterminate; checking it writes `target.active = 1` to every TS-backed panel

#### Scenario: Panel target edit
- **WHEN** the user clicks the edit glyph on a TS-backed panel mini-header row
- **THEN** the standard target flyout opens for that panel's TS target

### Requirement: The context menu is the extension point for future row actions
The right-click menu SHALL be structured so additional entity actions (Part 3 "Edit template…", future
cadence actions) can be appended per row type without redesign — one menu per row type, items composed
additively and gated by key/data presence (a row offers its own editor plus any entity it resolves, e.g.
its project).

#### Scenario: Menu composition today
- **WHEN** the user right-clicks a TS-backed filter row
- **THEN** the menu contains that row type's own item(s) plus "Edit project…" when a project key resolves, and the mechanism supports adding further items gated by row data

### Requirement: TS-backed rows offer a project edit trigger
Rows resolving a TS project key SHALL offer a right-click context menu item "Edit project…" — target group
rows, panel rows, and filter rows carrying `ProjectTsKey` — opening the schema-generated editor flyout for
`TsTable.Project`, titled with the project's name. Rows with no project key SHALL not offer the item. The
mosaic parent's dedicated "Edit mosaic project…" item SHALL remain its project entry point. No hover glyph
is added for the project trigger.

#### Scenario: Project edit from a plan row
- **WHEN** the user right-clicks a filter row whose `ProjectTsKey` is non-null and picks "Edit project…"
- **THEN** the flyout shows the project's editable fields seeded fresh from the local db, each committing
  per-field through the guarded gate (journaled for push)

#### Scenario: Disk-only row offers nothing
- **WHEN** the user right-clicks a row with no TS project key
- **THEN** no "Edit project…" item appears

### Requirement: All cadence-safe project fields are editable, including state
The project flyout SHALL render every `TsEditableSchema` project field that is cadence-safe (state, priority,
minimum time, min/max altitude, custom-horizon flag + offset, meridian window, dither-every, grader flag,
smart-exposure-order, flats handling), with `state` as an ordinary `ProjectState` enum edit — a plain guarded
column write, matching TS's own Database Manager behavior (no `ActiveDate`/`InactiveDate` stamping exists in
TS). Cadence-breaking fields (`filterswitchfrequency`) SHALL remain excluded.

#### Scenario: State change is a plain write
- **WHEN** the user changes state from Active to Inactive and the write verifies
- **THEN** `project.state` holds the new code, no date column was touched, and one journal entry exists

### Requirement: The min-time/meridian-window trap warns and never blocks
The flyout SHALL surface a caution naming the rule whenever a commit of `minimumtime` or `meridianwindow`
leaves the pair in the state TS's own save refuses (`MeridianWindow > 0` AND
`MinimumTime > 2 × MeridianWindow` — the project would never be selected for imaging), while the write
itself SHALL proceed and journal normally. The caution SHALL clear when a later commit makes the pair valid.

#### Scenario: Warn on an invalid pair
- **WHEN** meridian window is 60 and the user commits minimum time 150
- **THEN** the value 150 is written + journaled and the flyout shows the never-selected caution

#### Scenario: Fixing the pair clears the warning
- **WHEN** the user then commits meridian window 90 (150 ≤ 180)
- **THEN** the caution disappears

### Requirement: A toolbar picker reaches every template
The toolbar SHALL offer a "Templates…" picker listing every exposure template from the loaded graph — name,
filter, and used-by-N-plans count — including templates no visible plan uses; choosing one SHALL open the
schema-generated editor flyout for `TsTable.ExposureTemplate` keyed by the template's TS key. Before a load
completes the picker SHALL decline with a status note rather than show an empty list.

#### Scenario: Template reachable without any plan
- **WHEN** a template exists in TS with zero exposure plans referencing it and the user opens Templates…
- **THEN** it appears in the list ("used by 0 plans") and opens for editing

### Requirement: Plan rows offer their template for editing
Filter rows with a TS plan key SHALL offer a right-click "Edit template…" item that resolves the plan's
template through the loaded graph and opens the same editor flyout; rows whose template cannot be resolved
SHALL not offer the item.

#### Scenario: Edit the template behind a plan
- **WHEN** the user right-clicks the "M 81 · Ha" plan row and picks "Edit template…"
- **THEN** the flyout opens for that plan's template with every editable template field seeded from the local db

### Requirement: Template flyouts state their blast radius
The template editor flyout SHALL be titled with the template's name and its used-by count ("Template
'<name>' — used by N plan(s)"), and journaled template edits SHALL carry that label into the push review —
a template edit affects every plan using it, so the scope is always stated, never implied.

#### Scenario: Shared scope visible at commit and push
- **WHEN** the user edits moon separation on a template used by 12 plans and later opens the push review
- **THEN** both the flyout title and the review line read "Template '<name>' — used by 12 plan(s)"

### Requirement: The full template surface is editable, edit-only
The template flyout SHALL render all cadence-safe `TsEditableSchema` exposuretemplate fields — the existing
seven plus twilight level (`TwilightLevel` enum), minutes offset, the moon avoidance suite (enabled,
separation, width, relax scale, relax max/min altitude, moon-down), dither-every, and maximum humidity —
with the −1 camera-default sentinels rendering as "use default" checkboxes as today. Template creation,
deletion, and duplication SHALL remain out of scope (TS functions).

#### Scenario: Moon suite editable
- **WHEN** the user opens a template and enables moon avoidance with separation 30°
- **THEN** both writes verify, journal, and appear in the next push review under the template's label

### Requirement: Committed edits mirror in their grid cells in place
A committed, verified edit with an in-grid mirror (plan `desired`, plan exposure → the Seconds cell,
enable toggles) SHALL update the affected row's cells in place — no grid reload, so scroll position,
expansion state, and any in-progress edit survive — and the owning group/panel header aggregates SHALL
recompute at once. Change notifications SHALL be raised only for cells whose value actually changed.

#### Scenario: Desired commit updates the row and its header without a rebuild
- **WHEN** an inline Desired edit verifies against the local db
- **THEN** the row's Desired and Hours cells show the new values in place and the owning group header re-aggregates — the grid is not reloaded

#### Scenario: Exposure edit mirrors the Seconds cell at once
- **WHEN** a flyout exposure edit verifies, including a revert to the template default
- **THEN** the Seconds cell immediately shows the new effective value (resolved from the db when the caller does not know it), without waiting for the next reload
