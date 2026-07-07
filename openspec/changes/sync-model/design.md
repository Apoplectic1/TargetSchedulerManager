# Design: sync-model

## Context

Today `TsSource` resolves LIVE (direct SMB writes to BIRDWATCHER's `schedulerdb.sqlite`) vs LOCAL at load, and
`TsEditGate` writes per field to whichever is current, handling mid-write BIRDWATCHER drops (sticky-fall +
reload) and SMB pooled-reader staleness (`ClearAllPools`). Write-back exists in the library
(`WriteBackPlanner.Plan` — pure, exposure-aware, flags decreases; `TargetSchedulerWriter` — applies + verifies)
but has no app trigger since the CLI was removed. The user's primary workflow is `desired` edits + keeping TS
counts synced to disk truth; they relaunch TSM constantly while testing.

Decided in conversation (2026-07-06): pull-local-edit-push model; push must be a **journal replay**, not a
file copy (a file push is a time machine — it reverts everything BIRDWATCHER accrued since the pull: NINA's
nightly `acquired`/`filtercadence`/`acquiredimage` writes, XFM's graded counts); pull skipped on an unchanged
baseline; write-back runs automatically against local; **buttons carry decisions, guards carry facts** — no
correctness may depend on the user remembering cross-session state.

## Goals / Non-Goals

**Goals:**
- One editing world: every write lands in the local db instantly; BIRDWATCHER is touched only at pull (read)
  and push (replay).
- Push can never destroy data it didn't edit; forgetting state is impossible (persisted flags + visible badge).
- Unchanged relaunches are fast: no copy, no journal noise.
- Write-back = automatic local stamping + push-time review, one mechanism with field edits.

**Non-Goals:**
- No merge/conflict resolution beyond field-granularity replay + staleness warning (same-field collisions stay
  covered by edits-by-day; the sole writer of counts at night is TS itself).
- No library API changes; no change to the reconciliation/scan pipeline; no multi-machine coordination.
- The parked cadence change's semantics are untouched (it composes: local transactional edit now, replay later).

## Decisions

### D1 — Pull via the SQLite online backup API, gated by a baseline
Pull = `SqliteConnection.BackupDatabase` from the remote db (read-only connection) onto the local file — safe
while NINA holds it (reads through the WAL), unlike `File.Copy` (torn copies). Skip when the persisted
baseline matches: remote main-file `LastWriteTimeUtc` + `Length` equal the recorded values **and** no remote
`-wal`/`-shm`/`-journal` exists. *Why the sidecar clause:* under WAL, writes live in `-wal` until checkpoint —
the main file's mtime can be unchanged while content changed; sidecar-present means "content ambiguous → pull"
(which is also when fresh counts matter most). *Why mtime-vs-mtime:* both values are BIRDWATCHER's own, so
cross-machine clock skew is irrelevant; SMB metadata caching (seconds) is negligible at app-open granularity.
Baseline is re-recorded after every successful pull **and** push. Fallback upgrade path if SMB mtime ever
proves unreliable: read SQLite's file-change counter (4 bytes at offset 24) over SMB.

### D2 — Push replays the journal; a file copy toward BIRDWATCHER is forbidden
Every verified local edit appends a journal entry `(seq, table, tsKey, column, value, label, atLocal)` to a
persisted sidecar (JSON lines next to the local db). Push: collapse to last-write-per-(table, key, column),
show the review dialog, then apply each through `TargetSchedulerEditor.TrySetField` on the remote path —
guarded (a remote open sidecar refuses the whole push: "TS db busy — NINA imaging?"), read-back verified,
audited. Entries are absolute values → idempotent, order-insensitive after collapse, safe to re-run after a
partial failure (remaining entries stay journaled). *Why not file copy:* field replay touches only edited
columns; everything else BIRDWATCHER accrued is structurally untouched — this single decision eliminates the
lost-update class, including the reconnect-after-drop case (replaying `desired=25` onto a db NINA imaged on
all night is exactly right).

### D3 — Guards carry facts; buttons carry decisions
- **Dirty flag** (persisted with the journal): set on first journal entry, cleared on successful push/discard.
  Opening with dirty + BIRDWATCHER reachable prompts **before any pull**: "3 unpushed edits from Tue — Push
  now / Discard (re-pull fresh)". Crash-safety falls out of persistence.
- **Badge** (status/toolbar, always visible): "synced 14:32 · 3 unpushed" — state is displayed, never recalled.
- **Staleness warning at push**: remote mtime ≠ baseline → the dialog says BIRDWATCHER changed since the pull
  (NINA/XFM) — a warning, not a block, because replay makes cross-field interleaving safe; same-field
  collisions are the user's declared edits-by-day discipline.
- **Pull Now** exists only as a heuristic override (routes through the dirty prompt); **Push Now** is the one
  real decision, made with the journal and write-back decreases on screen.

### D4 — Write-back runs automatically against local, and journals like any edit
After each load (post-pull or post-skip, and in offline sessions), run `WriteBackPlanner.Plan` over the fresh
scan + local TS read; apply non-`IsNoOp` changes to the local db (per-plan `acquired`/`accepted`/`desired`
writes through the same gate) and journal them with a `writeback` label. *Why auto is safe here:* nothing
reaches BIRDWATCHER until push — the review gate moves to the push dialog, which lists write-back changes
with **decreases first** (a decrease from a scan miss is the dangerous half). An unchanged disk + already
stamped counts → all no-ops → zero journal entries → relaunches stay clean and skippable.

### D5 — `TsSource` becomes `TsSync`; the live-write machinery retires
The mode machine (LIVE/LOCAL, sticky-fall, `NotifyLiveWriteFailed`, `EditOutcome.LiveDropped`,
`ClearAllPools`-after-write) is deleted, not adapted — edits can no longer fail from BIRDWATCHER dropping
because they never travel over SMB. `TsSync` owns: paths, reachability probe, baseline persistence, dirty
flag, journal, pull/skip, push replay. `TsEditGate` keeps its shape (guard → write → verify → audit) minus the
drop classification, always targeting the local path, plus the journal append on success. Radios leave the
toolbar; the sync badge + Push button replace them. (No back-compat shims, per portfolio rule.)

### D6 — Offline sessions are preserved-by-default, discardable-by-choice
The original sketch declared no-BIRDWATCHER sessions disposable; that rule existed to protect a file-copy
push. Replay makes offline edits safe to push at reconnect, so the dirty prompt's **Push** is the recommended
default and **Discard** remains one click for genuine debug sessions. (Flagged as a deliberate softening of
the user's stated rule — veto restores hard-disposable.)

## Risks / Trade-offs

- **[Forgetting to push]** → the new failure this model introduces (live-writes couldn't have it). Mitigated
  by the persisted dirty flag + open prompt + permanent badge; accepted residual: TS schedules on stale plans
  until the next TSM open surfaces it.
- **[Same-field collision at push]** (write-back counts vs TS's own nightly increments) → edits-by-day
  discipline + staleness warning; replay's read-back verify also exposes surprises per field.
- **[Journal grows stale semantics]** (e.g. a replayed edit to a row TS deleted overnight) → `TrySetField`
  reports row-not-found per entry; the push summary lists skipped entries loudly (fail-loud, rule 16), journal
  keeps the failures for inspection.
- **[Backup API duration on a large db]** (`acquiredimage` history) → seconds, once per *changed* open; the
  baseline skip removes it from test loops entirely.
- **[Baseline false-skip]** (mtime+size collision) → sidecar clause covers the WAL case; residual risk is a
  same-second same-size non-WAL write — vanishingly rare for this db; Pull Now overrides; change-counter
  upgrade path documented in D1.

## Migration Plan

Pure app rework, no data migration (rule 15): first launch under the new model treats the existing local copy
as unbaselined → forces one full pull. `cadence-safe-ts-edits` (parked) rebases trivially — its writes are
already gate-shaped. Docs: ARCHITECTURE (sync model + retired invariants — the "live db when reachable" story
reverses), VERIFICATION (new flows), DOMAIN (badge/Push conventions), CLAUDE.md router line.

## Open Questions

- None blocking. Dialog layouts and badge wording are implementation-time visual calls (user-verified).
