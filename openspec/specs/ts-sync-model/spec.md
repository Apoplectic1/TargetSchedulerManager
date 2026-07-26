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

The journal SHALL NOT retain net-no-op fields: each field's **baseline** is the first journaled Old since
the last push, and a verified write whose value equals that baseline (under the journal's one invariant
display-text rule; equality fails safe to retention) SHALL prune the field's entries — crash-safely —
instead of appending; a first-touch write of the current value SHALL journal nothing. A pruned field is
clean everywhere the journal is read: direction marks on every surface, the unpushed count, the push review
and replay, and the dirty-open prompt. Push retention resets baselines (the pushed value is the next
edit's baseline).

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

### Requirement: Push replays the journal through the guarded per-field path
The push action SHALL apply the journal — collapsed to the last write per (table, key, column) — to the
remote db via the guarded, read-back-verified field editor, touching only journaled columns. A remote open
sidecar SHALL refuse the entire push. Entries whose row is missing or whose write fails SHALL be reported
loudly and retained in the journal; successful entries clear. On a fully successful push the journal and
dirty flag SHALL clear and the baseline SHALL be re-recorded from the remote.

The report SHALL be truthful about where a failure happened: the journal SHALL be cleared exactly when
the remote writes were applied and verified, and the reported outcome SHALL never contradict the
journal's state. A failure or cancellation of the *closing pull* — which runs after the journal rewrite —
SHALL be contained and reported as a successful push whose closing pull did not land (the next open pulls
fresh); it SHALL NOT be reported as a push failure and SHALL NOT claim the edits are still journaled.

#### Scenario: Closing-pull failure is not a push failure
- **WHEN** every entry applies and verifies, the journal clears, and the closing pull then fails (e.g. the network drops mid-backup)
- **THEN** the outcome reports the push as applied with the closing pull failed and the next open pulling fresh — never "push failed" or "edits stay journaled"

#### Scenario: A throw that escapes the push really does precede the journal rewrite
- **WHEN** the push throws for any reason other than the closing pull (probe, editor/applier fault, a write fault)
- **THEN** the journal still holds every entry, and the "edits stay journaled, re-push recovers" report is accurate

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
BIRDWATCHER reachable SHALL prompt — before any pull — to push or discard; Discard SHALL run the pull
first and clear the journal only when that pull lands (the swap has physically replaced the discarded
values). A cancelled or failed discard-pull SHALL leave the journal, baseline, dirty badge, and direction
marks intact — the discarded-but-still-present values SHALL never be displayed as clean, journal-less
truth. The push review dialog SHALL list the collapsed journal (write-back changes summarized with
decreases first) and SHALL warn (not block) when the remote changed since the baseline.

#### Scenario: Crash-safe dirty prompt
- **WHEN** TSM crashed with unpushed edits and is reopened with BIRDWATCHER reachable
- **THEN** the push/discard prompt appears before any pull can overwrite the local edits

#### Scenario: Staleness warning at push
- **WHEN** the user pushes and the remote mtime differs from the baseline
- **THEN** the review dialog warns that BIRDWATCHER changed since the pull, and proceeds only on confirm

#### Scenario: Cancelled discard-pull keeps the dirty state
- **WHEN** the user chooses Discard and then cancels the pull
- **THEN** the journal is not cleared, the badge still shows the unpushed count, the grid keeps its marks, and the status says the discard did not complete

#### Scenario: Discard completes exactly when its pull lands
- **WHEN** the user chooses Discard and the pull completes
- **THEN** the local db is the fresh remote copy and the journal is cleared — the discarded values are gone from both

### Requirement: The push review and the push replay share one selection rule
The count entry the push review presents per write-back plan SHALL be selected by the same rule the
replay executes (acquired, else accepted, else the desired-only fallback) — one definition, both
consumers — so the review can never show a count change the replay does not perform. A desired-only
group SHALL display no count pair (its counts already matched disk). Likewise the review's staleness
warning SHALL negate the same baseline-match definition the pull skip rule reads; with no baseline
recorded, the warning SHALL stay silent (there is nothing to have changed *since*) even though the skip
rule treats the same state as "must pull".

#### Scenario: Desired-only group shows no phantom count change
- **WHEN** a write-back group journaled only a desired ratchet (counts already matched disk)
- **THEN** the review line for that plan shows the desired change and no count pair

#### Scenario: Staleness warning agrees with the skip rule when a baseline exists
- **WHEN** a baseline is recorded and the remote's size or mtime differs from it at push time
- **THEN** the review warns that the remote changed since the baseline — the same comparison the skip rule uses

#### Scenario: No baseline, no staleness claim
- **WHEN** a push review is built with no baseline recorded
- **THEN** no "remote changed since baseline" warning is shown, even though the pull skip rule would pull in the same state

### Requirement: Push replay legs are ordered and abort cleanly
The push SHALL replay write-back entries (through the write-back writer, per plan) before manual field
entries (through the guarded field editor, in journal-sequence order), so an explicitly journaled later
edit to the same field outranks the writer's ratchet. A structural refusal detected in the write-back
leg (schema incompatible, read-only, open sidecar) SHALL refuse the whole push before any field write.
A whole-db refusal encountered during the field leg SHALL fail every remaining field entry as
not-attempted — without issuing further writes — while entries already applied stay applied and every
failed entry is retained in the journal.

#### Scenario: Later manual desired edit outranks the write-back ratchet
- **WHEN** one push replays a write-back desired ratchet and a later manual desired edit for the same plan
- **THEN** the manual field value is what the remote holds after the push

#### Scenario: Whole-db refusal mid-field-leg stops the hammering
- **WHEN** the first field write is refused for a whole-db reason (e.g. schema incompatible)
- **THEN** no further field write is attempted, every remaining entry is reported failed as not-attempted, and all failed entries stay journaled for the next push
