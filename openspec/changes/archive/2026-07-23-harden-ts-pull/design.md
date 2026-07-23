# Design: harden-ts-pull

## Context

`TsSync.Pull` (App `Shared\TsSync.cs`) copies BIRDWATCHER's TS db over the local working copy with
`SqliteConnection.BackupDatabase` — an all-or-nothing call that rewrites the live local db as **one giant
journaled transaction** (~132 MB rollback journal for the 151 MB db). The 2026-07-23 incident (see
`tsm.log.prev` forensics in the conversation record): a Pull Now ran tens of seconds (latency-dominated —
the backup makes ~37k synchronous 4 KB SMB page reads, so 2 s and 40+ s are both normal), the user killed
the process at ~87% done, and the survivors were a torn local db + hot journal that the read-only reader
cannot recover (SQLite Error 8) and the baseline skip-rule then preserved indefinitely (it checks
remote-vs-baseline, never local health). Pull also logs only on *completion*, so an interrupted pull is
invisible; the UI shows a bare spinner with no progress and no cancel — killing the process was the only
affordance offered.

Constraints: single-writer discipline per db; the inbound-diff snapshot must still read the *pre-pull* local
content; unpushed edits live in the separate `.tsm-edits.jsonl` (never inside the db); no back-compat code
(rule 15) — the local copy is disposable derived state.

## Goals / Non-Goals

**Goals:**
- A process death (kill, crash, power loss) at **any** moment of a pull leaves the previous local db fully
  usable and the baseline invariant ("baseline recorded ⇔ local mirrors remote") true.
- A torn local copy, however it arose, is detected at open and healed automatically — never silently kept.
- Pulls are observable: start + duration in the log; live percentage in the UI; user-cancellable without
  process kill.

**Non-Goals:**
- Making the pull *faster* (latency of the remote read path is environmental; out of scope).
- Guarding the remote/BIRDWATCHER side (push replay already has its own refusal guards).
- A progress *bar* — the user explicitly wants a text percentage only.
- Migration/repair tooling for the current incident state (already hand-cleaned; the heal gate covers any
  recurrence).

## Decisions

### D1 — Atomic pull: back up into a temp sibling, then swap (A)

`Pull` backs up into `<local>.pull-tmp` (same directory ⇒ same NTFS volume ⇒ atomic rename), then swaps it
over the real local db, then records the baseline:

1. Delete any stale `<local>.pull-tmp*` (tmp + its `-journal`/`-wal` sidecars) — leftovers of a dead pull.
2. Inbound-diff snapshot of the **real** local db (unchanged ordering — "what the user last saw").
3. Backup remote → tmp. The tmp starts empty, so the backup journals almost nothing (no old pages to
   preserve) — the incident's 132 MB journal disappears as a side benefit.
4. `SqliteConnection.ClearAllPools()` — pooled reader/editor handles on the local path would otherwise fail
   the swap with a sharing violation.
5. Swap: `File.Move(tmp, local, overwrite: true)` (create when no local db yet — first run).
6. Record baseline, diff-and-union inbound, log completion.

Kill windows: before 5 → old db intact, old baseline valid; between 5 and 6 → new db in place, baseline
stale-mismatched → harmless extra pull next open (the existing "extra pull, never a false skip" property).
*Alternative rejected:* keep in-place backup + rely solely on the heal gate (B) — heals after the fact but
still loses the local db to any interruption; atomicity removes the failure class instead of repairing it.

### D2 — Torn-local gate at open, heal by re-pull (B)

Before any local read (and before the baseline skip decision), a gate checks the local db's health: a
`<local>-journal` or `<local>-wal` sidecar present ⇒ torn (TSM closes cleanly; nothing legitimate leaves
one). Torn ⇒ `Log.Error` naming the file and sidecar, delete local db + sidecars + baseline, then pull
fresh (reachable) or fail the load loudly (unreachable — no silent half-state).

Why self-heal rather than rule-16 abort: the fail-fast rule targets *input-contract* violations where
proceeding would mask an upstream bug. Here the upstream truth (BIRDWATCHER) is intact and the local copy is
disposable derived state — re-pull is the deterministic, correct recovery; aborting would just make the user
delete files by hand (today's behavior). The loud log entry preserves the forensic trail.

Interaction with unpushed edits: `.tsm-edits.jsonl` is untouched by the heal, so the dirty prompt/push
replay still carries them. Trade-off: locally-edited *values* vanish from the grid until push replays them
(the db they were written to was discarded); the journal and dirty badge keep them safe and visible.

### D3 — Chunked backup via SQLitePCL.raw for percentage + cancel (D)

`SqliteConnection.BackupDatabase` is all-or-nothing — no progress, no cancellation. Drop one level to
SQLitePCL.raw (already present under Microsoft.Data.Sqlite): `sqlite3_backup_init` on the two connections'
`.Handle`s, loop `sqlite3_backup_step(N)` (N ≈ 512 pages ≈ 2 MB/step), reporting
`(pagecount − remaining) / pagecount` through an `IProgress<int>` and checking a `CancellationToken`
between steps. Cancel ⇒ dispose backup + connections, delete tmp — the real local db was never touched
(D1), no baseline recorded, session proceeds on the previous local db; a cancelled **first-ever** pull has
no local db to fall back to ⇒ the load fails loudly (status + log), matching the offline-open contract.
`SQLITE_BUSY`/`LOCKED` from a step retries within the existing 2 s busy-timeout patience.

UI: status text gains the live percentage — e.g. `pulling from BIRDWATCHER … 42%` — **text only, no
ProgressBar element** (user decision). A Cancel affordance is visible only while a pull is in flight
(implementation follows DOMAIN.md's add-a-UI-element checklist; likely the Pull Now button morphing to
Cancel, decided at implementation).

### D4 — Log the pull's existence, not just its success

`PULL starting (<N> bytes from <remote>)` before step 3; completion line gains duration
(`… in 43.2 s`); cancellation logs `PULL cancelled at NN% — tmp discarded`; the heal gate logs
`LOCAL TORN — <file> has hot <sidecar>; discarding and re-pulling`. Rationale: the incident was
undiagnosable from the live log precisely because an in-flight pull writes nothing.

## Risks / Trade-offs

- [Transient double disk footprint (~150 MB tmp beside db)] → trivial on the NVMe target; tmp deleted on
  every path (success, cancel, next pull's stale sweep).
- [`ClearAllPools` closes *all* pooled SQLite connections app-wide] → acceptable: pulls run inside
  load/push flows where no reader is mid-query (`IsLoading` mutual exclusion already serializes).
- [SQLitePCL.raw drops below the Microsoft.Data.Sqlite abstraction] → contained to one private method in
  `TsSync`; the raw backup API is stable SQLite C API; unit-tested against real temp dbs.
- [Sidecar-presence check could false-positive on a *live* concurrent writer] → single-writer discipline
  says no other writer exists; TSM's own edit transactions are sub-second and close before load runs.
- [Heal deletes a db that a torn *edit* transaction left] → edits are journaled in `.tsm-edits.jsonl` and
  replay at push; nothing is lost, values reappear after push.

## Open Questions

_None — cancel-on-first-pull, percentage-not-bar, and heal-vs-abort were decided above._
