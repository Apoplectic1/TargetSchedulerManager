# schema-driven-field-editor — Delta

## ADDED Requirements

### Requirement: The project name field edits as the base name

In the project editor, the `name` field SHALL seed with the **base name** — the stored name minus its
altitude clause — and a commit of that field SHALL write the stored name **composed** from the edited
base and the currently stored `minimumaltitude`. A commit of the `minimumaltitude` field SHALL likewise
recompose the stored name from the current base and the new altitude — journaling **two** per-field
writes (`minimumaltitude`, then `name`), each through the guarded gate, so the push review shows both. A
whitespace-only base SHALL be refused at the control (revert, the rename-verb precedent). The name field
SHALL NOT interpret typed text as an altitude — the altitude field is the only way to change the floor.

#### Scenario: The name field shows the base, not the clause

- **WHEN** the project editor opens on `Nebulae - 45`
- **THEN** the name field shows `Nebulae` and the min-altitude field shows 45

#### Scenario: A base edit recomposes with the stored altitude

- **WHEN** the user edits the base from `Nebulae` to `Nebula Survey` and commits, altitude untouched
- **THEN** one name write journals: the stored name becomes `Nebula Survey - 45`

#### Scenario: An altitude edit recomposes the name

- **WHEN** the user edits min altitude from 45 to 40 and commits
- **THEN** the journal gains a `minimumaltitude = 40` write and a rename to `Nebulae - 40` — two
  push-review lines

#### Scenario: A nonconforming name heals on its next commit

- **WHEN** the editor opens on a clause-less project `Widefield` (altitude 30) and the user commits any
  edit to the name or altitude field
- **THEN** the composed write produces `Widefield - 30` — the dialog edit is a nonconformance remedy

#### Scenario: A whitespace-only base is refused at the control

- **WHEN** the user clears the base name to spaces and commits
- **THEN** the control reverts to the seeded base and nothing journals
