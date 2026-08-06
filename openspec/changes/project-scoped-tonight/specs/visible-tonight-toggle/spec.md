# visible-tonight-toggle — delta (project-scoped-tonight)

## RENAMED Requirements

- FROM: `### Requirement: Draft and Closed projects are excluded`
- TO: `### Requirement: The pass never writes Draft or Closed project state`

## MODIFIED Requirements

### Requirement: Site and knob input contract
The system SHALL obtain the observing site (latitude, longitude, time zone, elevation) from
`DevDefaults` constants — the sole location input to the night-window and visibility computations —
and the predicate knobs from the toolbar's Visible-Tonight group: a **Project dropdown** (immediately
right of the group label) listing every TS project by name regardless of state with an **All projects**
entry selected by default; a Duration numeric up-down in whole minutes, range 0–999, default 30; and a
Floor numeric up-down in real degrees, range 0–90, default 30. A Tonight button SHALL run the pass with
the knobs' current values.

Selecting a project in the dropdown SHALL fill Duration from the project's `minimumtime` and Floor from
its `minimumaltitude` as read from the local working copy — a read, never a write. Selecting a
different project (or All) SHALL refill the boxes, discarding any unsaved box edits. The knob ranges
SHALL match the TS schema's for those fields, so a fill can never clamp a stored value to something the
user did not choose.

#### Scenario: Knob values drive the verdicts
- **WHEN** Tonight is pressed with Duration 120 and Floor 45
- **THEN** verdicts use a 120-minute minimum window above 45° altitude at the DevDefaults site

#### Scenario: Out-of-range input is corrected, not applied
- **WHEN** a knob holds out-of-range or non-numeric input and Tonight is pressed
- **THEN** the knob's value is restored to a valid in-range number before the pass runs

#### Scenario: Each knob is sized to its digit budget
- **WHEN** the toolbar is rendered
- **THEN** the Duration up-down is wide enough for its 3-digit maximum and the Floor up-down for its decimal maximum, each with its increment/decrement buttons visible without hovering

#### Scenario: Selecting a project fills the knobs
- **WHEN** a project with `minimumtime` 60 and `minimumaltitude` 30 is selected in the dropdown
- **THEN** Duration reads 60 and Floor reads 30, and nothing is journaled

#### Scenario: A stored value outside the old integer ranges fills intact
- **WHEN** a project with `minimumtime` 600 or a fractional `minimumaltitude` is selected
- **THEN** the boxes show the stored values exactly — no clamping, no rounding

#### Scenario: Switching selections discards unsaved edits
- **WHEN** the user edits Duration after selecting project A, then selects project B without pressing Tonight
- **THEN** the boxes refill from project B and nothing was written for project A

### Requirement: Bulk flip of target enables
On button press the system SHALL set `target.active` to the visibility verdict for every target in the
pass's universe, enabling visible targets and disabling non-visible ones. With **All projects**
selected the universe is every target of every project **regardless of project state** — target
enables are sky truth, a separate concept from the project lifecycle; with a single project selected
the universe is that project's targets only, and no other project's targets are read or written.
Targets whose `active` value already matches the verdict SHALL NOT be re-written. Mosaic panel targets
SHALL be evaluated individually like any other target row.

#### Scenario: Visible but disabled target is re-enabled
- **WHEN** the button is pressed and a target judged visible tonight has `active = 0` in a project in the universe
- **THEN** `active` is set to 1

#### Scenario: A Draft project's targets flip like any other
- **WHEN** the All-projects button is pressed and a Draft project contains a target judged visible tonight with `active = 0`
- **THEN** that target's `active` is set to 1 while the project's `state` stays Draft

#### Scenario: Enabled target no longer visible is disabled
- **WHEN** the button is pressed and a target judged not visible tonight has `active = 1`
- **THEN** `active` is set to 0

#### Scenario: Matching value is left untouched
- **WHEN** the button is pressed and a target's `active` already equals its visibility verdict
- **THEN** no edit is journaled for that target

#### Scenario: Scoped press leaves other projects' targets alone
- **WHEN** Tonight is pressed with one project selected while another project contains a target judged not visible tonight with `active = 1`
- **THEN** that other project's target keeps `active = 1` and no edit is journaled for it

### Requirement: Project state derived from applied target enables
After the target-flip batch has been applied, the system SHALL recompute effective target enables from
what actually landed — an applied flip contributes its new value, a refused or failed flip contributes
the target's pre-pass value, an unflipped target its existing value — for every project in the pass's
universe whose state is `Active` or `Inactive` (all such projects in All mode; only the selected
project in scoped mode), and SHALL set each such project's `state` to `Inactive` when it has no
effectively enabled targets, and to `Active` when it has at least one. Only the `Active ↔ Inactive`
transition pair SHALL ever be written — a Draft or Closed project's targets may flip, but its `state`
is never derived or written. Project flips SHALL NOT be derived from intended target states before the
target batch applies, and no project outside the universe SHALL have its `state` written.

#### Scenario: Project with no enabled targets is disabled
- **WHEN** every target of an Active project in the universe ends the pass with `active = 0`
- **THEN** the project's `state` is set to Inactive

#### Scenario: Inactive project regains a visible target
- **WHEN** an Inactive project in the universe ends the pass with at least one target at `active = 1`
- **THEN** the project's `state` is set to Active

#### Scenario: Failed target flip does not orphan a project flip
- **WHEN** an Inactive project's sole visible target has its `active = 1` write refused or failed
- **THEN** the derivation sees the target still disabled and the project's `state` is not flipped to Active

#### Scenario: Whole target batch fails
- **WHEN** the target batch's editor session cannot open, failing every target flip
- **THEN** project derivation runs against the unchanged pre-pass values and emits no flip whose premise did not land

#### Scenario: Scoped press flips only the selected project's state
- **WHEN** Tonight is pressed with one project selected and a different Active project happens to have zero enabled targets
- **THEN** only the selected project's `state` can change; the other project's `state` is untouched

### Requirement: The pass never writes Draft or Closed project state
Target enables and project lifecycle are separate concepts: the pass SHALL flip a Draft or Closed
project's targets like any other project's, but SHALL NOT write such a project's `state` in either
direction — promotion out of (or into) Draft/Closed remains a deliberate hand edit. Draft and Closed
projects SHALL appear in the Project dropdown, fill the knobs on selection, and accept the scoped
constraint write like any other project.

#### Scenario: Draft project's state survives its targets all disabling
- **WHEN** the button is pressed and every target of a Draft project is judged not visible tonight
- **THEN** those targets' `active` values are set to 0 and the project's `state` stays Draft

#### Scenario: Draft project accepts a constraint write
- **WHEN** a Draft project is selected, Floor is changed, and Tonight is pressed
- **THEN** the project's `minimumaltitude` is journaled with the new value and its targets flip per the sky, while its `state` stays Draft

## ADDED Requirements

### Requirement: A scoped press writes the project's constraints before enabling
With a single project selected, the Tonight press SHALL first journal the project's `minimumtime` from
Duration and `minimumaltitude` from Floor — each only when the box value differs from the stored value
— through the ordinary journaled edit path, then run the enable pass using the box values. Settings
flow down (the write applies to every member target at TS plan time by TS's own cascade); state rolls
up (the enable stage derives project `state` from what the sky left enabled). With All projects
selected the press SHALL write no project constraint. The project's display name is deliberately NOT
updated when the altitude changes (names encoding an altitude may go stale; renaming is a planned
follow-up).

#### Scenario: Changed values are journaled then applied
- **WHEN** a project fills Duration 60 / Floor 30, the user sets Floor to 40, and presses Tonight
- **THEN** `minimumaltitude = 40` is journaled for that project (no `minimumtime` edit), and the enable pass runs with Duration 60 / Floor 40 over that project's targets

#### Scenario: Unchanged values write nothing
- **WHEN** a project is selected and Tonight is pressed with both boxes untouched
- **THEN** no constraint edit is journaled and only the enable pass runs

#### Scenario: All mode never writes constraints
- **WHEN** All projects is selected and Tonight is pressed with any Duration/Floor values
- **THEN** no project's `minimumtime` or `minimumaltitude` is written

#### Scenario: The stale name is tolerated
- **WHEN** a project named "Nebulae - Above 45" has its Floor written to 40
- **THEN** the name is unchanged and no rename is journaled
