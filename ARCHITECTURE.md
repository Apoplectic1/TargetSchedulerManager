# TargetCatalogManager (TCM) — Architecture

TCM is a .NET 10 app whose sole job is to **own and maintain the catalog database** (`Catalog.db`) for the
astrophotography portfolio. It is the single writer; XFM, TargetPlanner (TP), and IntervalScheduler (IS/ISP)
are read consumers.

## Source-of-truth model

The disk image library (`E:\Photography\Astro Photography\Processing`) is **ACTUAL** — the ground truth of what
has been captured. N.I.N.A. Target Scheduler (TS) is the **PLAN**, maintained against actual. `Catalog.db`
re-organizes the plan clean and **anchors it to actual**, reconciling the two onto **one canonical `target`**:

| Facet | What it holds | Source |
|---|---|---|
| **Actual** (inventory) | per-target/filter frame counts, integration, dates, coords (`inventory_filter`) | disk scan |
| **Plan** | projects / exposure templates / exposure plans incl. **goals** (`desired_count`) | TS (cleaned) |

Each `target` carries both facets, distinguished by `source_id`: `Actual` (on disk only), `Planned` (in TS only /
not yet shot), `Both` (planned **and** shot — the two resolved onto one row). `inventory_filter` (actuals) and
`exposure_plan` (goals) both hang off the one target, so "goal vs actual" is a single join. TCM can read and
(Phase 4) write back Tom Palmer's TS database, which lets XFM retire its Target Scheduler tab into TCM.

## Components

- **`Astronomy.Catalog`** (shared library, `Library/Astronomy.sln`) — the schema + build contract: table POCOs,
  `ITableMapper<T>` mappers, embedded idempotent `schema.sql` (**no migration framework** — the catalog is fully
  derived and rebuildable), `SchemaManager` (WAL / foreign_keys / busy_timeout), `CatalogStore` (CRUD +
  `WriteCatalog`), `GuidBlob`, the `Scan/` image-library scanner, the `Build/` reconciler (`TargetResolver` +
  `CatalogBuilder` + `CatalogBuildReport`), the `Reconcile/` goal-vs-actual layer (`Reconciler` +
  `CatalogStore.GetReconciliation`), and the hardened read-only `TargetSchedulerReader`. Pure-managed
  (Microsoft.Data.Sqlite + Astronomy.XISF). **Every consumer references this**, not the TCM app — TCM is the
  writer, the library is the contract.
- **`TargetCatalogManager`** (this app):
  - **Headless build (Phase 1–2, shipped)** — `Program.cs` console host: `tcm [--catalog --library --ts
    --tolerance]` runs `CatalogBuilder.BuildAsync` and prints the reconciliation report + the goal-vs-actual
    summary. Cross-repo
    `ProjectReference` to `Astronomy.Catalog` (local disk is source of truth).
  - **Maintenance UI (Phase 3, WinUI 3)** — CRUD over `CatalogStore`, scan trigger, goal-vs-actual view; hosts
    the migrated XFM scheduler tree. Sits on the same `CatalogBuilder`.
- **Consumers** — XFM / TP / IS / ISP open `Catalog.db` read-only via `SchemaManager.OpenReadOnly`. XFM's
  actual-only world is `CatalogStore.GetShotTargets()` (source `Actual` | `Both`).

## Key facts

- **DB location:** `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db` (co-located with the data it indexes).
- **Reconciliation:** coordinate-primary — each TS target anchors to the nearest disk target within a tolerance
  (default 0.5° haversine); name only validates; disk plate-solved coords win on merge; the TS guid is retained
  on `Both` for write-back. TS duplicates fold onto one canonical, and name-mismatch / ambiguous / unanchored /
  out-of-range rows are reported in `CatalogBuildReport`, not dropped. First real run (2026-06): 70 disk × 102 TS
  → 39 Both / 62 Planned / 31 Actual, 1 name-mismatch (`CygnusLoop P3` ↔ `NGC 6995`, coords-matched despite the
  name), 1 TS duplicate (`M27` / `Dumbell`) in ~1s.
- **Schema rules:** GUID `BLOB(16)` PKs (big-endian, see `GuidBlob`), `snake_case`, NULL not sentinels, enum
  lookup tables + CHECK, every FK indexed, UNIX-seconds timestamps. **No `schema_migration` / `user_version`** —
  a schema change just means deleting the regenerable `Catalog.db`.
- **Harden rule:** never pass a raw TS integer into a CHECK/FK column — `TargetResolver` coerces unknown
  epoch/state/priority codes to a safe default and normalizes/clamps planned RA/Dec, so one bad external TS row
  can't abort the rebuild.
- **Concurrency:** TCM is the single writer; WAL is on so consumers read without blocking. (WAL is unhappy over
  network shares — relevant if a consumer runs on another PC.)
- **TS interop:** read-only today (`TargetSchedulerReader`, opened `Mode=ReadOnly` + busy-timeout, explicit
  column lists, schema-version aware). Write-back shipped in Phase 4 (`tcm writeback`, see below). All TS interop
  (read *and* write) is a **stop-gap** until IS/ISP reads `Catalog.db` directly; the live TS DB likely lives on
  the imaging PC (cross-machine), so write-back operates on a local copy.
- **Reuse:** the scan is `Astronomy.Catalog.Scan.ImageLibraryScanner` (on `Astronomy.XISF`'s header reader); the
  SQLite mapper pattern came from XFM.

## TS write-back (Phase 4 — built 2026-06-08)

`TargetSchedulerWriter` pushes disk-derived counts back into TS so its planner reflects ACTUAL. It is a
**stop-gap** until IS/ISP consumes `Catalog.db` directly, so it stays minimal and cleanly deletable. Load-bearing
invariants (full spec in `ROADMAP.md` Phase 4):

- **Disk is master, one-way.** Write-back only ever flows ACTUAL → TS, never the reverse; conflicts overwrite the
  TS value up or down.
- **Counts only, cached columns only.** Sets `exposureplan.acquired` *and* `.accepted` = disk count; touches no
  `acquiredimage` rows. TS never recomputes counts from images, so the cached columns are authoritative (its own
  Database-Manager UI hand-edits them) — that is why a column write suffices and survives.
- **`(target, filter, purpose)` is the join.** Purpose (Light vs Stars) is the `"Stars "` naming convention,
  symmetric across disk directories and TS templates — so the main/Stars two-plan split resolves without guessing
  (the disk inventory can't separate same-purpose plans by exposure, hence the purpose axis carries it).
- **Uncertain identity → manual.** ≥2 plans collapsing onto one `(target,filter,purpose)` (a same-purpose
  multi-plan or a dup-fold target), **and** any target whose match is flagged (name-mismatch / ambiguous coord),
  are held for manual resolution with full info, never auto-written — a false-positive coordinate match must not
  overwrite a real TS target.
- **Safe by construction.** Operates on a local DB copy (never the live imaging-PC db) with hard guards
  (open-connection sidecars, schema version, read-only file), dry-run by default, one transaction + read-back
  verify. No backups — both DBs are recreatable. The writer uses a private SQLite cache (so it doesn't inherit the
  build-reader's read-only shared cache); a fresh re-scan each run can't push stale numbers.

This supersedes the earlier "IS owns `scheduler.db`" plan — `Catalog.db` is the hub and IS becomes a consumer.
