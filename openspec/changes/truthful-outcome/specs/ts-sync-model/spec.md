# ts-sync-model — delta

## MODIFIED Requirements

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

#### Scenario: Closing-pull failure is not a push failure
- **WHEN** every entry applies and verifies, the journal clears, and the closing pull then fails (e.g. the network drops mid-backup)
- **THEN** the outcome reports the push as applied with the closing pull failed and the next open pulling fresh — never "push failed" or "edits stay journaled"

#### Scenario: A throw that escapes the push really does precede the journal rewrite
- **WHEN** the push throws for any reason other than the closing pull (probe, editor/applier fault, a write fault)
- **THEN** the journal still holds every entry, and the "edits stay journaled, re-push recovers" report is accurate

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
