# visible-tonight-toggle — delta spec

## ADDED Requirements

### Requirement: Visibility predicate
The system SHALL judge a target "visible tonight" if and only if the target has a single contiguous
window of at least the user's Duration (minutes) above the user's Horizon altitude floor (degrees)
between tonight's astronomical dusk and dawn at the configured site. Two shorter windows that only sum
to the minimum SHALL NOT qualify. TS's own altitude rules (`minimumaltitude`, custom horizon,
`horizonoffset`, `minimumtime`) SHALL NOT be consulted.

#### Scenario: Long-enough contiguous window
- **WHEN** a target is above the Horizon floor for one contiguous stretch of at least the Duration during tonight's astronomical night
- **THEN** the target is judged visible tonight

#### Scenario: Sliver window below the threshold
- **WHEN** a target's only above-floor stretch tonight is shorter than the Duration
- **THEN** the target is judged not visible tonight

#### Scenario: Never above the floor tonight
- **WHEN** a target never exceeds the Horizon floor between astronomical dusk and dawn
- **THEN** the target is judged not visible tonight

#### Scenario: Horizon floor gates a low-arc target
- **WHEN** the Horizon knob is 30 and a target is above 0° for hours tonight but never climbs above 30° altitude
- **THEN** the target is judged not visible tonight

#### Scenario: TS altitude configuration is ignored
- **WHEN** a target clears the user's Horizon floor for a qualifying stretch tonight but its project's `minimumaltitude` is higher than the target ever reaches
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

### Requirement: Project state derived from post-pass target enables
After target flips, the system SHALL set each processed project's `state` to `Inactive` when the project
has no enabled targets, and to `Active` when it has at least one enabled target. Only the
`Active ↔ Inactive` transition pair SHALL ever be written.

#### Scenario: Project with no enabled targets is disabled
- **WHEN** every target of an Active project ends the pass with `active = 0`
- **THEN** the project's `state` is set to Inactive

#### Scenario: Inactive project regains a visible target
- **WHEN** an Inactive project ends the pass with at least one target at `active = 1`
- **THEN** the project's `state` is set to Active

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
minutes, range 15–480, default 30; and a Horizon numeric up-down in whole degrees, range 0–89,
default 30. A Find button SHALL run the pass with the knobs' current values.

#### Scenario: Knob values drive the verdicts
- **WHEN** Find is pressed with Duration 120 and Horizon 45
- **THEN** verdicts use a 120-minute minimum window above 45° altitude at the DevDefaults site

#### Scenario: Out-of-range input is corrected, not applied
- **WHEN** a knob holds out-of-range or non-numeric input and Find is pressed
- **THEN** the knob's value is restored to a valid in-range number before the pass runs

### Requirement: One press, applied summary
The Find button SHALL apply without a confirmation dialog and, on completion, report on the status
line a summary of targets enabled, targets disabled, targets unchanged, and projects flipped.

#### Scenario: Summary after a pass
- **WHEN** the pass completes having enabled 3 targets, disabled 5, and flipped 1 project to Inactive
- **THEN** the user sees a status-line summary reporting those counts
