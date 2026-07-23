# ts-sync-model Specification

## Purpose

The pull → edit-locally → push-as-replay sync model between TSM's local TS db copy and the live BIRDWATCHER
db. Design principle throughout: buttons carry decisions, guards carry facts — correctness never depends on
the user remembering cross-session state.

## Requirements

### Requirement: Opening pulls a fresh copy unless the baseline proves it unnecessary
When BIRDWATCHER is reachable at open, TSM SHALL refresh the local db from the remote via the SQLite online
backup API (never a file copy), EXCEPT when the persisted baseline proves the local copy current: the local
db is healthy (no torn-state sidecar — see the torn-local requirement) AND the remote main file's
last-write time and size equal the values recorded at the last pull/push AND no remote
`-wal`/`-shm`/`-journal` sidecar exists. After every successful pull the baseline SHALL be re-recorded.
When BIRDWATCHER is unreachable, the session SHALL proceed on the local db (offline session).

#### Scenario: Unchanged remote skips the copy
- **WHEN** TSM opens, the local db is healthy, and the remote db's mtime+size match the baseline with no remote sidecars
- **THEN** no copy occurs and the session opens on the local db as-is

#### Scenario: Remote sidecar forces a pull
- **WHEN** TSM opens and a remote `-wal` exists, even with matching mtime+size
- **THEN** the pull runs (WAL content is invisible to the main file's timestamp)

#### Scenario: Changed remote pulls
- **WHEN** the remote db's mtime or size differ from the baseline
- **THEN** the pull runs and the baseline is updated

#### Scenario: A matching baseline cannot excuse a torn local copy
- **WHEN** TSM opens with a torn local db while the remote mtime+size still match the baseline
- **THEN** the skip rule does not apply — the torn-local requirement's heal path runs instead

### Requirement: A pull never leaves the local db torn
The pull SHALL back up into a temporary sibling file and atomically swap it over the local db only after
the backup completes; the real local db SHALL NOT be written during the copy. Process death at any point
SHALL leave either the previous local db or the fully-pulled new one — never an intermediate state — and
SHALL leave the baseline invariant true (baseline recorded ⇔ local mirrors remote). Stale temporary files
(and their SQLite sidecars) from a dead pull SHALL be removed before the next pull. The inbound diff's
"before" snapshot SHALL still be taken from the pre-swap local db.

#### Scenario: Kill mid-copy is harmless
- **WHEN** the process dies while the backup is writing the temporary file
- **THEN** the next open finds the previous local db healthy, the baseline still valid, and only a stale temp file (removed at the next pull)

#### Scenario: Kill between swap and baseline record costs one extra pull
- **WHEN** the process dies after the swap but before the baseline is recorded
- **THEN** the next open sees a baseline mismatch and pulls fresh — never a false skip

### Requirement: A torn local copy is detected at open and healed loudly
Before the local db is read, TSM SHALL treat a `-journal` or `-wal` sidecar beside the local db as torn
state (TSM closes cleanly; no healthy shutdown leaves one). On detection TSM SHALL log an error naming the
file and sidecar, discard the local db, its sidecars, and the baseline, and pull fresh. When BIRDWATCHER is
unreachable at that moment, the load SHALL fail loudly (status + log) rather than open a torn or silently
emptied session. The heal SHALL NOT touch the edit journal (`.tsm-edits.jsonl`); unpushed edits survive and
follow the normal dirty flow.

#### Scenario: Hot journal self-heals into a fresh pull
- **WHEN** TSM opens and `<local>-journal` exists beside the local db
- **THEN** an error is logged, the local db + sidecars + baseline are discarded, a fresh pull runs, and the session opens on the new copy

#### Scenario: Torn local with BIRDWATCHER offline fails loudly
- **WHEN** the torn gate fires while BIRDWATCHER is unreachable
- **THEN** no local read is attempted, the load fails with a status naming the torn file, and the log records why

#### Scenario: Unpushed edits survive a heal
- **WHEN** the heal discards a local db while `.tsm-edits.jsonl` holds unpushed entries
- **THEN** the journal is untouched, the dirty badge still shows them, and a later push replays them

### Requirement: Pulls are observable and cancellable
Every pull SHALL log a start line (size + source) before copying and a completion line including duration;
a cancelled pull SHALL log the percentage reached. While a pull is in flight the UI SHALL show live
progress as a text percentage (no progress-bar element) and SHALL offer a cancel action. Cancel SHALL
discard the temporary file, record no baseline, and leave the previous local db untouched; the session
proceeds on it. A cancelled first-ever pull (no local db exists) SHALL fail the load loudly.

#### Scenario: In-flight pull is visible in the log
- **WHEN** a pull is killed or cancelled partway
- **THEN** the log still shows the pull's start line

#### Scenario: Progress reads as a percentage
- **WHEN** a pull is copying
- **THEN** the status shows a live numeric percentage as text, and no progress-bar element appears

#### Scenario: Cancel is safe and needs no process kill
- **WHEN** the user cancels a pull at any percentage
- **THEN** the copy stops, the temp file is deleted, the previous local db and baseline are unchanged, and the session continues on the previous local db

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
