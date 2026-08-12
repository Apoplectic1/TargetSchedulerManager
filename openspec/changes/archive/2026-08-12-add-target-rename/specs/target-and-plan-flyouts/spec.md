# target-and-plan-flyouts — delta (add-target-rename)

## ADDED Requirements

### Requirement: The target editor offers a guarded rename
The target editor (target group rows and panel target rows) SHALL render the target's name as an
editable text field guarded by the arm-to-edit gesture: the field starts disabled every time the form
opens and accepts changes only while armed. A committed rename SHALL flow like any intent edit —
guarded gate, journal entry, reviewed push-as-replay to TS — labeled with the target's identity. A
blank or whitespace-only name SHALL NOT reach the gate: the control reverts, matching the
out-of-bounds numeric behavior. RA/Dec/epoch SHALL remain excluded from the editable surface — the
rename re-admits `name` only.

#### Scenario: Rename a panel target
- **WHEN** the user opens the editor for target "Cygnus Loop P9", arms the Name field, commits
  "CygnusLoop P9", and later pushes
- **THEN** the write applies to the local copy and journals under the target's label, the push
  replays it to TS, and the push review lists the rename

#### Scenario: Unarmed name field accepts no input
- **WHEN** the editor opens for a TS-backed target and the user clicks into Name without arming
- **THEN** the field is disabled and no write can originate from it

#### Scenario: Blank rename is refused locally
- **WHEN** the user arms Name, clears it to whitespace, and confirms
- **THEN** no write reaches the gate and the control reverts to the current name

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
when the dialog closes.

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

#### Scenario: Non-keying edits never trigger the close-time reload
- **WHEN** an editor session commits only desired, enable, or moon-rule changes
- **THEN** closing the dialog reloads nothing — the in-place mirrors were the whole story
