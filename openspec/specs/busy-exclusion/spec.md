# busy-exclusion Specification

## Purpose

The one-at-a-time rule for db-touching work: bulk operations (load/reload, pull, push, visible-tonight)
are mutually exclusive, row edits are refused and their surfaces disabled while a bulk operation runs, and
the exclusion is check-and-set on the UI thread — structural, not by convention. Exists because the
"IsLoading doubles as the mutual exclusion" convention was shown (2026-07-24 review) to be forgettable:
one bulk command only checked the flag, and no row-edit path consulted it at all.

## Requirements

### Requirement: Bulk operations are mutually exclusive
Bulk db-touching operations (load/reload, pull, push, visible-tonight) SHALL be mutually exclusive: while
one runs, starting another SHALL be refused without side effects. The exclusion SHALL be acquired
check-and-set as one uninterrupted step on the UI thread, and SHALL be held for the operation's whole
span — including every point where the operation yields the UI thread.

#### Scenario: Reload during a visible-tonight pass is refused
- **WHEN** a visible-tonight pass is applying flips and the user clicks Reload
- **THEN** the reload does not start and no second writer touches the local db

#### Scenario: Second Tonight click during a pass is refused
- **WHEN** a visible-tonight pass is running and Tonight is pressed again
- **THEN** no second pass starts and no flip is journaled twice

#### Scenario: Push during a load is refused
- **WHEN** a load is running and a push is requested
- **THEN** the push does not start and the journal is not replayed

### Requirement: Row edits are refused while a bulk operation runs
While a bulk operation runs, every row-level edit path (target/plan enable toggles, Desired boxes,
field-editor flyout commits, template edits) SHALL refuse the write before it reaches the local db: the
edit is not applied, not journaled, the control reverts to its prior value, and a status-line note states
why. The refusal SHALL be enforced in the view-model edit funnel, independent of any UI disabling.

#### Scenario: Checkbox toggle during a load is refused loudly
- **WHEN** a load is running and a target-enable checkbox commit reaches the view-model
- **THEN** no write occurs, the checkbox reverts, and the status line notes the edit was refused because the app is busy

#### Scenario: Open flyout confirming after busy began
- **WHEN** a field-editor flyout was open before a bulk operation started and the user confirms a value mid-operation
- **THEN** the funnel refuses the write, the editor reverts the field, and nothing is journaled

### Requirement: Edit surfaces are visibly disabled while busy
While a bulk operation runs, edit-capable surfaces SHALL be visibly disabled: the row grid's interactive
controls and the busy-sensitive toolbar actions (Tonight, Reload, Pull now, Push, Templates). Read-only and
escape-hatch surfaces SHALL stay enabled: search, view filters, expand/collapse, the ambiguity report,
and Cancel pull during a pull. Surfaces SHALL re-enable when the operation ends.

#### Scenario: Grid greys during a pass
- **WHEN** a visible-tonight pass or load is running
- **THEN** the grid's checkboxes and edit affordances render disabled, and the busy-sensitive toolbar buttons are disabled

#### Scenario: Cancel stays reachable during a pull
- **WHEN** a pull is copying from the remote
- **THEN** the Cancel-pull button remains enabled while edit surfaces are disabled

#### Scenario: Surfaces recover after completion
- **WHEN** the bulk operation completes (success or failure)
- **THEN** every disabled surface re-enables

### Requirement: A bulk operation is refused while an edit is in flight
An accepted row edit whose worker has not yet completed SHALL block a bulk operation from starting: the
bulk operation is refused with a status-line note and may be retried. At no time SHALL an edit's write
overlap a bulk operation's read, write, or local-db file swap.

#### Scenario: Reload clicked immediately after an edit
- **WHEN** the user commits a row edit and clicks Reload before the edit's worker completes
- **THEN** the reload is refused with a status note, and retrying after the edit completes succeeds
