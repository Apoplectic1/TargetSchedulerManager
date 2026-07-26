# edit-direction-marks — Delta

## ADDED Requirements

### Requirement: Reverted fields read clean on every mark surface
A field whose value has returned to its baseline (per the journal's net-no-op pruning) SHALL carry no
outbound direction anywhere marks are shown — the grid's row and header marks, the Templates… picker, the
per-field flyout marks, and the unpushed count — with no per-surface filtering (all read the pruned
journal). The field's inbound state SHALL be unaffected by the round-trip: a field that showed `←` before
the edits shows `←` again after the revert, never blank.

#### Scenario: Toggle round-trip clears the row and flyout marks
- **WHEN** the user toggles a template field off and back on with the flyout open
- **THEN** the field's flyout mark and every using plan row's `→` clear on the revert commit

#### Scenario: Revert restores the prior inbound mark
- **WHEN** a plan field carries `←` from the open pull, the user edits it (`⇄`), then reverts it to the
  pre-edit value
- **THEN** the field and its row read `←` again — the rig-side fact survives the round-trip

#### Scenario: The unpushed count excludes reverted fields
- **WHEN** two fields are edited and one is reverted to its baseline
- **THEN** the sync badge counts 1 unpushed field
