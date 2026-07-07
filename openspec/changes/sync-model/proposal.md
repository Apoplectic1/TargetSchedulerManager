# Proposal: sync-model

## Why

TSM currently edits the live BIRDWATCHER db directly over SMB when reachable (per-field writes with
`ClearAllPools` stale-page workarounds, mid-write drop handling, sticky-fall to LOCAL), else a local copy —
two divergent editing worlds. Meanwhile write-back (stamping TS `acquired`/`accepted` from disk-ACTUAL — the
user's stated primary workflow) has an engine in the library but no home in the app. This change replaces both
with one **pull → edit locally → push-as-replay** sync model: fast local edits, one deliberate reviewed sync
moment, and write-back running automatically against the local copy with its review folded into the push. A
timestamp baseline skips the pull when BIRDWATCHER hasn't changed, keeping rapid test relaunches fast.

## What Changes

- **Pull on open**: when BIRDWATCHER is reachable, copy its TS db over the local copy via the SQLite **online
  backup API** (torn-copy-safe while NINA holds the file) — skipped when a persisted **baseline** (remote
  mtime + size, recorded at last pull/push) matches *and* no remote `-wal`/`-shm`/`-journal` sidecar exists
  (WAL can hide changes from the main file's mtime).
- **All edits hit the local db** — the flyouts/grid write exactly as today, but always locally; every verified
  edit also appends to a persisted **session journal** `(table, key, column, value, label, at)`.
- **Push = journal replay, never file copy**: an explicit button replays the (collapsed) journal against
  BIRDWATCHER through the existing guarded, read-back-verified write path. Only edited fields are touched —
  NINA's nightly counts, `acquiredimage` history, and XFM's graded writes are structurally safe from clobber.
  A confirm dialog shows the journal summary (write-back decreases prominently) plus a staleness warning when
  BIRDWATCHER changed since the pull.
- **Auto write-back after every load**: `WriteBackPlanner.Plan` runs against the fresh scan + local db;
  non-no-op changes apply locally and journal like any edit (an unchanged relaunch journals nothing).
- **Guards over memory** (design principle: *buttons carry decisions, guards carry facts*): persisted dirty
  flag + always-visible "N unpushed" badge; open-with-dirty prompts push/discard before any pull; push refuses
  on a remote open sidecar.
- **Retires** the LIVE direct-write path: the LIVE/LOCAL radios, mid-write `LiveDropped` sticky-fall, and the
  post-write `ClearAllPools` SMB workaround all go away (**BREAKING** for TSM's internal seams only; the
  library editor/writer are unchanged).
- Offline sessions (BIRDWATCHER absent at open) keep journaling; because push is a replay, their edits are
  *pushable* at next reconnect rather than forcibly lost — the open-with-dirty prompt offers push or discard
  (softens the original "local-only edits are disposable" rule; a deliberate Discard preserves the
  debug-session use).

## Capabilities

### New Capabilities

- `ts-sync-model`: pull-on-open with baseline skip, local-only edits with a persisted journal, push-as-replay
  with review + staleness/sidecar guards, dirty-flag/badge state surfacing.
- `write-back`: automatic disk→TS count stamping against the local db on load, journaled like field edits,
  reviewed (decreases first) at push.

### Modified Capabilities

_None — `schema-driven-field-editor` / `target-and-plan-flyouts` requirements still hold verbatim (edits
commit through the same guarded gate; only the gate's target db changes underneath)._

## Impact

- **TSM app only** (no library API changes expected; the pull uses `SqliteConnection.BackupDatabase`, the push
  and write-back reuse `TargetSchedulerEditor`/`TargetSchedulerWriter`/`WriteBackPlanner` as shipped).
- **Reworked seams**: `TsSource` (LIVE/LOCAL mode machine) becomes a sync-state holder (baseline, dirty,
  journal); `TsEditGate` loses the live-drop classification; `MainViewModel`/toolbar swap radios for a sync
  badge + Push button. `EditOutcome.LiveDropped` and its tests retire.
- **UX**: toolbar shows "synced from BIRDWATCHER 14:32 · 3 unpushed" instead of LIVE/LOCAL; Push button is the
  review gate; Reload keeps meaning "rescan disk + re-read local" (a modifier/secondary action forces a pull).
- **Interplay with the parked `cadence-safe-ts-edits`**: unaffected in design — its transactional edits write
  locally and replay at push through the same whitelisted columns (the clear re-derives on replay).
- **Tests**: sync-state machine, journal collapse, pull-skip logic, and push replay are all seam-testable
  without SMB; App.Tests grows accordingly. Verification against the real BIRDWATCHER is the user's pass.
- **Not in scope**: any TS-side merge logic (same-field collisions remain covered by the edits-by-day
  discipline + the staleness warning), multi-machine TSM, `Catalog.db`.
