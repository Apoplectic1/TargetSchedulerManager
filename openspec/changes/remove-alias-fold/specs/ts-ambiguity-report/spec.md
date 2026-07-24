# ts-ambiguity-report — delta spec (alias premise removed)

> NOTE: the `ts-ambiguity-report` capability is not yet in main specs — it lives in the still-active
> `ts-ambiguity-report` change, whose delta this change amends **in place** to the exact text below.
> If that change archives first, syncing this delta is a no-op (text already matches); if this change
> archives first, there is nothing in main to modify and the amendment travels with the other change.

## MODIFIED Requirements

### Requirement: TS-internal checks cover what the grid cannot badge
The report SHALL include three checks computed over the loaded graph independent of disk matching: two or more
exposure plans on one target sharing (filter, purpose, effective whole-second exposure) — across all TS-sourced
targets, not only disk-matched ones; planned-only twin targets (same normalized name, or a
pair within the load's match tolerance, among targets with no disk anchor); and duplicate exposure-template
names within a profile.

#### Scenario: Planned-only twins are visible for the first time
- **WHEN** two TS targets with the same name and coordinates exist and neither has a disk directory
- **THEN** the report carries a twin item naming both TS Ids (the grid shows two unbadged rows today)

#### Scenario: Same-key check spans planned-only targets
- **WHEN** a planned-only target carries duplicate same-key plans
- **THEN** the report flags it even though write-back's planner (scoped to Both) never saw it

## REMOVED Requirements

### Requirement: Adjudicated-shape folds are information, not action
**Reason**: The alias-fold mechanism is removed in full (agreed 2026-07-08, NOTEBOOK correction entry):
the hand-edit doctrine abolishes the "benign multi-claim" category the requirement existed to soften, and
the fold demonstrably masked the unintentional M27/Dumbell twin. Multi-claims are now always duplicates.
**Migration**: none — an ex-alias shape now surfaces as a duplicate-fold action item with a consolidation
instruction, which is the desired behavior (surface for the user's decision).
