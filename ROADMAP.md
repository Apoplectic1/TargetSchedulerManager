# TargetCatalogManager (TCM) — Roadmap

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Status — pick up here (2026-06-08)

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

**▶ Next — Phase 4: `TargetSchedulerWriter` (design resolved 2026-06-08, not yet built).** The full write-back
design is settled — see Phase 4 below. It writes disk-derived counts back into TS so its planner stops over/under-
scheduling (TS's own `acquired` is badly stale: Wizard said 0 H, disk has 140). TS read+write is a **stop-gap**
until IS/ISP reads `Catalog.db` directly, so the writer stays minimal and cleanly deletable at Phase 5.
(Alternative: Phase 3 WinUI first, if you'd rather see it before write-back.)

Dup-fold goals (`M27`/`Dumbell`): two TS targets folding onto one disk target still **accumulate** plans (doubling
goals) — faithful to TS, flagged in the report, fixed by cleaning the TS dup. Phase-4 write-back sends such
duplicates to its **manual bucket** (reports, never writes) per disk-is-master.

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

## Phase 4 — Write back to TS  ◀ designed 2026-06-08, not yet built

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS/ISP reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Design resolved via grill-me (2026-06-08):

- **Scope:** counts only — no `desired` edits, no two-way sync.
- **What's written:** cached columns only — `exposureplan.acquired` *and* `.accepted`, both set = disk count
  (disk = post-cull "kept" = accepted-equivalent), so TS halts scheduling under any grading mode. **No**
  synthesized `acquiredimage` rows — TS never recomputes counts from them; its own Database-Manager UI hand-edits
  these columns (confirmed `SchedulerDatabaseContext.cs` / `TargetViewVM.cs`).
- **Conflict:** **disk wins** — overwrite up or down (ACTUAL is master). No clamp to `desired` (over-shoot >100% OK).
- **Mapping key — `(target, filter, purpose)`:** purpose ∈ {Light, Stars} from the `"Stars "` prefix, identical on
  disk dir names *and* TS template names (`B300`→Light, `Stars B`→Stars). Light plan ← `LightCount`, Stars plan ←
  `StarsCount`. **Never** the `Combined` sum. Resolves 126/127 multi-plan RGB pairs in the snapshot.
- **Manual bucket (report, never write):** genuine duplicates — ≥2 plans on one `(target,filter,purpose)` (1 case:
  `tid 52` H) and dup-fold targets (`M27`/`Dumbell`). One detector: group catalog plans by `(target,filter,purpose)`;
  >1 ⇒ manual.
- **Safety:** local-copy only; refuse on `-wal`/`-shm`/`-journal` sidecar or `user_version ≠ 24`; dry-run default,
  `--apply` to commit; timestamped backup; transaction + read-back verify.
- **Source:** fresh re-scan each run (`tcm writeback`, ~1 s, self-contained — can't push stale numbers).
- **Surface:** library = pure mechanism (compute diff plan + apply transactionally); TCM `Program.cs` = dry-run
  print + `--apply` gate + manual report. Add Writer tests against a fixture TS db.
- **Write key:** catalog `exposure_plan.imported_from_ts_guid` already holds the TS `exposureplan.Id` (integer PK)
  → direct `UPDATE exposureplan SET acquired=?, accepted=? WHERE Id=?`.

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TCM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).
