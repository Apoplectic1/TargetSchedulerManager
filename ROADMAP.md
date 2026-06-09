# TargetCatalogManager (TCM) — Roadmap

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Status — pick up here (2026-06-08)

Phases 1–2 are **done and working on real data**. The headless host (`tcm`) rebuilds `Catalog.db` from the image
library (ACTUAL) + the TS snapshot (PLAN) and prints reconciliation + goal-vs-actual in ~1s:

- **70 disk × 102 TS targets → 39 Both / 62 Planned-only / 31 Actual-only**; 1 name-mismatch (`CygnusLoop P3` ↔
  `NGC 6995`, coords-matched despite the name) and 1 TS duplicate (`M27` / `Dumbell`) surfaced.
- **goal vs actual:** 101 planned targets (6 complete / 33 in-progress / 62 not-started), **8221 / 30088 frames
  done**, 21867 remaining.

All logic lives in the shared `Astronomy.Catalog` library (58 tests, 0 warnings). The TCM project itself is just
`Program.cs` (the headless host) so far — **no UI yet**. Run it: `tcm` (override `--catalog --library --ts
--tolerance`). DB at `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db`. Match tolerance is **0.5°**
(validated 2026-06-04: a sweep showed a clean gap, two near-misses `Forsaken` 0.50° / `Pickering↔CygnusLoop P11`
0.569° just outside — left as-is by choice).

**▶ Phase 4 — `TargetSchedulerWriter` — DONE (built 2026-06-08).** `tcm writeback [--apply]` fresh-rebuilds the
catalog, then pushes disk-derived counts into a **local** TS copy (dry-run by default). Validated on real data:
**182 plans written / 13 held for manual / 92 ignored-missing**, the motivating case `Sh2-142 Wizard H 0 → 140` is
fixed, re-apply idempotent. **`tcm writeback --target "<dir>"`** adds a surgical single-target write — no catalog
rebuild, and for a **mosaic it writes each panel's** counts to that panel's own TS plan (`Mosaic - Cygnus Loop` →
16 panels, 96 cells matched / 80 writes, apply-verify OK, idempotent). Details in Phase 4 below.

**▶ Recommended next — Phase 3 (WinUI maintenance UI) or Phase 5 (consumer cutover).** TS read+write is a
**stop-gap** until IS/ISP reads `Catalog.db` directly.

Write-back's **manual bucket** (never auto-written — presented with full info to resolve): **dup-folds**
(`M27`/`Dumbell`: two TS targets onto one disk target, plans accumulate) and **identity conflicts** (name-mismatch
/ ambiguous coord match, e.g. `CygnusLoop P3` ↔ `NGC 6995` — auto-writing a false-positive match would zero a real
TS target's counts).

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

## Phase 4 — Write back to TS  ✅ DONE (built 2026-06-08)

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS/ISP reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Built 2026-06-08 (grill-me design + real-data validation, 58 library
tests). Verb: `tcm writeback [--apply]` (dry-run default).

- **Scope:** counts only — no `desired` edits, no two-way sync.
- **What's written:** cached columns only — `exposureplan.acquired` *and* `.accepted`, both set = disk count
  (disk = post-cull "kept" = accepted-equivalent), so TS halts scheduling under any grading mode. **No**
  synthesized `acquiredimage` rows — TS never recomputes counts from them; its own Database-Manager UI hand-edits
  these columns (confirmed `SchedulerDatabaseContext.cs` / `TargetViewVM.cs`).
- **Conflict:** **disk wins** — `acquired`/`accepted` overwrite up or down (ACTUAL is master); `desired` is ratcheted
  **up** to the disk count (never lowered) so a goal is never below what was kept (over-shot targets read exactly 100%).
- **Mapping key — `(target, filter, purpose)`:** purpose ∈ {Light, Stars} from the `"Stars "` prefix, identical on
  disk dir names *and* TS template names (`B300`→Light, `Stars B`→Stars). Light plan ← `LightCount`, Stars plan ←
  `StarsCount`. **Never** the `Combined` sum. Resolves 126/127 multi-plan RGB pairs in the snapshot.
- **Manual bucket (presented with full info, never auto-written):** ≥2 plans on one `(target,filter,purpose)`
  (same-purpose multi-plan, or a dup-fold of two TS targets onto one disk target — `M27`/`Dumbell`), **and**
  identity conflicts — the whole flagged target is held when its match is a name-mismatch or ambiguous coord match
  (e.g. `CygnusLoop P3` ↔ `NGC 6995`), so a false-positive match can't zero a real TS target's counts.
- **Safety:** local-copy only; **no backups** (both DBs are recreatable); refuse on `-wal`/`-shm`/`-journal`
  sidecar, a read-only db file, or `exposureplan` missing its `acquired`/`accepted`/`Id` columns — validated by
  **column presence, not exact `user_version`** (TS bumps that every NINA-nightly migration; it's 25 now). Dry-run
  default, `--apply` to commit; one transaction + read-back verify. Writer uses a **private** SQLite cache so it
  doesn't inherit the build-reader's read-only shared cache (`SQLITE_READONLY` otherwise).
- **Source:** fresh re-scan each run (`tcm writeback`, ~1 s, self-contained — can't push stale numbers).
- **Surface:** `FilterPurposeClassifier` (shared `"Stars "` rule) + `WriteBackPlanner` (pure) + `WriteBackPlan`
  records + `TargetSchedulerWriter` (thin I/O); TCM `Program.cs` = dry-run print + `--apply` gate + manual report.
  Tests: hermetic planner cases + classifier + integration (snapshot copy → apply → verify).
- **Write key:** catalog `exposure_plan.imported_from_ts_guid` already holds the TS `exposureplan.Id` (integer PK)
  → direct `UPDATE exposureplan SET acquired=?, accepted=? WHERE Id=?`.
- **Surgical single-target — `tcm writeback --target "<dir>"`:** scans **one** directory only (no catalog rebuild)
  and writes just its cells. The unit is a **filter-cell** = `(filter, purpose, binning)`: a normal target is one
  unit, a **mosaic is N panel units**. Each unit coordinate-anchors to its TS target (a mosaic panel only within the
  same-named `isMosaic` project — name-matched first, then coord-matched), and each cell matches the TS plan by
  `(filter, purpose, binning)` so a 2×2 cell can't write a 1×1 plan. Unmatched units (beyond tolerance / ambiguous →
  `ReconcileNote`) and cells (no / multiple plans → `ManualGroup`, new `ManualReason.NoMatchingPlan`) are reported,
  never forced. Reuses the bulk writer (acq/acc + `desired` ratchet + read-back verify) + `Program.PrintWriteBack`;
  new `SingleTargetPlanner` (pure) + `ImageLibraryScanner.ScanUnitsAsync`. Tests: per-panel match, binning
  disambiguation, no-bin-match→manual, unit-beyond-tolerance→note, normal-doesn't-grab-a-panel. (75 library tests.)
- **Out of scope (later phase):** automated network push of the local copy back to the imaging PC (BIRDWATCHER) —
  for now copied back by hand; and creating missing targets (TS-only kept, disk-only deferred), revisited in the UI.

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TCM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).
