# visible-tonight-toggle Specification

## Purpose

The Visible-Tonight toolbar group: one Tonight press reconciles `target.active` / `project.state` with
tonight's sky — a target is visible iff it has a single contiguous window of at least the toolbar's
Duration above the toolbar's Floor altitude during tonight's astronomical night at the configured
site. Deliberately independent of TS's own altitude rules (TS re-applies those at plan time); flips ride
the ordinary journaled edit path, so pushing remains optional. Meridian-flip downtime is likewise out of
scope by decision (2026-07-24): pier flips are handled by TS/NINA at runtime, and imaging order/timing is
unknowable at planning time, so a target that may become temporarily unavailable during a pier flip still
counts as visible.

## Requirements

### Requirement: Visibility predicate
The system SHALL judge a target "visible tonight" if and only if the target has a single contiguous
window of at least the user's Duration (minutes) above the user's Floor altitude (degrees)
between tonight's astronomical dusk and dawn at the configured site. Two shorter windows that only sum
to the minimum SHALL NOT qualify. TS's own altitude rules (`minimumaltitude`, custom horizon,
`horizonoffset`, `minimumtime`) SHALL NOT be consulted.

#### Scenario: Long-enough contiguous window
- **WHEN** a target is above the Floor for one contiguous stretch of at least the Duration during tonight's astronomical night
- **THEN** the target is judged visible tonight

#### Scenario: Sliver window below the threshold
- **WHEN** a target's only above-floor stretch tonight is shorter than the Duration
- **THEN** the target is judged not visible tonight

#### Scenario: Never above the floor tonight
- **WHEN** a target never exceeds the Floor between astronomical dusk and dawn
- **THEN** the target is judged not visible tonight

#### Scenario: Floor gates a low-arc target
- **WHEN** the Floor knob is 30 and a target is above 0° for hours tonight but never climbs above 30° altitude
- **THEN** the target is judged not visible tonight

#### Scenario: TS altitude configuration is ignored
- **WHEN** a target clears the user's Floor for a qualifying stretch tonight but its project's `minimumaltitude` is higher than the target ever reaches
- **THEN** the target is still judged visible tonight

### Requirement: Bulk flip of target enables
On button press the system SHALL set `target.active` to the visibility verdict for every target belonging
to a project whose state is `Active` or `Inactive`, enabling visible targets and disabling non-visible
ones. Targets whose `active` value already matches the verdict SHALL NOT be re-written. Mosaic panel
targets SHALL be evaluated individually like any other target row.

#### Scenario: Visible but disabled target is re-enabled
- **WHEN** the button is pressed and a target judged visible tonight has `active = 0` in an Active project
- **THEN** `active` is set to 1

#### Scenario: Enabled target no longer visible is disabled
- **WHEN** the button is pressed and a target judged not visible tonight has `active = 1`
- **THEN** `active` is set to 0

#### Scenario: Matching value is left untouched
- **WHEN** the button is pressed and a target's `active` already equals its visibility verdict
- **THEN** no edit is journaled for that target

### Requirement: Project state derived from applied target enables
After the target-flip batch has been applied, the system SHALL recompute each processed project's
effective target enables from what actually landed — an applied flip contributes its new value, a
refused or failed flip contributes the target's pre-pass value, an unflipped target its existing value —
and SHALL set the project's `state` to `Inactive` when the project has no effectively enabled targets,
and to `Active` when it has at least one. Only the `Active ↔ Inactive` transition pair SHALL ever be
written. Project flips SHALL NOT be derived from intended target states before the target batch applies.

#### Scenario: Project with no enabled targets is disabled
- **WHEN** every target of an Active project ends the pass with `active = 0`
- **THEN** the project's `state` is set to Inactive

#### Scenario: Inactive project regains a visible target
- **WHEN** an Inactive project ends the pass with at least one target at `active = 1`
- **THEN** the project's `state` is set to Active

#### Scenario: Failed target flip does not orphan a project flip
- **WHEN** an Inactive project's sole visible target has its `active = 1` write refused or failed
- **THEN** the derivation sees the target still disabled and the project's `state` is not flipped to Active

#### Scenario: Whole target batch fails
- **WHEN** the target batch's editor session cannot open, failing every target flip
- **THEN** project derivation runs against the unchanged pre-pass values and emits no flip whose premise did not land

### Requirement: Draft and Closed projects are excluded
The system SHALL NOT read, evaluate, or write projects whose `state` is `Draft` or `Closed`, nor any of
their child targets.

#### Scenario: Draft project untouched
- **WHEN** the button is pressed while a Draft project contains targets judged not visible tonight
- **THEN** the project's `state` and all its targets' `active` values are unchanged and no edits are journaled for them

### Requirement: Flips are ordinary journaled edits
All writes SHALL go through the existing schema-driven edit path (`target.active`, `project.state`) so
they are journaled, written back to the local working copy, reflected in the dirty badge, and replayed
only by the existing reviewed Push. The button SHALL NOT write to the remote (BIRDWATCHER) database, and
pushing SHALL remain optional.

#### Scenario: Button edits travel like hand edits
- **WHEN** the button flips two targets and one project
- **THEN** three field edits appear in the journal exactly as if made from the grid, and the dirty badge reflects them

#### Scenario: No remote writes at press time
- **WHEN** the button is pressed
- **THEN** the remote TS database is not opened for writing

### Requirement: Site and knob input contract
The system SHALL obtain the observing site (latitude, longitude, time zone, elevation) from
`DevDefaults` constants — the sole location input to the night-window and visibility computations —
and the predicate knobs from the toolbar's Visible-Tonight group: a Duration numeric up-down in whole
minutes, range 15–480, default 30; and a Floor numeric up-down in whole degrees, range 0–89,
default 30. A Tonight button SHALL run the pass with the knobs' current values.

#### Scenario: Knob values drive the verdicts
- **WHEN** Tonight is pressed with Duration 120 and Floor 45
- **THEN** verdicts use a 120-minute minimum window above 45° altitude at the DevDefaults site

#### Scenario: Out-of-range input is corrected, not applied
- **WHEN** a knob holds out-of-range or non-numeric input and Tonight is pressed
- **THEN** the knob's value is restored to a valid in-range number before the pass runs

#### Scenario: Each knob is sized to its digit budget
- **WHEN** the toolbar is rendered
- **THEN** the Duration up-down is wide enough for its 3-digit maximum and the Floor up-down for its 2-digit maximum, each with its increment/decrement buttons visible without hovering

### Requirement: One press, applied summary
The Tonight button SHALL apply without a confirmation dialog and, on completion, report on the status
line a summary of targets enabled, targets disabled, targets unchanged, and projects flipped.

#### Scenario: Summary after a pass
- **WHEN** the pass completes having enabled 3 targets, disabled 5, and flipped 1 project to Inactive
- **THEN** the user sees a status-line summary reporting those counts

### Requirement: The pass holds the busy exclusion and applies as two sequenced batches
The pass SHALL hold the bulk-operation exclusion (see `busy-exclusion`) from before planning until after
the last flip is applied, and SHALL apply its flips as two sequenced off-UI-thread batches — the target
flips, then the project flips derived from the target batch's outcomes — each on one editor session.
The exclusion SHALL NOT be released between the batches, so no other bulk operation and no row edit can
execute anywhere inside the pass. Per-flip outcomes SHALL be preserved in both batches: an individual
flip failure is counted and logged while the remaining flips still apply, exactly as in the per-edit
path, and the reported failure count SHALL sum across both batches. The closing grid reload SHALL run
after the exclusion is released.

#### Scenario: No interleaving window inside the pass
- **WHEN** a pass is applying 80 target flips followed by 5 project flips
- **THEN** no other bulk operation and no row edit can execute between any two flips, including between the two batches

#### Scenario: One editor session per batch
- **WHEN** a pass applies N target flips and M project flips
- **THEN** one editor session performs all N target writes and a second performs all M project writes, each write individually verified and journaled

#### Scenario: Per-flip failure does not abort its batch
- **WHEN** one flip in either batch fails verification
- **THEN** the remaining flips in that batch still apply, and the status summary reports the combined failure count
