# TargetSchedulerManager (TSM) — Architecture

TSM is a .NET 10 WinUI 3 app that **manages the N.I.N.A. Target Scheduler database**: view + edit TS plans live,
with disk-ACTUAL beside every number from a fresh read-only in-memory scan each load.

> **History (2026-06-11):** this project was **TargetCatalogManager (TCM)** — a dual-head repo whose console host
> (`tcm`) built and owned `Catalog.db`. The CLI was removed (catalog-building moves to the planned
> **LibraryCatalogManager**, sibling dir `..\LibraryCatalogManager`) and the project was renamed to match its real
> role. The build/reconcile engine described below lives in `Astronomy.Catalog` and is untouched; TSM runs it
> in memory (no `Catalog.db` write). Pre-rename docs and git history say TCM / `tcm` / `tcmui`.

## Source-of-truth model

The disk image library (`E:\Photography\Astro Photography\Processing`) is **ACTUAL** — the ground truth of what
has been captured. N.I.N.A. Target Scheduler (TS) is the **PLAN**, maintained against actual. The catalog model
(`Astronomy.Catalog`) re-organizes the plan clean and **anchors it to actual**, reconciling the two onto **one
canonical `target`**:

| Facet | What it holds | Source |
|---|---|---|
| **Actual** (inventory) | per-target/filter frame counts, integration, dates, coords (`inventory_filter`) | disk scan |
| **Plan** | projects / exposure templates / exposure plans incl. **goals** (`desired_count`) | TS (cleaned) |

Each `target` carries both facets, distinguished by `source_id`: `Actual` (on disk only), `Planned` (in TS only /
not yet shot), `Both` (planned **and** shot — the two resolved onto one row). `inventory_filter` (actuals) and
`exposure_plan` (goals) both hang off the one target, so "goal vs actual" is a single join. TSM reads and writes
Tom Palmer's TS database, which lets XFM retire its Target Scheduler tab into TSM.

## Components

- **`Astronomy.Catalog`** (shared library, `Library/Astronomy.sln`) — the schema + build contract: table POCOs,
  `ITableMapper<T>` mappers, embedded idempotent `schema.sql` (**no migration framework** — the catalog is fully
  derived and rebuildable), `SchemaManager` (WAL / foreign_keys / busy_timeout), `CatalogStore` (CRUD +
  `WriteCatalog`), `GuidBlob`, the `Scan/` image-library scanner, the `Build/` reconciler (`TargetResolver` +
  `CatalogBuilder` + `CatalogBuildReport`), the `Reconcile/` goal-vs-actual layer (`Reconciler` +
  `CatalogStore.GetReconciliation` + `ReconciliationProjection`), and the hardened read-only
  `TargetSchedulerReader`. Pure-managed (Microsoft.Data.Sqlite + Astronomy.XISF). **Every consumer references
  this**, not the TSM app — the library is the contract.
- **`TargetSchedulerManager`** (this app) — `TargetSchedulerManager.App` (assembly `tsmui`), a grid-first
  reconciliation editor over the TS db (LIVE BIRDWATCHER when reachable, else the local working copy): plan vs
  disk-ACTUAL per (target, filter, purpose, exposure seconds), tiered editing (counts/toggles → identity →
  project knobs → structural). Each load runs scan → resolve → project **in memory** (`ReconciliationLoader`);
  no `Catalog.db` is needed or written. Its TS data layer is a deletable stop-gap; the **UI shell is permanent**
  and retargets `Catalog.db` when IS arrives.
- **`Catalog.db` and its consumers** — the persistent catalog is the planned **LCM**'s output (was the retired
  CLI's job). XFM / TP / IS / ISP will open it read-only via `SchemaManager.OpenReadOnly`. XFM's actual-only
  world is `CatalogStore.GetShotTargets()` (source `Actual` | `Both`).

## Key facts

- **Catalog DB location (when built):** `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db`
  (co-located with the data it indexes). Currently unbuilt — nothing consumes it yet; LCM will own it.
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
  via `GetChildTargets`); reporting rolls panel reconciliations up under the parent's name
  (`Reconciler.Merge`). Real run: 6 mosaics → 28 matched / 10 planned-only / 7 disk-only panels.
- **Schema rules:** GUID `BLOB(16)` PKs (big-endian, see `GuidBlob`), `snake_case`, NULL not sentinels, enum
  lookup tables + CHECK, every FK indexed, UNIX-seconds timestamps. **No `schema_migration` / `user_version`** —
  a schema change just means deleting the regenerable `Catalog.db`.
- **Harden rule:** never pass a raw TS integer into a CHECK/FK column — `TargetResolver` coerces unknown
  epoch/state/priority codes to a safe default and normalizes/clamps planned RA/Dec, so one bad external TS row
  can't abort the rebuild.
- **Concurrency:** one writer per db — TSM is the only writer of the TS db on this side (TS itself writes during
  imaging; the open-sidecar guard covers the overlap), and the future LCM is `Catalog.db`'s single writer with
  WAL on so consumers read without blocking. (WAL is unhappy over network shares — relevant if a consumer runs
  on another PC.)
- **TS interop:** reads via `TargetSchedulerReader` (opened `Mode=ReadOnly` + busy-timeout, explicit column
  lists, schema-version aware); edits via the in-grid editing path (reference-driven `TsEditableSchema`);
  write-back engine shipped 2026-06-08 (see below). All TS interop (read *and* write) is a **stop-gap** until
  IS/ISP reads `Catalog.db` directly — though a *long* one (TS is the daily scheduler until IS exists), and the
  UI shell built on it is permanent; only the TS data layer is disposable. The live TS DB lives on the imaging
  PC (BIRDWATCHER, cross-machine). **TSM reads + writes that live db directly over SMB when it is
  network-reachable, falling back to the local working copy when it is not** (`TsDatabaseResolver`, ~1.5 s
  probe; a loud LIVE/LOCAL indicator says which). The risk of live SQLite-over-SMB is accepted and mitigated:
  a daily Macrium image of BIRDWATCHER (corruption → restore) and a night-image/day-edit rhythm (the rig is
  idle when editing, so the open-sidecar guard rarely even trips).
- **Grid count columns (display):** after `Desired` (TS goal) the grid shows **`TS`** = TS's recorded
  `exposureplan.acquired` (the count TS schedules on with the grader off) and **`Actual`** = on-disk frames
  (ground truth). TS `accepted` is deliberately **not** a column — with grading off TS increments acquired and
  accepted together (`ImageSaveWatcher` auto-accepts) and write-back re-sets them equal, so accepted only mirrors
  acquired; a rare in-session `accepted ≠ acquired` drift surfaces as a flagged **`acc≠acq` badge** instead
  (`BuildRows`; the data stays in the row model). *Why `TS`, not a TS %:* TS's grader-off `PercentComplete`
  divides acquired by `ExposureThrottle × Desired` (default **125 %**), so a target shot to `Desired` reads ~80 %
  and TS keeps scheduling it — TSM's `remaining = max(0, Desired − Actual)` is the honest completion. The
  `acc = acq = disk` *write* is the write-back contract (below), deferred to a future **WriteBack** action; the
  app writes only `enable`/`desired` today.
- **Reuse:** the scan is `Astronomy.Catalog.Scan.ImageLibraryScanner` (on `Astronomy.XISF`'s header reader); the
  SQLite mapper pattern came from XFM.

## TS write-back (engine built 2026-06-08; CLI verbs retired 2026-06-11)

`TargetSchedulerWriter` pushes disk-derived counts back into TS so its planner reflects ACTUAL. The engine
(`WriteBackPlanner` / `SingleTargetPlanner` / `TargetSchedulerWriter`) lives in AL and is fully tested; its
former CLI surface (`tcm writeback [--target]`) was removed with the console host and **will resurface as a TSM
app action**. It is a **stop-gap** until IS/ISP consumes `Catalog.db` directly, so it stays minimal and cleanly
deletable. Load-bearing invariants (full spec in `ROADMAP.md` Phase 4):

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
- **Guarded, not copy-isolated.** Targets the **live** BIRDWATCHER db when reachable (else the local copy) via
  `TsDatabaseResolver`; the hard guards do the protecting — refuse an open `-wal`/`-shm`/`-journal` sidecar (TS
  mid-transaction), a read-only file, or missing `exposureplan` columns (*not* an exact schema version, which the
  NINA-nightly bumps) — plus dry-run by default and one transaction + read-back verify. No app-side backups — the
  daily Macrium image is the recovery path and both DBs are recreatable. The writer uses a private SQLite cache
  (so it doesn't inherit the build-reader's read-only shared cache); a fresh re-scan each run can't push stale
  numbers.
- **Surgical single-target.** The single-target path (was `tcm writeback --target "<dir>"`) scans one directory
  only (no catalog rebuild) and writes just its cells; a **mosaic writes per panel** — each panel dir
  coordinate-anchors to its TS panel *within the same-named isMosaic project*, and each `(filter, purpose,
  binning, seconds)` cell lands on that panel's matching plan (binning guards a 2×2 cell off a 1×1 plan; seconds
  guard 600 s frames off a 900 s plan — a same-seconds plan at another binning is a `NoMatchingPlan` manual with
  context, a pure duration mismatch is an `UnplannedFrames` note). The unit is a filter-cell, so a normal target
  is one unit and a mosaic is N panel units. Unmatched units (beyond tolerance / ambiguous) are **reported,
  never forced**; reuses the same writer (acq/acc + `desired` ratchet + verify) and guards. **Deliberate
  asymmetry:** the surgical path never zeroes plans with no matching cell (a per-cell push tool must not let a
  partial scan silently zero the target's other plans); the bulk path does. Surface: `SingleTargetPlanner`
  (pure) + `ImageLibraryScanner.ScanUnitsAsync` (per-panel scan).
- **Audited.** Every write-back run (bulk or surgical, dry-run or apply) appends its full decision trail —
  writes with old→new and flags, manual groups, unplanned buckets, verify results — to the diagnostics log
  (the standing M2 rule: the writer logs every TS write; today that is `tsm.log` under
  `%APPDATA%\TargetSchedulerManager\Logs\`).

This supersedes the earlier "IS owns `scheduler.db`" plan — `Catalog.db` is the hub and IS becomes a consumer.
