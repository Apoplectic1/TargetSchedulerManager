# image-library-scan Delta

## ADDED Requirements

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
