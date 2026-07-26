## MODIFIED Requirements

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
