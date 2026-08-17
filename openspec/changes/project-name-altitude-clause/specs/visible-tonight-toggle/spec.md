# visible-tonight-toggle — Delta

## MODIFIED Requirements

### Requirement: A scoped press writes the project's constraints before enabling

With a single project selected, the Set press SHALL first journal the project's `minimumtime` from
Duration and `minimumaltitude` from Floor — each only when the box value differs from the stored value
— through the ordinary journaled edit path, then run the enable pass using the box values. Settings
flow down (the write applies to every member target at TS plan time by TS's own cascade); state rolls
up (the enable stage derives project `state` from what the sky left enabled). With All projects
selected the press SHALL write no project constraint.

The project name SHALL be composed, not tracked: after the press's constraint writes settle, when the
stored name differs from the composition of its base and the stored `minimumaltitude` (the shared
grammar; base = name minus any trailing spaced clause), the press SHALL journal a rename to the
composed form. The clause is definitional (capability `project-name-clause`), so a clause-less name
gains its clause and a stale or legacy-form (`… - Above N`) name is rewritten — the press is a
nonconformance remedy, altitude change or not. An already-composed name yields no edit, and a refused
or failed altitude write SHALL NOT rename — composition always uses the value actually stored, so the
name never asserts a constraint that did not land.

#### Scenario: Changed values are journaled then applied

- **WHEN** a project fills Duration 60 / Floor 30, the user sets Floor to 40, and presses Set
- **THEN** `minimumaltitude = 40` is journaled for that project (no `minimumtime` edit), and the enable pass runs with Duration 60 / Floor 40 over that project's targets

#### Scenario: Unchanged values write nothing

- **WHEN** a project is selected and Set is pressed with both boxes untouched and the name already composed
- **THEN** no constraint edit and no rename are journaled — only the enable pass runs

#### Scenario: All mode never writes constraints

- **WHEN** All projects is selected and Set is pressed with any Duration/Floor values
- **THEN** no project's `minimumtime` or `minimumaltitude` is written

#### Scenario: The name clause follows the altitude write

- **WHEN** a project named `Nebulae - 45` has its Floor written to 40
- **THEN** a rename to `Nebulae - 40` is journaled alongside the `minimumaltitude` edit

#### Scenario: A clause-less name gains its clause

- **WHEN** a project named `Galaxies` (stored altitude 45) is selected and Set is pressed, Floor untouched
- **THEN** a rename to `Galaxies - 45` is journaled — the press composes from the stored value

#### Scenario: A clause-less name is never renamed

- **WHEN** All projects is selected and Set is pressed while a clause-less project exists
- **THEN** no rename is journaled — only a *scoped* press composes; the All press writes no
  project constraint and no name

#### Scenario: A legacy clause migrates to the short form

- **WHEN** a project named `Nebulae - Above 45` has its Floor written to 40
- **THEN** the journaled rename reads `Nebulae - 40` — base extraction strips the retired legacy
  suffix (capability `project-name-clause`), so composition heals the name rather than nesting it

#### Scenario: A refused altitude write leaves the name alone

- **WHEN** a scoped press's `minimumaltitude` write is refused
- **THEN** no rename is journaled and the name keeps asserting the value still stored
