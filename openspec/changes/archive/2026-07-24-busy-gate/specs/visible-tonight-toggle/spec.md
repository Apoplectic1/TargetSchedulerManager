# visible-tonight-toggle — delta

## ADDED Requirements

### Requirement: The pass holds the busy exclusion and applies as one batch
The pass SHALL hold the bulk-operation exclusion (see `busy-exclusion`) from before planning until after
the last flip is applied, and SHALL apply its flips as a single off-UI-thread batch on one editor
session — the UI thread is not re-entered between flips, and one editor connection serves the whole
batch. Per-flip outcomes SHALL be preserved: an individual flip failure is counted and logged while the
remaining flips still apply, exactly as in the per-edit path. The closing grid reload SHALL run after the
exclusion is released.

#### Scenario: No interleaving window between flips
- **WHEN** a pass is applying 80 flips
- **THEN** no other bulk operation and no row edit can execute between any two flips

#### Scenario: One editor session for the batch
- **WHEN** a pass applies N flips
- **THEN** one editor session performs all N writes, each individually verified and journaled

#### Scenario: Per-flip failure does not abort the batch
- **WHEN** one flip in the batch fails verification
- **THEN** the remaining flips still apply, and the status summary reports the failure count
