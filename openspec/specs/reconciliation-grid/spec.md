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
