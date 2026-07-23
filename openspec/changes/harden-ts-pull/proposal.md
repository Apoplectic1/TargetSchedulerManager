# Proposal: harden-ts-pull

## Why

On 2026-07-23 a real incident proved the pull is the sync model's one unguarded window: `TsSync.Pull` backs
BIRDWATCHER up **directly over the live local db** as one giant journaled transaction, so killing the process
mid-pull (the user did, after ~40 s of silent spinner — the pull was in fact ~87% done and healthy) leaves a
torn local db + hot rollback journal. The read-only reader then fails every subsequent load with SQLite
Error 8, and the baseline skip-rule — which validates remote-vs-baseline but never local health — faithfully
preserves the wreckage forever. Recovery today is manual file deletion. The pull is also latency-dominated
(~37k synchronous 4 KB SMB page reads), so a 2 s pull and a 40 s pull are both normal — and the UI gives the
user no way to tell "slow" from "hung" and no cancel, making the process-kill the only move it offers.

## What Changes

- **A — Atomic pull**: back up into a temp sibling file (`<local>.pull-tmp`), then atomically swap over the
  real local db on success. A process death at any moment leaves the previous local db fully intact and the
  baseline invariant ("baseline recorded ⇔ local mirrors remote") true. Stale tmp files are deleted at the
  next pull. Requires no pooled connection holding the local db across the swap.
- **B — Torn-local detection + self-heal at open**: before the local db is read, a hot `-journal` (or other
  torn state) next to the local copy is detected, logged loudly, and healed by discarding the local copy and
  forcing a fresh pull — never by trusting the baseline. Safe by construction: unpushed edits live in the
  separate `.tsm-edits.jsonl`, which the heal does not touch.
- **D — Pull observability**: a `PULL starting` log line before the copy (an interrupted pull is currently
  invisible in the log) and duration on completion; pull progress surfaced in the UI as a **percentage**
  (text, e.g. "pulling… 42%" — explicitly not a progress bar) via chunked backup steps; and a real **Cancel**
  that cleanly rolls back/discards the tmp so the user never needs Task Manager.

## Capabilities

### New Capabilities

_None — all three changes revise the behavior of the existing sync model._

### Modified Capabilities

- `ts-sync-model`: the pull requirement changes — pull SHALL be atomic w.r.t. the real local db (temp file +
  swap), the open sequence SHALL detect and self-heal a torn local copy instead of skipping on a matching
  baseline, and pulls SHALL be observable (start/duration logged, percentage progress, cancellable).

## Impact

- **Code**: `TargetSchedulerManager.App\Shared\TsSync.cs` (Pull rewrite, torn-local gate), `MainViewModel`
  (progress/cancel plumbing, status text), `MainWindow` (cancel affordance). Chunked progress requires
  stepping the backup via `SQLitePCL.raw` (`sqlite3_backup_init/step`) instead of the all-or-nothing
  `SqliteConnection.BackupDatabase` — SQLitePCLRaw is already present transitively under Microsoft.Data.Sqlite.
- **Connection discipline**: local-db connections must not hold pooled handles across the swap
  (`Pooling=false` where scoped, or `SqliteConnection.ClearAllPools()` before the swap).
- **Tests**: `TargetSchedulerManager.App.Tests` — atomic-swap semantics, torn-state detection/heal matrix,
  cancel mid-backup, progress callback; existing pull/differ tests keep passing (inbound-diff snapshot
  ordering is unchanged).
- **Docs**: `ARCHITECTURE.md` (sync-model section: atomic pull + heal gate), `ROADMAP.md`; supersedes the
  parked "differ-RW hot-journal hardening" note.
- **No library impact**: all changes live in the App's `Shared\` machine/network-policy layer; the
  consumer-neutral `Astronomy.Catalog` is untouched.
