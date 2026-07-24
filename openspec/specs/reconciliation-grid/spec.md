# reconciliation-grid Specification

## Purpose

Presentation requirements for the reconciliation grid — the header + row-template rendering contract.
Seeded 2026-07-24 (`grid-column-ruler`) with the column-alignment invariant; future grid-presentation
requirements accrue here.

## Requirements

### Requirement: One column geometry across the header and every row kind
The reconciliation grid's column header and every row presentation (target group, mosaic panel, filter
row, and their nested detail lines) SHALL render on one shared column geometry — the same column count,
order, and widths — so cells align vertically across row kinds. The geometry SHALL have a single
authoritative definition; header and row templates SHALL consume it rather than restate it.

#### Scenario: Cells align across row kinds
- **WHEN** a target group row, a filter row, and a mosaic panel row render under the header
- **THEN** every column's cells align vertically with the header's captions

#### Scenario: A width change propagates everywhere
- **WHEN** one column's width is changed in the authoritative definition
- **THEN** the header and all row kinds render the new width with no other edit

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
