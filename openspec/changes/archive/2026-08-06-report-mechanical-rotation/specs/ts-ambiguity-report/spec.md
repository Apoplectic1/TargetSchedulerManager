# ts-ambiguity-report — delta

## ADDED Requirements

### Requirement: Mechanical-only framings are enumerated as informational items
The report's informational section SHALL list every in-scope target whose disk framings express only
mechanical rotation (no sky angle recorded), naming the target with its project prefix, the folded
mechanical angle(s), and the number of frames, with a pointer to the measurement fix (plate-solving
the frames). These are informational, not action items — a mechanical angle is a missing measurement,
not a slipped authoring convention.

#### Scenario: Mechanical framing listed
- **WHEN** an in-scope target's framing carries mechanical-only rotation
- **THEN** the informational section names the target, its folded `°(M)` angle(s), and its frame count

#### Scenario: Sky framing not listed
- **WHEN** a target's framings all express sky rotation
- **THEN** no mechanical-rotation line appears for it
