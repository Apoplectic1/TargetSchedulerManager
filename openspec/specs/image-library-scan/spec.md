# image-library-scan Specification

## Purpose

Defines what the image-library scan reads — and, decisively, what it never reads. The scan is the sole
source of disk-ACTUAL, so anything it admits becomes a fact the grid reports and anything it excludes
disappears completely; both directions therefore need stating. Covers the capture-tree scope and the two
standing exclusions: the calibration tree (master frames, not acquired light) and non-sidereal targets
(no sidereal plan can describe them). Also covers the per-frame facts the scan must carry through
untouched — including the rotation and plate-solved coordinates the framing dimension is derived from.
Seeded 2026-07-27 by the `skip-comet-targets` change, which added the non-sidereal exclusion and gave the
long-standing calibration skip its first written contract; the framing facts arrived 2026-07-29 with
`rotation-framing-key`.

## Requirements

### Requirement: The scan reads only each target's capture tree
The image-library scan SHALL read light frames only from a target's capture tree, and SHALL ignore every other directory beside it. A target directory with no capture tree SHALL yield no target.

#### Scenario: Sibling directories are ignored
- **WHEN** a target directory holds processing or output folders alongside its capture tree
- **THEN** only the capture tree is read, and frames elsewhere contribute nothing

#### Scenario: A target with no capture tree yields nothing
- **WHEN** a directory under the library root has no capture tree
- **THEN** no target is produced for it, and the scan continues with the others

### Requirement: Calibration trees are never read
The scan SHALL never read the calibration tree within a target's capture tree. It holds master bias, dark and flat frames — calibration, not acquired light — so counting them would inflate every count the library reports.

#### Scenario: Master frames do not become light frames
- **WHEN** a target's capture tree contains a calibration directory holding master frames
- **THEN** none of them appears in any aggregate, and the target's counts reflect only its light frames

#### Scenario: A target whose only frames are calibration yields nothing
- **WHEN** every frame beneath a target's capture tree is calibration
- **THEN** no target is produced for it

### Requirement: Non-sidereal targets are never read
The scan SHALL exclude any target naming a **non-sidereal** object — one whose coordinates change from night to night. No sidereal plan can describe such a target, so it can never be reconciled against one, and every frame of it is acquired by hand at the telescope. Exclusion SHALL happen at the directory walk, so the target is absent from the scan result rather than filtered afterwards by each consumer.

The naming convention identifying them SHALL be matched such that a **sidereal** target whose name merely begins with the same letters is still read.

#### Scenario: A comet target is absent from the scan
- **WHEN** the library contains a comet target directory beside ordinary targets
- **THEN** the scan reports the ordinary targets only, and nothing from the comet

#### Scenario: Its non-conforming filter directories are never published
- **WHEN** a comet target's capture tree nests date-named session folders where filter directories belong
- **THEN** those session names never appear as filter codes anywhere in the scan result

#### Scenario: A sidereal target with a similar name is still read
- **WHEN** a target is named for a sidereal object whose name begins with the same letters as the non-sidereal convention
- **THEN** it is scanned normally

#### Scenario: A surgical scan honours the exclusion too
- **WHEN** a scan is pointed directly at a single non-sidereal target directory
- **THEN** it returns no target, exactly as the whole-library scan would

### Requirement: The scan reads each frame's framing facts
The scan SHALL read from each light frame its rotator sky angle, its mechanical rotator position angle,
and its plate-solved coordinates, so the framing dimension can be derived from the frames themselves. A
frame missing any of these SHALL still be scanned; the absent fact is carried as absent, never defaulted
to a value that could pair or cluster as though recorded.

#### Scenario: Rotation facts survive to the scan result
- **WHEN** a frame records a sky angle, a mechanical angle, or both
- **THEN** the scan result carries the recorded value(s) for that frame's aggregate, unaltered

#### Scenario: A frame without rotation is not invented one
- **WHEN** a frame records neither a sky angle nor a mechanical angle
- **THEN** the frame is counted normally and its framing expression is reported as unknown

### Requirement: The scan publishes a centroid per framing cluster
The scan SHALL publish a plate-solved centroid for each framing cluster within a unit, in addition to the
unit's consensus centroid. A unit whose frames span more than one framing thereby reports where each
framing actually points, rather than one blended position that describes none of them.

#### Scenario: A multi-framing unit reports one centroid per cluster
- **WHEN** a unit's frames form two framing clusters whose fields genuinely differ
- **THEN** the scan result carries each cluster's own centroid

#### Scenario: A single-framing unit is unchanged
- **WHEN** a unit's frames form one framing cluster
- **THEN** its cluster centroid and unit consensus centroid describe the same field, as before this change

### Requirement: The scan reads each frame's sensor geometry
The scan SHALL read each light frame's **sensor pixel dimensions** and carry them, so that a framing
cluster's angular footprint can be derived from the frames themselves alongside the focal length and pixel
size the scan already reads. Dimensions SHALL be taken from the frame format's own **mandatory** image-geometry
declaration rather than from optional header keywords that describe the same thing, so the read has one
source and needs no fallback.

Because the declaration is mandatory, a frame lacking it is a **malformed file**, not a frame with unknown
dimensions. Such a frame SHALL be recorded as unreadable with a reason naming what was expected, on the same
footing as any other corrupt frame — and SHALL NOT be given default dimensions, nor carried with its
dimensions absent, nor counted as if it had been read.

#### Scenario: Sensor dimensions reach the scan result
- **WHEN** a light frame declares its image geometry
- **THEN** the scan result carries that frame's pixel width and height unaltered

#### Scenario: Two sensors are reported as two geometries
- **WHEN** a unit's frames were captured through sensors of different pixel dimensions
- **THEN** the scan result distinguishes them rather than reporting one geometry for the unit

#### Scenario: A frame with no geometry declaration is recorded as unreadable
- **WHEN** a file beneath a capture tree carries no image-geometry declaration
- **THEN** it is recorded as unreadable with a reason naming the missing declaration, and contributes to no
  count or aggregate

#### Scenario: No fabricated dimensions
- **WHEN** a frame's geometry cannot be read
- **THEN** no default or inferred dimensions are attributed to it

### Requirement: Frames the scan could not read are surfaced, never silent
The count of frames the scan recorded as unreadable SHALL be surfaced to the user wherever the scan's results
are presented. A frame that fails to read lowers every count it would have contributed to, so leaving the
record unread would present a **quietly wrong total** — the failure mode this capability's exclusions are
written down to prevent. A scan that read everything SHALL surface nothing, so the indication means
"something was lost", never merely "a scan happened".

#### Scenario: Unreadable frames announce themselves
- **WHEN** a scan records one or more frames as unreadable
- **THEN** the count is visible to the user alongside the scan's results

#### Scenario: A clean scan says nothing
- **WHEN** every frame beneath the capture trees was read
- **THEN** no unreadable-frame indication appears

### Requirement: Angular footprint is derived without applying binning
A frame's angular footprint SHALL be derived from its pixel dimensions, focal length and **recorded pixel
size**, with the binning factors taking no part in the derivation. Writers record the pixel size already
scaled for the binning in use, so the recorded dimensions and the recorded pixel size are already mutually
consistent; multiplying by the binning factor as well would report a field larger than the one imaged.

#### Scenario: A binned frame reports the field it actually imaged
- **WHEN** a frame is captured binned, recording half the pixel dimensions and twice the pixel size of an
  unbinned frame from the same camera and optics
- **THEN** both frames report the same angular footprint

#### Scenario: The footprint follows the optics
- **WHEN** two frames share pixel dimensions and pixel size but differ in focal length
- **THEN** the longer focal length reports the smaller angular footprint
