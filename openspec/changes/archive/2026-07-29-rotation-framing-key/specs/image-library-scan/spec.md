# image-library-scan Delta

## ADDED Requirements

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
