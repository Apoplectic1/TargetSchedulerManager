# TargetCatalogManager (TCM) — Roadmap

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Phase 1 — Foundation (shared schema library) ✅ DONE

`Astronomy.Catalog` in `Library/Astronomy.sln` (pure-managed, `net10.0-windows`):
- `Catalog.db` schema via embedded idempotent `schema.sql` (GUID `BLOB(16)` PKs, snake_case, NULL-not-sentinel,
  enum lookup tables, indexed FKs). **No migration framework** — the catalog is fully derived (scan + TS) and
  rebuildable, so a schema change just deletes the file.
- `SchemaManager` (WAL / foreign_keys / busy_timeout, idempotent schema apply), `GuidBlob`, `ITableMapper<T>` +
  `SqliteReaderExtensions`, `CatalogStore` (CRUD + `WriteCatalog` + `GetShotTargets`).
- Hardened read-only `TargetSchedulerReader` (Mode=ReadOnly + busy-timeout, explicit columns, version-aware).

## Phase 2 — Scanner → inventory + TS reconciliation ✅ DONE

- `Astronomy.Catalog.Scan.ImageLibraryScanner` walks `E:\Photography\Astro Photography\Processing` (on
  `Astronomy.XISF`) → per-target/filter aggregates.
- **Canonical-target reconciliation** (`Build/`): one `target` carries disk identity + plan attributes
  (`source_id` Actual/Planned/Both); `TargetResolver` does coordinate-primary matching (default 0.5°), disk
  coords win, TS guid retained for write-back, duplicates/mismatches reported in `CatalogBuildReport`;
  `CatalogBuilder.BuildAsync` is the full rebuild (scan + read TS → resolve → `WriteCatalog`).
- TS → catalog carries provenance (`imported_from_ts_guid`); actuals live in `inventory_filter`, goals in
  `exposure_plan.desired_count`.
- **TCM headless host** (`Program.cs`): `tcm [--catalog --library --ts --tolerance]` runs the build + prints the
  reconciliation report. First real run: 70 disk × 102 TS → 39 Both / 62 Planned / 31 Actual in ~1s (1 name
  mismatch, 1 TS duplicate surfaced).
- 36 Catalog tests + 45 NINA tests pass.

## Phase 3 — TCM app (WinUI 3)

- Maintenance UI over `CatalogStore` (CRUD, scan trigger, goal-vs-actual view) on the same `CatalogBuilder`.
- Migrate XFM's Target Scheduler tab (`MainForm/TargetScheduler.*` + `CustomTreeView`) into TCM.

## Phase 4 — Reconcile / write back to TS

- Write reconciled fields and plan edits back into TS via the retained `imported_from_ts_guid` provenance
  (a `TargetSchedulerWriter`). Handle TS on the imaging PC (cross-machine; WAL share caveat).

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TCM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).
