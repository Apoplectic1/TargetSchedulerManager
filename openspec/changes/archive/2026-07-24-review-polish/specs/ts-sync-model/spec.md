# ts-sync-model — delta

## MODIFIED Requirements

### Requirement: All edits write locally and journal
Every TSM edit (grid, flyout, write-back) SHALL write to the local db only, and every verified write SHALL
append a persisted journal entry `(table, key, column, value, label, timestamp)`. BIRDWATCHER SHALL never be
written outside an explicit push. The dirty state (journal non-empty) SHALL be persisted and survive an app
crash/restart.

Durability boundary: a journal append SHALL be flushed to the operating system before the entry is
visible in memory, so entries survive a process crash. An OS or power failure MAY lose the final
append — the local db still holds that write (the grid stays correct); only its replay at push is lost.
The journal append and the db commit are separate durability events and are not atomic with each other;
the journal SHALL NOT claim stronger durability than this.

#### Scenario: An edit lands locally and journals
- **WHEN** the user commits a flyout edit
- **THEN** the local db holds the new value, one journal entry exists for it, and no remote write occurred

#### Scenario: Process crash loses nothing
- **WHEN** the app process dies immediately after an edit committed
- **THEN** the relaunch shows the value, the journal entry, and the dirty badge
