# capture-config-keys Delta

## MODIFIED Requirements

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
- **THEN** the grid renders a TS row carrying the desired count and a Disk row carrying the frames, and no
  `Both` row for that bucket

#### Scenario: An offset disagreement separates the planes
- **WHEN** a plan specifies offset 10 and a subset of the target's frames were captured at offset 50
- **THEN** the matching frames pair into a `Both` row and the offset-50 frames render as their own Disk row

#### Scenario: A rotation disagreement separates the planes
- **WHEN** a plan's rotation is 50° and a subset of the target's frames were captured at 60°
- **THEN** the 50° frames pair into `Both` rows and the 60° frames render as their own Disk rows

#### Scenario: A camera difference never prevents pairing
- **WHEN** a disk bucket matching a plan on every shared key was captured on a camera the plan cannot name
- **THEN** the row still renders as `Both`
