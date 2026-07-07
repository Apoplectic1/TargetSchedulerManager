# Tasks: sync-model

TSM-only change (library engine reused as shipped). Order: sync core → journal → gate rework → push → 
write-back → UI → verify. Each group leaves the build green.

## 1. Sync core (`Shared/TsSync`)

- [x] 1.1 `TsSyncState` persistence beside the local db (JSON sidecar): baseline (remote mtime + size, recorded-at), dirty flag; load/save, crash-safe
- [x] 1.2 Pull via `SqliteConnection.BackupDatabase` (remote read-only → local), with remote-stat + sidecar probe under the existing reachability timeout; re-record baseline after pull
- [x] 1.3 Skip logic: baseline match AND no remote sidecar → no copy; unit tests for all skip/pull matrix cells (changed / unchanged / sidecar / unreachable / unbaselined-first-run)

## 2. Journal

- [x] 2.1 Journal model + persistence (JSON lines beside the local db): `(seq, table, key, column, value, label, at)`; append, load, clear, collapse to last-write-per-(table,key,column)
- [x] 2.2 Tests: append/collapse/persist round-trip; dirty flag tracks journal non-empty

## 3. Gate rework (retire the live-write world)

- [x] 3.1 `TsEditGate` always targets the local path; journal append on verified write; delete `EditOutcome.LiveDropped`, `NotifyLiveWriteFailed`, sticky-fall, post-write `ClearAllPools` (+ their tests)
- [x] 3.2 Rework `TsSource` → `TsSync` (paths, probe, state, pull/skip, push); update `MainViewModel` load path; migrate/retire the LIVE/LOCAL radio bindings and `RefusalText` wiring as needed
- [x] 3.3 Open-with-dirty flow in the VM: reachable + dirty → push/discard decision before any pull (UI hook in group 6)

## 4. Push (replay)

- [x] 4.1 `TsSync.PushAsync`: collapse journal → per-entry `TrySetField` against the remote path (whole-push refusal on remote sidecar; per-entry row-missing/verify failures reported and retained); on full success clear journal + dirty, re-record baseline
- [x] 4.2 Staleness check (remote mtime vs baseline) surfaced as a warning result for the dialog
- [x] 4.3 Seam tests: replay touches only journaled fields; collapse; sidecar refusal; partial-failure retention; baseline/dirty reset

## 5. Write-back integration

- [x] 5.1 Post-load step in `MainViewModel`/loader: `WriteBackPlanner.Plan` over fresh scan + local read; apply non-no-op changes locally via the gate with a write-back label; journal entries carry the write-back marker
- [x] 5.2 Tests: counts stamp + journal on drift; clean system → zero writes/entries; one-sided targets ignored (mirrors planner contract)

## 6. UI

- [x] 6.1 Toolbar: remove LIVE/LOCAL radios; add sync badge ("synced HH:mm · N unpushed", offline wording) + **Push** button (disabled when clean); optional Pull-now override routed through the dirty guard
- [x] 6.2 Push review `ContentDialog`: manual edits list + write-back summary with decreases first (target · filter · old → new); staleness warning line; confirm/cancel
- [x] 6.3 Open-with-dirty prompt dialog (push / discard-and-pull)

## 7. Verify + docs

- [x] 7.1 Build + full App.Tests (113 pass, 0 warnings); user-run pass verified clean 2026-07-06 (fresh pull, skip on relaunch, offline session, edit → badge → push verified in NINA's TS editor, write-back decreases review, dirty-prompt after kill)
- [x] 7.2 Update `ARCHITECTURE.md` (sync model replaces the live-db invariant — the "prefers the live BIRDWATCHER db" story reverses), `VERIFICATION.md` (new flows), `DOMAIN.md` (badge/Push conventions + the buttons-decisions/guards-facts principle), `ROADMAP.md`, root `CLAUDE.md` router line — same commit as the code
