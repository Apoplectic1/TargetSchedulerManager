# Delta: visible-tonight-toggle (visible-tonight-applied-states)

## MODIFIED Requirements

### Requirement: Project state derived from applied target enables

After the target-flip batch has been applied, the system SHALL recompute each processed project's
effective target enables from what actually landed — an applied flip contributes its new value, a
refused or failed flip contributes the target's pre-pass value, an unflipped target its existing value —
and SHALL set the project's `state` to `Inactive` when the project has no effectively enabled targets,
and to `Active` when it has at least one. Only the `Active ↔ Inactive` transition pair SHALL ever be
written. Project flips SHALL NOT be derived from intended target states before the target batch applies.

#### Scenario: Project with no enabled targets is disabled

- **WHEN** every target of an Active project ends the pass with `active = 0`
- **THEN** the project's `state` is set to Inactive

#### Scenario: Inactive project regains a visible target

- **WHEN** an Inactive project ends the pass with at least one target at `active = 1`
- **THEN** the project's `state` is set to Active

#### Scenario: Failed target flip does not orphan a project flip

- **WHEN** an Inactive project's sole visible target has its `active = 1` write refused or failed
- **THEN** the derivation sees the target still disabled and the project's `state` is not flipped to Active

#### Scenario: Whole target batch fails

- **WHEN** the target batch's editor session cannot open, failing every target flip
- **THEN** project derivation runs against the unchanged pre-pass values and emits no flip whose premise did not land

### Requirement: The pass holds the busy exclusion and applies as two sequenced batches

The pass SHALL hold the bulk-operation exclusion (see `busy-exclusion`) from before planning until after
the last flip is applied, and SHALL apply its flips as two sequenced off-UI-thread batches — the target
flips, then the project flips derived from the target batch's outcomes — each on one editor session.
The exclusion SHALL NOT be released between the batches, so no other bulk operation and no row edit can
execute anywhere inside the pass. Per-flip outcomes SHALL be preserved in both batches: an individual
flip failure is counted and logged while the remaining flips still apply, exactly as in the per-edit
path, and the reported failure count SHALL sum across both batches. The closing grid reload SHALL run
after the exclusion is released.

#### Scenario: No interleaving window inside the pass

- **WHEN** a pass is applying 80 target flips followed by 5 project flips
- **THEN** no other bulk operation and no row edit can execute between any two flips, including between the two batches

#### Scenario: One editor session per batch

- **WHEN** a pass applies N target flips and M project flips
- **THEN** one editor session performs all N target writes and a second performs all M project writes, each write individually verified and journaled

#### Scenario: Per-flip failure does not abort its batch

- **WHEN** one flip in either batch fails verification
- **THEN** the remaining flips in that batch still apply, and the status summary reports the combined failure count
