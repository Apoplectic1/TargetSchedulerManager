# project-name-clause — Delta

## Purpose

The definitional project-name convention: every TS project name is `<base> - N` where the trailing
altitude clause mirrors `Project.minimumaltitude` exactly — the name is derived from base + altitude,
never parsed to obtain either. Projects only; target names never carry the clause.

## ADDED Requirements

### Requirement: The project name is derived — base plus a definitional altitude clause

A project's stored TS name SHALL be the composition `<base> - <altitude>`, where `<altitude>` renders
`minimumaltitude` in `0.#` format (integer degrees bare, tenths kept: `30`, `89.9`). The clause is
**definitional**: it mirrors the stored `minimumaltitude` at all times, including `- 0` (a legal value
meaning "image as low as the horizon allows" — TS's planner floors at `max(horizon + offset,
minimumAltitude)`, so 0 leaves the horizon governing). Editing surfaces SHALL treat base name and
altitude as separate facts and compose the stored name on commit; no surface SHALL parse a typed name to
obtain an altitude. The convention applies to **projects only** — target names never carry a clause.

#### Scenario: Composition renders integer and decimal floors

- **WHEN** a project with base `Nebulae` has `minimumaltitude` 45, and another 89.9
- **THEN** their stored names compose as `Nebulae - 45` and `Nebulae - 89.9`

#### Scenario: A zero floor composes like any other

- **WHEN** a project with base `Nebulae` has `minimumaltitude` 0
- **THEN** its stored name composes as `Nebulae - 0` — the clause is present, not omitted

#### Scenario: A base that resembles a clause round-trips

- **WHEN** a project's base name is `Veil - 3` and its `minimumaltitude` is 30
- **THEN** the stored name composes as `Veil - 3 - 30`, and decomposition strips only the final clause,
  recovering base `Veil - 3` and altitude 30

### Requirement: The clause grammar is exactly the spaced form

The altitude clause SHALL be recognized only as the spaced suffix `" - N"` — a space, a dash, a space,
then an integer or decimal number, at the end of the name. A name not ending in that exact shape carries
**no** clause: hyphen-digit designations (`Sh2-155`), bare trailing numbers (`Abell 2218`), and the
retired legacy form (`… - Above N`) SHALL NOT parse as clauses. One shared grammar SHALL serve
composition, decomposition (base extraction), clause reading, and the reconciliation name-match's clause
stripping — the matcher tolerates the clause's presence or absence on either side of a compare, but only
in this spaced form.

#### Scenario: A hyphen-digit designation is not a clause

- **WHEN** a name `Sh2-155` is decomposed or clause-stripped
- **THEN** it is treated as clause-less — base `Sh2-155`, no altitude read, nothing stripped

#### Scenario: The legacy Above form no longer parses as a clause, but base extraction heals it

- **WHEN** a name `Nebulae - Above 45` is decomposed
- **THEN** no clause value is read (the name is nonconforming) — but base extraction additionally
  strips the retired legacy suffix, yielding base `Nebulae`, so any recomposition heals the name
  (`Nebulae - 40`) rather than nesting it (`Nebulae - Above 45 - 40` is never produced)

#### Scenario: The matcher strips symmetrically only where a real clause exists

- **WHEN** a mosaic project named `Mosaic - Pleiades - 50` is name-matched against the bare capture
  directory `Mosaic - Pleiades`
- **THEN** the project side strips its clause, the directory side strips nothing, and the names match

### Requirement: Nonconformance is detected, never repaired automatically

A project whose stored name lacks the clause, or whose clause value disagrees with the stored
`minimumaltitude`, SHALL be detected as **nonconforming** after each load. Detection SHALL never rename
by itself — the remedy is a user gesture (a project edit commit, or a scoped Visible-Tonight Set press,
either of which recomposes the name). Nonconforming names can always arrive from outside (a project
created or renamed in TS's own UI on the imaging machine), so detection is the only enforcement possible.

#### Scenario: An inbound clause-less project is flagged, not renamed

- **WHEN** a pull delivers a project named `Widefield` (no clause) with `minimumaltitude` 30
- **THEN** the project is reported as nonconforming and its stored name is untouched

#### Scenario: A disagreeing clause is flagged

- **WHEN** a project is named `Nebulae - 45` while its stored `minimumaltitude` is 30
- **THEN** the project is reported as nonconforming with both values shown

#### Scenario: A conforming library reports nothing

- **WHEN** every project's name equals the composition of its base and stored altitude
- **THEN** no nonconformance is reported
