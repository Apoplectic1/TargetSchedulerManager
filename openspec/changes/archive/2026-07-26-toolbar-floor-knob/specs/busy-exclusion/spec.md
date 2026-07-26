## MODIFIED Requirements

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
