# TargetCatalogManager (TCM) — Roadmap

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Status — pick up here (2026-06-04)

Phases 1–2 are **done and working on real data**. The headless host (`tcm`) rebuilds `Catalog.db` from the image
library (ACTUAL) + the TS snapshot (PLAN) and prints reconciliation + goal-vs-actual in ~1s:

- **70 disk × 102 TS targets → 39 Both / 62 Planned-only / 31 Actual-only**; 1 name-mismatch (`CygnusLoop P3` ↔
  `NGC 6995`, coords-matched despite the name) and 1 TS duplicate (`M27` / `Dumbell`) surfaced.
- **goal vs actual:** 101 planned targets (6 complete / 33 in-progress / 62 not-started), **8221 / 30088 frames
  done**, 21867 remaining.

All logic lives in the shared `Astronomy.Catalog` library (42 tests, 0 warnings). The TCM project itself is just
`Program.cs` (the headless host) so far — **no UI yet**. Run it: `tcm` (override `--catalog --library --ts
--tolerance`). DB at `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db`. Match tolerance is **0.5°**
(validated 2026-06-04: a sweep showed a clean gap, two near-misses `Forsaken` 0.50° / `Pickering↔CygnusLoop P11`
0.569° just outside — left as-is by choice).

**▶ Recommended next — Phase 4: `TargetSchedulerWriter` (write reconciled disk counts back into TS).**
Reconciliation already computes the true per-(target,filter) actuals, and TS's own `acquired_count` is badly
stale (Wizard: TS said 0 H, disk has 140). Every catalog row retains `imported_from_ts_guid`, so the writer maps
catalog → exact TS rows. (Alternative: Phase 3 WinUI first, if you'd rather see it before write-back.)

Known wrinkle to revisit: when two TS targets fold onto one disk target (the `M27`/`Dumbell` dup), their plans
**accumulate**, doubling that target's goals. Faithful to the duplicated TS state and flagged in the report;
cleaning the TS dup fixes it on rebuild. A "merge duplicate goals: sum vs max" option is possible if common.

## Phase 1 — Foundation (shared schema library) ✅ DONE

`Astronomy.Catalog` in `Library/Astronomy.sln` (pure-managed, `net10.0-windows`):
- `Catalog.db` schema via embedded idempotent `schema.sql` (GUID `BLOB(16)` PKs, snake_case, NULL-not-sentinel,
  enum lookup tables, indexed FKs). **No migration framework** — the catalog is fully derived (scan + TS) and
  rebuildable, so a schema change just deletes the file.
- `SchemaManager` (WAL / foreign_keys / busy_timeout, idempotent schema apply), `GuidBlob`, `ITableMapper<T>` +
  `SqliteReaderExtensions`, `CatalogStore` (CRUD + `WriteCatalog` + `GetShotTargets`).
- Hardened read-only `TargetSchedulerReader` (Mode=ReadOnly + busy-timeout, explicit columns, version-aware).

## Phase 2 — Scanner → inventory + reconciliation ✅ DONE

- `Astronomy.Catalog.Scan.ImageLibraryScanner` walks `E:\Photography\Astro Photography\Processing` (on
  `Astronomy.XISF`) → per-target/filter aggregates.
- **Canonical-target resolution** (`Build/`): one `target` carries disk identity + plan attributes
  (`source_id` Actual/Planned/Both); `TargetResolver` coordinate-primary matching (default 0.5°), disk coords
  win, TS guid retained for write-back, duplicates/mismatches reported in `CatalogBuildReport`;
  `CatalogBuilder.BuildAsync` is the full rebuild (scan + read TS → resolve → `WriteCatalog`).
- **Goal-vs-actual** (`Reconcile/`): `Reconciler` + `CatalogStore.GetReconciliation` join TS goals
  (`exposure_plan.desired_count`) to disk actuals (`inventory_filter`) per (target, filter); actuals are disk
  truth (TS's stale `acquired_count` ignored); `ReconcilePolicy.Combined` (default) counts Light + Stars; status
  NotStarted / InProgress / Complete / Unplanned. The host prints the rollup + most-incomplete targets.
- TS → catalog carries provenance (`imported_from_ts_guid`).
- **TCM headless host** (`Program.cs`): `tcm [--catalog --library --ts --tolerance]` runs the build + prints the
  reconciliation report and goal-vs-actual summary.
- 42 Catalog tests + 45 NINA tests pass.

## Phase 3 — TCM app (WinUI 3)  ◀ alternative next

- Maintenance UI over `CatalogStore` (CRUD, scan trigger, goal-vs-actual view) on the same `CatalogBuilder` /
  `GetReconciliation`.
- Migrate XFM's Target Scheduler tab (`MainForm/TargetScheduler.*` + `CustomTreeView`) into TCM.

## Phase 4 — Reconcile / write back to TS  ◀ recommended next

- `TargetSchedulerWriter`: write reconciled disk-derived counts (and plan edits) back into TS via the retained
  `imported_from_ts_guid` provenance. Handle TS on the imaging PC (cross-machine; WAL share caveat).

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TCM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).
