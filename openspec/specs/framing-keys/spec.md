# framing-keys Specification

## Purpose

Treats a target's **framing** — the (field-center, sky-rotation) pair that decides whether frames share a
footprint and can integrate together — as reconciliation data. Defines how disk frames group into framing
clusters (rotation folded mod 180° plus a per-cluster centroid), why a pier flip never splits a framing,
how a framing cluster pairs against the plan's rotation, and how rotation the plan cannot be compared
against (mechanical-only) is carried without being converted or guessed. Seeded 2026-07-29 by the
`rotation-framing-key` change.

## Requirements

### Requirement: Disk frames group into framing clusters
Within one scan unit, frames SHALL group into **framing clusters** by sky rotation folded mod 180° and by
field-center proximity, each cluster carrying its own plate-solved centroid. Frames whose rotation or
center places them outside every existing cluster's tolerance SHALL form their own cluster, however few
they are — a single stray frame is a cluster, because low-count off-footprint framings are precisely the
integration hazard this capability exists to surface. Framing SHALL be derived from the frames themselves
and SHALL never be adjusted to agree with any plan.

#### Scenario: Two rotations become two clusters
- **WHEN** a target's frames were captured partly at sky rotation 20° and partly at 160°
- **THEN** they form two framing clusters, each reporting its own frame count and centroid

#### Scenario: A single stray framing is not absorbed
- **WHEN** 104 frames share one framing and one frame was captured rotated 135° away from it
- **THEN** the stray frame forms its own cluster rather than folding into the majority

#### Scenario: A translated stray separates at unchanged rotation
- **WHEN** a frame's rotation matches its siblings but its plate-solved center lies far outside their
  centroid tolerance
- **THEN** it forms its own framing cluster, because footprint is center as much as angle

#### Scenario: Uniform framing stays one cluster
- **WHEN** every frame of a unit shares one rotation within jitter and one field center
- **THEN** exactly one framing cluster results, and the unit reads as before this change

### Requirement: A pier flip is the same framing
Frames whose sky rotations sit 180° apart and whose centroids coincide within tolerance SHALL belong to
one framing cluster. A rectangle rotated 180° about the same center covers the identical footprint, so a
meridian flip — a routine acquisition event — changes nothing about what integrates with what. The
centroid coincidence test SHALL guard the fold: 180°-apart groups whose centers genuinely differ are
different framings, not a flip.

#### Scenario: Flip frames merge
- **WHEN** a unit's frames sit at sky rotations 0° and 180° with centroids a small fraction of the field
  apart
- **THEN** they form one framing cluster

#### Scenario: A flipped plan still pairs
- **WHEN** the plan's rotation is 0° and every frame was captured at 180°
- **THEN** the rotation comparison treats them as agreeing

### Requirement: Rotation joins the pairing test only as expressed by both planes
A framing cluster's **sky** rotation SHALL be compared fold-180 against the plan target's rotation, and
only the cluster(s) agreeing within tolerance SHALL be eligible to pair into `Both`; disagreeing clusters
SHALL render as separate Disk rows. When either side does not express a comparable sky rotation — the
plan target carries no rotation, or the cluster's rotation is mechanical-only — rotation SHALL NOT
participate in the pairing test and SHALL NOT prevent pairing, exactly as camera does not.

#### Scenario: The plan-matching cluster pairs; the old framing separates
- **WHEN** a target's frames form clusters at 50° and 60° and the plan's rotation is 50°
- **THEN** the 50° cluster's buckets are eligible to pair into `Both` rows and the 60° cluster's buckets
  render as Disk rows

#### Scenario: The majority does not win — the plan does
- **WHEN** the cluster agreeing with the plan's rotation holds fewer frames than a disagreeing cluster
- **THEN** the agreeing cluster still pairs and the larger cluster still separates

#### Scenario: A plan without rotation pairs on the remaining keys
- **WHEN** a plan target carries no rotation value
- **THEN** no rotation comparison occurs and pairing is decided by the other shared keys alone

#### Scenario: A mechanical-only cluster never fails pairing on rotation
- **WHEN** a cluster's frames carry only a mechanical rotator angle
- **THEN** rotation does not participate in that cluster's pairing test

### Requirement: Mechanical-only rotation is carried, never converted
A cluster whose frames record only a mechanical rotator position SHALL be presented as expressing
mechanical rotation, distinguishable from sky rotation. The system SHALL NOT infer a sky angle from a
mechanical angle: the mechanical-to-sky zero point shifts when the camera is remounted, so a conversion
would silently mislabel exactly the multi-framing targets this capability exists to expose. Mechanical
angles SHALL still group frames into clusters within their unit, because a mechanical difference within
one unit is a real framing difference.

#### Scenario: Mechanical clusters still separate framings
- **WHEN** a unit's frames carry only mechanical angles, in two groups far apart fold-180
- **THEN** the unit reports two framing clusters

#### Scenario: No sky angle is fabricated
- **WHEN** a mechanical-only cluster is displayed or compared
- **THEN** it is marked as mechanical, and no derived sky value appears anywhere

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
