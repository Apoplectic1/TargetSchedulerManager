# reconciliation-grid — delta

## ADDED Requirements

### Requirement: Absent values render as the em dash; real zeros render as zeros
A cell whose value is absent (no plan side, no disk side, unknown) SHALL render as the em dash ("—") —
never blank and never a fabricated 0 — while a measured zero (e.g. zero frames on disk for a TS-only
row) SHALL render as 0: the dash means "nothing to say", the zero is a fact. Hours SHALL render with one
decimal, except small non-zero magnitudes (< 0.05 h) with two — so a short-frame total reads as small
rather than missing. These conventions SHALL have a single authoritative definition consumed by every
renderer.

#### Scenario: No plan side shows dashes, not zeros
- **WHEN** a disk-only row renders its Desired/TS cells
- **THEN** they show "—" (no goal exists), while its Actual cell shows the real frame count

#### Scenario: Small hours read as small, not missing
- **WHEN** a row's hours total is 0.03
- **THEN** it renders "0.03", not "0.0"
