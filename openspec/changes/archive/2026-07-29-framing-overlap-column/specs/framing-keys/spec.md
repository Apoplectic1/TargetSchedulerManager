# framing-keys Delta

## ADDED Requirements

### Requirement: A framing that is off the plan's footprint reports how much of it the plan asked for
A framing cluster SHALL report an **overlap fraction**: the share of that cluster's own angular footprint
that falls inside the footprint the plan asked for. The plan's footprint SHALL be constructed from **the
same sensor dimensions as the cluster being measured**, centered on the plan target's coordinates, and
rotated to the plan target's rotation.

The fraction SHALL price being off-footprint **for any reason** — a rotation the plan did not ask for, a
pointing the plan did not ask for, or both — because a framing that points elsewhere at the plan's own angle
is the same hazard as one turned away from it. A cluster whose rotation **disagrees** SHALL always report,
however high its overlap; a cluster that **serves** the plan's rotation SHALL report only when its pointing
still leaves it below an on-footprint threshold, so an ordinary on-plan framing prices nothing rather than
restating a full overlap on every row. A cluster whose rotation is not comparable (mechanical-only or
unknown) and one whose plan target expresses no rotation SHALL report **no** overlap fraction: no
orientation may be invented in order to produce a number.

Constructing both footprints from one sensor SHALL make the fraction depend only on the **center offset and
the angle difference** — the framing error itself — so that no *other* framing's sensor can change it: each
framing is measured against its own, and a target's camera history therefore cannot move any of its numbers.

#### Scenario: A rotated stray reports the share the plan asked for
- **WHEN** a target's plan is at 50° and 451 of its frames form a cluster at 60°
- **THEN** that cluster reports the fraction of its own footprint lying inside the plan's 50° footprint, and
  the serving 50° cluster reports none

#### Scenario: A translated framing at the plan's own angle still reports
- **WHEN** a cluster matches the plan's rotation but its centroid sits off the plan's coordinates far enough
  to leave it below the on-footprint threshold
- **THEN** it reports an overlap fraction reflecting the displacement, even though it carries no badge

#### Scenario: A serving framing on the plan's footprint prices nothing
- **WHEN** a cluster's sky rotation agrees with the plan's rotation within tolerance and its pointing leaves
  it on the plan's footprint
- **THEN** it reports no overlap fraction, rather than reporting a full overlap

#### Scenario: A disagreeing framing reports even when its overlap is nearly full
- **WHEN** a cluster's rotation sits just past the tolerance and most of its footprint still lands inside the
  plan's
- **THEN** it still reports its fraction, so a badged row is never left with nothing to read

#### Scenario: An incomparable framing prices nothing
- **WHEN** a cluster's rotation is mechanical-only or unknown, or its plan target expresses no rotation
- **THEN** it reports no overlap fraction, because rotation never entered the comparison

#### Scenario: A framing with no derivable footprint prices nothing
- **WHEN** a cluster's frames carry no sensor geometry or optics to derive a field from
- **THEN** it reports no overlap fraction — absent, and never zero, which would read as no overlap at all

#### Scenario: The number survives a target whose every framing strays
- **WHEN** a target's plan rotation matches none of its framing clusters
- **THEN** every one of them still reports an overlap fraction, because the comparand is the plan and not
  another cluster

#### Scenario: A neighbouring framing's camera does not move the number
- **WHEN** a cluster captured on one sensor sits beside clusters of the same target captured on another
- **THEN** its overlap fraction is what it would be if those other clusters did not exist, because it is
  measured against a plan footprint built from its own sensor

### Requirement: A cluster spanning sensors is measured by its dominant sensor and marked
A framing cluster whose frames span more than one sensor geometry SHALL take its **dominant** sensor's
footprint and SHALL be marked as spanning sensors. Dimensions from two sensors SHALL NOT be blended into an
averaged footprint, because such a rectangle describes neither sensor and would report a measurement of a
field that was never imaged.

#### Scenario: A mixed-sensor cluster is measured, not averaged
- **WHEN** a cluster holds frames from a wide sensor and a smaller square one
- **THEN** its footprint is the more numerous sensor's, and the cluster is marked as spanning sensors

#### Scenario: A single-sensor cluster carries no marking
- **WHEN** every frame of a cluster shares one sensor geometry
- **THEN** no sensor-spanning marking appears

### Requirement: Overlap is diagnostic and never affects crediting
The overlap fraction SHALL NOT influence what any plan is credited with. Crediting SHALL remain the boolean
serve / does-not-serve decision: a frame whose framing serves the target's rotation counts in full, and one
whose framing does not counts not at all. A partially overlapping frame is not a fractional frame — whether
it belongs in a stack is a decision for the integration tool, not a proportion TSM invents.

#### Scenario: A low overlap does not reduce a credited count
- **WHEN** a cluster serves its target's rotation and also reports a low overlap fraction against a
  different plan's framing
- **THEN** its frames credit that target in full, unscaled by any overlap value

#### Scenario: A high overlap does not rescue a non-serving cluster
- **WHEN** a cluster fails the rotation comparison but its footprint overlaps the plan's substantially
- **THEN** none of its frames credit the plan

## MODIFIED Requirements

### Requirement: The grid shows each row's framing
A disk-backed row SHALL display its framing cluster's rotation — the sky angle where expressed, the
mechanical angle marked as such otherwise, and an explicit unknown marking where frames record neither.
Rollup rows SHALL show the single framing value when uniform and a mixed marking otherwise. The framing
display SHALL NOT participate in row sorting, consistent with the capture-configuration columns.

A disk row whose framing cluster disagrees with the plan's framing SHALL carry a warning-severity
row-scoped `framing` badge, so stray framings are findable through the badge filter rather than only by
scrolling for split rows. The badge SHALL display at the **deepest visible level**: always on the target
summary row; on an intermediate rollup while it is collapsed (the triggering line is hidden inside it);
and on the triggering source line itself once the rollup is expanded — at which point the rollup SHALL NOT
repeat it. This display rule SHALL apply to **every row-scoped badge the same way** (camera provenance and
framing alike), not to framing alone. The rollup counts as flagged throughout, so filtering to flagged
rows keeps the target reachable.

The `framing` badge SHALL carry its **overlap fraction inline** — `framing 57%` — on the **deepest visible
line only**: the source line whose frames it prices (or a standalone disk row, which is its own deepest
level). Rollups and target summary rows SHALL show the bare token — a rollup can span clusters with
different fractions, so no single number may sit above the lines. The percentage SHALL be a display
decoration only: search, the flagged filter, header aggregation and every other consumer of the badge
vocabulary SHALL reason over the bare token, and a decorated token SHALL classify (severity, row scope)
exactly as the bare one. A badged row whose frames carry no derivable footprint SHALL show the bare badge —
unpriced is not priced at zero.

The overlap facts that carry no badge SHALL surface in the **ambiguity report** as informational entries: a
framing at the plan's own angle whose displacement leaves it below the on-footprint threshold (rotation
serves, so the grid shows no badge), and the dominant-sensor qualifier for a priced framing spanning
sensors. Frames the scan could not read SHALL appear in the same report as **action items** naming each
path and reason, with their count on the load status line when nonzero and silence when zero.

#### Scenario: A separated framing row is readable at a glance
- **WHEN** a target renders a `Both` row and a Disk row split only by framing
- **THEN** each row shows its own rotation value, making the framing difference the visible explanation
  for the split

#### Scenario: A mechanical angle is not dressed as a sky angle
- **WHEN** a row's cluster expresses only mechanical rotation
- **THEN** its displayed value is visibly marked mechanical

#### Scenario: Rollups mark mixed framings
- **WHEN** a target's rows span more than one framing cluster
- **THEN** the target-level rollup marks framing as mixed rather than showing one value

#### Scenario: A stray framing is findable by badge
- **WHEN** a disk row's framing cluster disagrees with the plan's rotation
- **THEN** the row carries a warning-severity `framing` badge, and filtering by it surfaces every such row
  in the library

#### Scenario: The badge follows the deepest visible level
- **WHEN** the disagreeing disk line sits nested under a collapsed rollup within its target
- **THEN** the target summary row and the rollup both show `framing`; expanding the rollup moves it — the
  now-visible triggering line shows it and the rollup no longer repeats it — and the rollup surfaces under
  a flagged-only filter in both states

#### Scenario: An agreeing framing carries no badge
- **WHEN** a disk row's framing agrees with the plan, or its target's plan expresses no rotation
- **THEN** no `framing` badge appears on it

#### Scenario: A badged line states how far off it is
- **WHEN** a disk source line carries the `framing` badge and its cluster's footprint was derivable
- **THEN** the badge itself reads `framing N%` — the share of its footprint the plan asked for — on that
  line and nowhere above it

#### Scenario: No number ever sits above the lines
- **WHEN** a rollup or target summary row shows the `framing` token for a stray hidden beneath it
- **THEN** the token is bare, and expanding down to the source line is where the percentage appears

#### Scenario: The percentage is invisible to the badge vocabulary
- **WHEN** the user searches, filters to flagged rows, or reads a header's aggregated badges
- **THEN** the results are identical to a build with no percentages: the decoration exists only where a
  deepest visible line renders its own badge

#### Scenario: Unpriced is not priced at zero
- **WHEN** a badged line's frames carry no derivable footprint
- **THEN** the badge shows bare `framing` — never `framing 0%`

#### Scenario: An off-plan pointing that serves speaks in the report
- **WHEN** a framing matches the plan's angle but its displacement leaves it below the on-footprint
  threshold
- **THEN** the grid shows no badge, and the ambiguity report carries an informational entry naming the
  target, the cell, and the fraction

#### Scenario: Unreadable frames are action items with paths
- **WHEN** the scan records files it could not read
- **THEN** the load status line shows their count, and the ambiguity report lists each path and reason as an
  action item; a clean scan shows neither
