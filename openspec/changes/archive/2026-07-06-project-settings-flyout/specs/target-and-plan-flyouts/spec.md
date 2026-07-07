# Delta Spec: target-and-plan-flyouts (project-settings-flyout)

Adds the project editing trigger and its one cross-field courtesy to the flyout capability. Existing
requirements (target/plan triggers, flyout host, mosaic special case) are unchanged.

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: The context menu is the extension point for future row actions
The right-click menu SHALL be structured so additional entity actions (Part 3 "Edit template…", future
cadence actions) can be appended per row type without redesign — one menu per row type, items composed
additively and gated by key/data presence (a row offers its own editor plus any entity it resolves, e.g.
its project).

#### Scenario: Menu composition today
- **WHEN** the user right-clicks a TS-backed filter row
- **THEN** the menu contains that row type's own item(s) plus "Edit project…" when a project key resolves, and the mechanism supports adding further items gated by row data
