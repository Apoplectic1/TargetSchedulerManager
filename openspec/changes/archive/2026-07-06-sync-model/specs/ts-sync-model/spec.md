# Spec: ts-sync-model

The pull → edit-locally → push-as-replay sync model between TSM's local TS db copy and the live BIRDWATCHER
db. Design principle throughout: buttons carry decisions, guards carry facts — correctness never depends on
the user remembering cross-session state.

## ADDED Requirements

### Requirement: Opening pulls a fresh copy unless the baseline proves it unnecessary
When BIRDWATCHER is reachable at open, TSM SHALL refresh the local db from the remote via the SQLite online
backup API (never a file copy), EXCEPT when the persisted baseline matches: the remote main file's
last-write time and size equal the values recorded at the last pull/push AND no remote
`-wal`/`-shm`/`-journal` sidecar exists. After every successful pull the baseline SHALL be re-recorded.
When BIRDWATCHER is unreachable, the session SHALL proceed on the local db (offline session).

#### Scenario: Unchanged remote skips the copy
- **WHEN** TSM opens and the remote db's mtime+size match the baseline with no remote sidecars
- **THEN** no copy occurs and the session opens on the local db as-is

#### Scenario: Remote sidecar forces a pull
- **WHEN** TSM opens and a remote `-wal` exists, even with matching mtime+size
- **THEN** the pull runs (WAL content is invisible to the main file's timestamp)

#### Scenario: Changed remote pulls
- **WHEN** the remote db's mtime or size differ from the baseline
- **THEN** the pull runs and the baseline is updated

### Requirement: All edits write locally and journal
Every TSM edit (grid, flyout, write-back) SHALL write to the local db only, and every verified write SHALL
append a persisted journal entry `(table, key, column, value, label, timestamp)`. BIRDWATCHER SHALL never be
written outside an explicit push. The dirty state (journal non-empty) SHALL be persisted and survive an app
crash/restart.

#### Scenario: An edit lands locally and journals
- **WHEN** the user commits a flyout edit
- **THEN** the local db holds the new value, one journal entry exists for it, and no remote write occurred

### Requirement: Push replays the journal through the guarded per-field path
The push action SHALL apply the journal — collapsed to the last write per (table, key, column) — to the
remote db via the guarded, read-back-verified field editor, touching only journaled columns. A remote open
sidecar SHALL refuse the entire push. Entries whose row is missing or whose write fails SHALL be reported
loudly and retained in the journal; successful entries clear. On a fully successful push the journal and
dirty flag SHALL clear and the baseline SHALL be re-recorded from the remote.

#### Scenario: Replay touches only edited fields
- **WHEN** a push runs after BIRDWATCHER accrued unrelated changes (new acquiredimage rows, count increments on un-edited plans)
- **THEN** only the journaled (table, key, column) values are written; all unrelated remote data is untouched

#### Scenario: Collapse before replay
- **WHEN** the journal holds desired=20 then desired=25 for the same plan
- **THEN** the push writes 25 once

#### Scenario: Push refused while NINA holds the db
- **WHEN** a remote sidecar exists at push time
- **THEN** no remote write occurs and the user is told the db is busy

#### Scenario: Partial failure stays loud and recoverable
- **WHEN** one journal entry's row no longer exists remotely
- **THEN** the push summary names it, the entry remains journaled, and all other entries applied and cleared

### Requirement: Unpushed state is guarded and always visible
TSM SHALL show a persistent sync badge (last-synced time + unpushed count). Opening with a dirty journal and
BIRDWATCHER reachable SHALL prompt — before any pull — to push or discard; Discard SHALL clear the journal
and pull fresh. The push review dialog SHALL list the collapsed journal (write-back changes summarized with
decreases first) and SHALL warn (not block) when the remote changed since the baseline.

#### Scenario: Crash-safe dirty prompt
- **WHEN** TSM crashed with unpushed edits and is reopened with BIRDWATCHER reachable
- **THEN** the push/discard prompt appears before any pull can overwrite the local edits

#### Scenario: Staleness warning at push
- **WHEN** the user pushes and the remote mtime differs from the baseline
- **THEN** the review dialog warns that BIRDWATCHER changed since the pull, and proceeds only on confirm

## REMOVED Requirements

_None removed from existing specs — but this change retires TSM-internal behavior no spec captured: the LIVE
direct-write path (LIVE/LOCAL radios, mid-write sticky-fall `EditOutcome.LiveDropped`, post-write
`ClearAllPools`). Recorded here for the archive trail._
