# framing-keys Delta

## Purpose

Treats a target's **framing** — the (field-center, sky-rotation) pair that decides whether frames share a
footprint and can integrate together — as reconciliation data. Defines how disk frames group into framing
clusters (rotation folded mod 180° plus a per-cluster centroid), why a pier flip never splits a framing,
how a framing cluster pairs against the plan's rotation, and how rotation the plan cannot be compared
against (mechanical-only) is carried without being converted or guessed.

## ADDED Requirements

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
scrolling for split rows. The badge marks the hazard; quantifying it (footprint-overlap percentage) is
deliberately out of scope.

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

#### Scenario: An agreeing framing carries no badge
- **WHEN** a disk row's framing agrees with the plan, or its target's plan expresses no rotation
- **THEN** no `framing` badge appears on it
