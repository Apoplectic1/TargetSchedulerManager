# ts-ambiguity-report — Delta

## ADDED Requirements

### Requirement: Nonconforming project names are report action items

The report SHALL list every project detected as nonconforming under the definitional name convention
(capability `project-name-clause`) — the stored name lacks the altitude clause, or the clause value
disagrees with the stored `minimumaltitude`. Each item SHALL state the stored name, the stored altitude,
the composed name the convention expects, and the hand fix: commit any edit in the project's edit dialog,
or press Set with the project selected — either recomposes the name. The count SHALL ride the same
tripwire total as every other action item.

#### Scenario: A clause-less project reports with its expected composition

- **WHEN** a load finds project `Widefield` with `minimumaltitude` 30
- **THEN** the report carries an action item showing `Widefield`, altitude 30, expected `Widefield - 30`,
  and the dialog-or-Set remedy, and the status-line tripwire count includes it

#### Scenario: A disagreeing clause reports both values

- **WHEN** a load finds project `Nebulae - 45` with `minimumaltitude` 30
- **THEN** the report's action item shows the stored clause 45 against the stored altitude 30 and the
  expected composition `Nebulae - 30`

#### Scenario: Conforming projects add nothing

- **WHEN** every project's name equals its composition
- **THEN** the report carries no name-convention items and the tripwire count is unchanged
