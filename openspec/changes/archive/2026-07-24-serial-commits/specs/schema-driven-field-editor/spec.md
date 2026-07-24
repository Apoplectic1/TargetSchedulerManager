# schema-driven-field-editor — delta

## ADDED Requirements

### Requirement: Commits from one editing surface serialize in confirmation order
Commits issued from one editing surface (a field-editor form, the grid's inline Desired cell) SHALL
apply one at a time, strictly in confirmation order: a commit SHALL NOT start until every earlier commit
from that surface has completed. A later confirmation SHALL NOT cause an earlier verified write to
report failure or revert, and after the last commit completes the control's displayed value, the local
db, and the journal's collapsed last-write for the field SHALL agree.

#### Scenario: Rapid re-confirmation of one field lands last-value-wins, no spurious revert
- **WHEN** the user confirms a value and immediately confirms a second value in the same field while the first commit is still writing
- **THEN** both commits apply in order, neither reports a false failure, and the control, db, and journal all hold the second value

#### Scenario: A slow commit does not let a later one overtake it
- **WHEN** a commit is slow (e.g. the local db is briefly busy) and another field in the same form is confirmed meanwhile
- **THEN** the second commit starts only after the first completes, and each field's last-known state updates in confirmation order
