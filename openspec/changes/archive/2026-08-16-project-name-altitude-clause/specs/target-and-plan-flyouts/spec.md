# target-and-plan-flyouts — Delta

## MODIFIED Requirements

### Requirement: Committed edits mirror in their grid cells in place

A committed, verified edit with an in-grid mirror (plan `desired`, plan exposure → the Seconds cell,
enable toggles) SHALL update the affected row's cells in place — no grid reload, so scroll position,
expansion state, and any in-progress edit survive — and the owning group/panel header aggregates SHALL
recompute at once. Change notifications SHALL be raised only for cells whose value actually changed.
An applied edit to a **cell-keying field** — plan exposure, template gain/offset/bin/default-exposure/
filter/name, target rotation — re-shapes the reconciliation (merged rows split, splits merge), which no
in-place mirror can express: when the editor dialog closes after such an edit, the grid SHALL
re-reconcile without a pull, so a row never keeps asserting a pairing the edit broke (obs 4798). The
in-place mirror still applies while the editor remains open. An applied edit to the **target name**
SHALL trigger the same close-time re-reconcile: name is group identity (group header, sort order,
name-claim matching, mosaic parent grouping), so name-dependent structure and badges follow the rename
when the dialog closes. An applied change to the **project name** — whether from a base-name edit or an
altitude edit's recomposition — SHALL likewise trigger the close-time re-reconcile: the project name is
grouping identity and the mosaic parent's match key, so grouping, sort, and mosaic parentage follow the
composed name when the dialog closes, with no live mirror while it is open.

#### Scenario: Desired commit updates the row and its header without a rebuild

- **WHEN** an inline Desired edit verifies against the local db
- **THEN** the row's Desired and Hours cells show the new values in place and the owning group header re-aggregates — the grid is not reloaded

#### Scenario: Exposure edit mirrors the Seconds cell at once

- **WHEN** a flyout exposure edit verifies, including a revert to the template default
- **THEN** the Seconds cell immediately shows the new effective value (resolved from the db when the caller does not know it), without waiting for the next reload

#### Scenario: A de-pairing exposure edit re-splits when the editor closes

- **WHEN** the user edits a merged Both row's exposure from 300 to 600 s over 300 s frames and closes the editor
- **THEN** the grid re-reconciles without a pull and the cell renders as its split — the TS plan and the disk frames on separate lines

#### Scenario: A rename re-groups when the editor closes

- **WHEN** the user commits a target rename and closes the editor
- **THEN** the grid re-reconciles without a pull — the group header, sort position, and any
  name-dependent badges reflect the new name

#### Scenario: A project rename or altitude recomposition re-groups when the editor closes

- **WHEN** the user commits a project base-name edit, or an altitude edit whose recomposition changes
  the stored project name, and closes the editor
- **THEN** the grid re-reconciles without a pull — grouping headers, the project dropdown, and mosaic
  parent matching reflect the composed name

#### Scenario: Non-keying edits never trigger the close-time reload

- **WHEN** an editor session commits only desired, enable, or moon-rule changes
- **THEN** closing the dialog reloads nothing — the in-place mirrors were the whole story
