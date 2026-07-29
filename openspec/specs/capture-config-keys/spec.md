# capture-config-keys Specification

## Purpose

Treats the capture configuration — gain, offset, binning, and camera — as first-class reconciliation
data rather than incidental metadata. Defines which dimensions key the disk plane (target, filter,
purpose, exposure, gain, offset, binning, framing cluster), which key the TS plane (the same set as
expressed by the plan's exposure template, minus camera), and the rule that decides when the two planes
pair into one `Both` row versus separating into distinct TS and Disk rows. Seeded 2026-07-27 by the
`capture-config-keys` change; framing joined the key set 2026-07-29 via `rotation-framing-key`, whose
`framing-keys` capability owns how a framing cluster is formed and compared.

## Requirements

### Requirement: The disk plane is keyed by its capture configuration
A disk bucket SHALL be identified by target, filter, purpose, whole-second exposure, **gain**, **offset**,
**binning**, and **framing cluster**. Frames differing in any of these SHALL occupy separate buckets,
because such frames do not combine into one integration. The disk plane is a record of what was captured;
its identity SHALL be derived from the frames themselves and SHALL never be adjusted to agree with any
plan. (Framing-cluster formation — fold-180 rotation plus centroid — is defined by the `framing-keys`
capability.)

#### Scenario: Frames at two gains do not share a bucket
- **WHEN** a target's L frames at 300 s include some captured at gain 53 and some at gain 0
- **THEN** they occupy two disk buckets, each reporting its own frame count and integration time

#### Scenario: Frames at two offsets do not share a bucket
- **WHEN** a target's H frames at 600 s include some at offset 10 and some at offset 50
- **THEN** they occupy two disk buckets

#### Scenario: Frames at two framings do not share a bucket
- **WHEN** a target's H frames at 600 s were captured under two framing clusters
- **THEN** they occupy two disk buckets, each reporting its own frame count and integration time

#### Scenario: Identical configuration stays one bucket
- **WHEN** every frame for a (target, filter, purpose, exposure) combination shares one gain, offset,
  binning and framing cluster
- **THEN** exactly one disk bucket results, as before this change

### Requirement: The TS plane is keyed by what the exposure template expresses
A TS bucket SHALL be identified by target, filter, purpose, effective exposure, **gain**, **offset**, and **binning**, taken from the plan's exposure template. The TS plane describes intended future capture; a TS bucket SHALL carry no camera, because a Target Scheduler profile does not fix one.

#### Scenario: Template values populate the TS bucket
- **WHEN** a plan resolves to a template specifying gain 111, offset 10, binning 1
- **THEN** its TS bucket carries those values, and its row displays them

#### Scenario: A TS bucket has no camera
- **WHEN** a TS row renders its camera cell
- **THEN** it shows the em dash, because the plan does not name a camera

### Requirement: A row pairs into Both only when every shared key matches
A disk bucket and a TS bucket SHALL render as one `Both` row **if and only if** they agree on every key
both planes express — target, filter, purpose, exposure, gain, offset, binning, and **rotation where both
planes express a comparable sky rotation** (the fold-180 comparison defined by the `framing-keys`
capability). Where they disagree on any of these, the grid SHALL render a separate TS row and one or more
Disk rows rather than merging them. The separation is the diagnostic: it shows that captured history does
not describe what is planned. Keys only one plane expresses SHALL NOT participate in this test and SHALL
NOT prevent pairing.

#### Scenario: Matching configuration pairs
- **WHEN** a disk bucket of H frames at 600 s, gain 111, offset 10, binning 1 meets a plan specifying the
  same, with the bucket's framing agreeing with the plan's rotation
- **THEN** one `Both` row renders, showing the plan's desired count beside the disk frame count

#### Scenario: A gain disagreement separates the planes
- **WHEN** a plan specifies gain 0 at 60 s but every frame at 60 s was captured at gain 53
- **THEN** the grid renders a TS row carrying the desired count and a Disk row carrying the frames, and no `Both` row for that bucket

#### Scenario: An offset disagreement separates the planes
- **WHEN** a plan specifies offset 10 and a subset of the target's frames were captured at offset 50
- **THEN** the matching frames pair into a `Both` row and the offset-50 frames render as their own Disk row

#### Scenario: A rotation disagreement separates the planes
- **WHEN** a plan's rotation is 50° and a subset of the target's frames were captured at 60°
- **THEN** the 50° frames pair into `Both` rows and the 60° frames render as their own Disk rows

#### Scenario: A camera difference never prevents pairing
- **WHEN** a disk bucket matching a plan on every shared key was captured on a camera the plan cannot name
- **THEN** the row still renders as `Both`

### Requirement: Camera is displayed, never used to pair
Camera SHALL be carried and displayed on disk-side rows but SHALL NOT participate in the pairing test, because the Target Scheduler cannot express a camera: a single profile is used with cameras exchanged between sessions, so a plan's `desired` count is camera-agnostic. The system SHALL NOT attribute any plan to any camera, nor infer how many frames a given camera still owes.

#### Scenario: Camera is shown on a disk-backed row
- **WHEN** a row carries disk frames captured through a known camera directory
- **THEN** the row's camera cell shows that camera's alias

#### Scenario: No per-camera goal is derived
- **WHEN** a target's frames for one plan were captured on more than one camera
- **THEN** the system reports counts and integration without apportioning the plan's desired count between cameras

### Requirement: Camera is taken from the capture directory and shown as an alias
The camera SHALL be taken from the capture directory name under a target's `Captures` tree. For display it SHALL be resolved to a short alias, and a directory resolving to no known alias SHALL be reported rather than shown raw as though understood.

#### Scenario: A known camera directory resolves to its alias
- **WHEN** frames are read from a capture directory whose name identifies a known camera
- **THEN** the row displays that camera's alias

#### Scenario: An unknown camera directory is reported
- **WHEN** a capture directory name resolves to no known camera alias
- **THEN** the affected rows carry a warning-severity `camera` badge

#### Scenario: A directory disagreeing with its frames is reported
- **WHEN** a capture directory's name and the camera identifier recorded inside its frames disagree
- **THEN** the affected rows carry a warning-severity `cam≠` badge naming the disagreement

### Requirement: Offset is read as recorded, without further scaling
The offset SHALL be read from each frame exactly as recorded, with no per-camera scaling applied. Frames are written with the offset already in the scale the Target Scheduler's templates use, so any further conversion would produce a value comparable to neither plane.

#### Scenario: A recorded offset survives to the grid unchanged
- **WHEN** a frame records an offset of 10
- **THEN** its bucket reports offset 10, and it pairs with a plan specifying offset 10

#### Scenario: A differing offset does not silently agree
- **WHEN** a frame records an offset of 50 and the plan specifies 10
- **THEN** the two do not pair, and both values are visible

### Requirement: Exposure template names are unique
Each Target Scheduler exposure template SHALL have a name distinct from every other template's. Two templates sharing a name SHALL be reported as an authoring error the user repairs in the Target Scheduler's own interface, because the template name is load-bearing: it also determines whether a plan is classified as a Light or a Stars capture.

#### Scenario: Duplicate template names are reported
- **WHEN** two exposure templates share one name
- **THEN** the condition is reported as repairable authoring, naming both templates

#### Scenario: Distinct names pass
- **WHEN** every exposure template name is distinct
- **THEN** nothing is reported
