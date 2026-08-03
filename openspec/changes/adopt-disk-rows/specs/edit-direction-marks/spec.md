## ADDED Requirements

### Requirement: Inserted rows mark outbound from their journal entries
An unpushed insert journal entry SHALL mark its row `→` through the same (table, key) matching as field
entries — no separate mechanism — and SHALL roll up into header marks like any plan or target entry. The
tooltip line for an insert SHALL read as a creation (the entity's identity, "new"), not as an old → new
field pair.

#### Scenario: Adopted plan marks its row
- **WHEN** an adoption inserts a plan locally (journaled, unpushed)
- **THEN** the cell's row and its target header mark `→`, with a tooltip line identifying the new plan

#### Scenario: Insert marks survive restart
- **WHEN** the app restarts with unpushed insert entries
- **THEN** the same rows mark `→` again from the persisted journal

### Requirement: Pushed inserts do not echo as inbound new-rows
The closing pull after a push that replayed inserts SHALL NOT record inbound "new row" entries for those
rows, even though their local integer id changed to the remote-minted id. The differ (or a push-time mask)
SHALL correlate the pre-pull local row with the post-pull remote row by guid and treat them as the same
row. A row genuinely added on the remote by another writer SHALL still record its inbound entry.

#### Scenario: No phantom inbound after pushing an adoption
- **WHEN** the user adopts a cell, pushes, and the closing pull lands
- **THEN** the adopted rows read blank (no `←`, no `→`) — never a spurious "new row" inbound entry

#### Scenario: A genuinely remote-added row still reports
- **WHEN** the rig added a different plan remotely and the same closing pull lands
- **THEN** that plan records its inbound new-row entry and marks `←` as today
