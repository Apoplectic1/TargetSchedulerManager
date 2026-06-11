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
  - **TS Editor (Phase 3, WinUI 3 — planned, spec in `ROADMAP.md`)** — `TargetCatalogManager.App`, a grid-first
    reconciliation editor over the **local TS working copy**: plan vs disk-ACTUAL per (target, filter, purpose,
    exposure seconds),
    tiered editing (counts/toggles → identity → project knobs → structural), per-class difference-resolution
    commands (create-from-disk, rename-to-disk, delete). Its TS data layer (`TargetSchedulerEditor`, in the
    library beside Reader/Writer) is a deletable stop-gap; the **UI shell is permanent** and retargets
    `Catalog.db` when IS arrives.
- **Consumers** — XFM / TP / IS / ISP open `Catalog.db` read-only via `SchemaManager.OpenReadOnly`. XFM's
  actual-only world is `CatalogStore.GetShotTargets()` (source `Actual` | `Both`).

## Key facts

- **DB location:** `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db` (co-located with the data it indexes).
- **Reconciliation:** coordinate-primary, scope-equal — every disk unit (a top-level dir OR one mosaic panel)
  carries a *scope key* (the default scope for top-level units; the mosaic's normalized name for its panels;
  none for a mosaic parent, which matches by project name); each TS target derives its scope from its own
  grouping (isMosaic project → that scope, else default). ONE rule: anchor to the nearest in-tolerance unit
  of the same scope (default 0.5° haversine); name validates (a panel validates via its directory token —
  `Panel 01of16` → `P1`); **an aligned claim outranks an unaligned one** (a nearby-but-differently-named
  target releases back to planned instead of piling onto a directory a correctly-named target owns — the
  Witch Head shape). Disk plate-solved coords win on merge; the TS guid is retained on `Both` for write-back.
  Cross-scope matches are impossible by construction (`CygnusLoop P3` can never grab `NGC 6995`). Duplicates /
  aliases / mismatches / ambiguous / unanchored rows are reported in `CatalogBuildReport`, never dropped.
- **Mosaics = target hierarchy:** a panel **is a normal target** whose key is composite. A `Mosaic - <Name>`
  dir nests an extra panel level; the scanner's one walk feeds both the whole-target aggregate and per-panel
  sub-reports; the resolver emits one **parent row** (grouping node — no plans, no inventory) plus one
  **child target per panel** (`parent_target_id` set; `directory_name` = `<mosaic dir>/<panel label>`; own
  centroid, own TS provenance, own plans + inventory). Bulk write-back therefore treats panels as ordinary
  targets (the old mosaic→manual routing is gone); `GetShotTargets()` returns top-level rows only (panels
  via `GetChildTargets`); the console rolls panel reconciliations up under the parent's name
  (`Reconciler.Merge`). Real run: 6 mosaics → 28 matched / 10 planned-only / 7 disk-only panels.
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
  (read *and* write) is a **stop-gap** until IS/ISP reads `Catalog.db` directly — though a *long* one (TS is the
  daily scheduler until IS exists), and the Phase-3 UI shell built on it is permanent; only the TS data layer is
  disposable. The live TS DB lives on the imaging PC (cross-machine), so write-back and the Phase-3 editor
  operate on a local copy.
- **Reuse:** the scan is `Astronomy.Catalog.Scan.ImageLibraryScanner` (on `Astronomy.XISF`'s header reader); the
  SQLite mapper pattern came from XFM.

## TS write-back (Phase 4 — built 2026-06-08)

`TargetSchedulerWriter` pushes disk-derived counts back into TS so its planner reflects ACTUAL. It is a
**stop-gap** until IS/ISP consumes `Catalog.db` directly, so it stays minimal and cleanly deletable. Load-bearing
invariants (full spec in `ROADMAP.md` Phase 4):

- **Disk is master, one-way.** Write-back only ever flows ACTUAL → TS, never the reverse; conflicts overwrite the
  TS value up or down.
- **Counts only, cached columns only.** Sets `exposureplan.acquired` *and* `.accepted` = disk count and ratchets
  `desired` **up** to ≥ that (never lowered — a goal can't be below what was kept); touches no `acquiredimage` rows.
  TS never recomputes counts from images, so the cached columns are authoritative (its own Database-Manager UI
  hand-edits them) — that is why a column write suffices and survives.
- **`(target, filter, purpose, seconds)` is the join.** Purpose (Light vs Stars) is the `"Stars "` naming
  convention, symmetric across disk directories and TS templates. **The plan's whole-second exposure is its
  spec**: effective seconds = round(plan exposure ?? template default); each plan receives the disk count at
  exactly that bucket — **0 when none match** (a flagged decrease; 600 s frames never satisfy a 900 s plan).
  Same-purpose plans at *different* durations are different cells and auto-resolve; disk buckets no plan
  targets are surfaced as `UnplannedFrames` notes, never written and never manual — **write-back updates
  existing plan rows only** (plan creation/deletion is an M2 concern).
- **Uncertain identity → manual.** ≥2 plans collapsing onto one `(target,filter,purpose,seconds)` (a same-key
  multi-plan or a dup-fold target), **and** any target whose match is flagged (name-mismatch / ambiguous coord),
  are held for manual resolution with full info, never auto-written — a false-positive coordinate match must not
  overwrite a real TS target.
- **Safe by construction.** Operates on a local DB copy (never the live imaging-PC db) with hard guards
  (open-connection sidecars, read-only file, and `exposureplan` column presence — *not* an exact schema version,
  which the NINA-nightly bumps), dry-run by default, one transaction + read-back verify. No backups — both DBs are
  recreatable. The writer uses a private SQLite cache (so it doesn't inherit the build-reader's read-only shared
  cache); a fresh re-scan each run can't push stale numbers.
- **Surgical single-target (`--target`).** `tcm writeback --target "<dir>"` scans one directory only (no catalog
  rebuild) and writes just its cells; a **mosaic writes per panel** — each panel dir coordinate-anchors to its TS
  panel *within the same-named isMosaic project*, and each `(filter, purpose, binning, seconds)` cell lands on that
  panel's matching plan (binning guards a 2×2 cell off a 1×1 plan; seconds guard 600 s frames off a 900 s plan — a
  same-seconds plan at another binning is a `NoMatchingPlan` manual with context, a pure duration mismatch is an
  `UnplannedFrames` note). The unit is a filter-cell, so a normal target is one unit and a mosaic is N panel units.
  Unmatched units (beyond tolerance / ambiguous) are **reported, never forced**; reuses the same writer (acq/acc +
  `desired` ratchet + verify) and guards. **Deliberate asymmetry:** the surgical path never zeroes plans with no
  matching cell (a per-cell push tool must not let a partial scan silently zero the target's other plans); the bulk
  path does. Surface: `SingleTargetPlanner` (pure) + `ImageLibraryScanner.ScanUnitsAsync` (per-panel scan).
- **Audited.** Every CLI writeback run (bulk or surgical, dry-run or apply) appends its full decision trail —
  writes with old→new and flags, manual groups, unplanned buckets, verify results — to
  `%APPDATA%\TargetCatalogManager\Logs\tcm-cli.log` (separate from the WinUI app's session-rotated `tcm.log`).

This supersedes the earlier "IS owns `scheduler.db`" plan — `Catalog.db` is the hub and IS becomes a consumer.
