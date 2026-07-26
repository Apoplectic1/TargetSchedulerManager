# ts-sync-model — Delta

## MODIFIED Requirements

### Requirement: All edits write locally and journal
Every accepted edit SHALL write to the local db through the guarded, read-back-verified field editor and
append a persisted journal entry `(table, key, column, value, label, timestamp)`. BIRDWATCHER SHALL never be
written outside an explicit push. The dirty state (journal non-empty) SHALL be persisted and survive an app
restart.

The journal SHALL NOT retain net-no-op fields: each field's **baseline** is the first journaled Old since
the last push, and a verified write whose value equals that baseline (under the journal's one invariant
display-text rule; equality fails safe to retention) SHALL prune the field's entries — crash-safely —
instead of appending; a first-touch write of the current value SHALL journal nothing. A pruned field is
clean everywhere the journal is read: direction marks on every surface, the unpushed count, the push review
and replay, and the dirty-open prompt. Push retention resets baselines (the pushed value is the next
edit's baseline).

Durability boundary: a journal append SHALL be flushed to the operating system before the entry is
reported (protects against process crash). It is not required to be a synced-to-disk write.
The journal append and the db commit are separate durability events and are not atomic with each other;
the journal SHALL NOT claim stronger durability than this.

#### Scenario: An edit lands locally and journals
- **WHEN** the user commits a desired change on a plan row
- **THEN** the local db holds the new value, one journal entry exists for it, and no remote write occurred

#### Scenario: Dirty state survives restart
- **WHEN** the app is closed with unpushed edits and reopened
- **THEN** the relaunch shows the value, the journal entry, and the dirty badge

#### Scenario: A toggle round-trip leaves no edit
- **WHEN** the user toggles a template's moon avoidance off and back on (its baseline state)
- **THEN** the journal holds no entry for the field, no surface marks it, the unpushed count excludes it,
  and a push replays nothing for it

#### Scenario: Re-committing the current value journals nothing
- **WHEN** the user commits a field's existing value (the editor verifies without writing)
- **THEN** no journal entry is created and the field stays clean

#### Scenario: Baseline resets at push
- **WHEN** a field's edit is pushed and the user then changes the field and changes it back to the pushed value
- **THEN** the field reads clean again (the pushed value is the new baseline)
