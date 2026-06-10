# TargetCatalogManager (TCM) — Roadmap

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Status — pick up here (2026-06-10)

Phases 1–2 are **done and working on real data**. The headless host (`tcm`) rebuilds `Catalog.db` from the image
library (ACTUAL) + the TS snapshot (PLAN) and prints reconciliation + goal-vs-actual in ~1s:

- **77 disk × 102 TS targets → 44 Both / 25 Planned-only / 33 Actual-only**, 6 mosaics (38 panels folded); **0
  name-mismatches / 0 ambiguous** (mosaic handling fixed the old `CygnusLoop P3` ↔ `NGC 6995`), 1 TS
  alias-duplicate (`M27` / `Dumbell` — see Open decision below).
- **goal vs actual:** 69 planned targets (6 complete / 38 in-progress / 25 not-started), **12235 / 30088 frames
  done**, 17853 remaining.

All logic lives in the shared `Astronomy.Catalog` library (75 tests, 0 warnings). The TCM project itself is just
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

**Shipped 2026-06-08 (this session):** `tcm writeback --target` (surgical single-target; per-panel for mosaics) —
**verified live on BIRDWATCHER with NINA/TS running**. CLI hardened so the verb is dash-tolerant (`--writeback`
routes) and `--target` without the verb prints a hint instead of silently running a full build. All committed
(library + TCM, branch `dev`).

**▶ SHIPPED 2026-06-10 — M27/Dumbell = alias, option B** (treat aliases as one object). An **alias** = every
colliding TS name **exactly** matches a disk identity facet (directory / catalog / common / object; normalized, no
substring) — `M27` + `Dumbell` are the two halves of disk `M27 - Dumbell`; the strict rule keeps genuine variants
like `M42` + `M42 core` flagged as real duplicates. Implemented: `AliasTsTarget` in `CatalogBuildReport`,
`TargetResolver.IsAliasName`, and `WriteBackPlanner` auto-writes an alias cell when its plan count equals the alias
member count (disk count to **every** member's plan; any other multiplicity stays `MultiPlan` manual). Verified on
real data: duplicates 1→0, aliases 1, the 6 held cells became 12 writes (both members converge, one `desired`
ratchet 129→169), manual bucket M27-free, dry-run idempotent. 78 library tests (+3).

**▶ Phase 3 planned (grill-me session 2026-06-10) — TS Editor (WinUI 3).** TS stays the daily scheduler until IS
exists; TCM bridges: view + edit the **local TS working copy** with disk-ACTUAL beside every number. Full spec in
Phase 3 below. Build order: **(1) alias rule (above — ✅ shipped) → (2) M1 read-only grid (✅ built 2026-06-10) →
(3) M2 edits → (4) M3 resolution + structural.**

**▶ M1 BUILT 2026-06-10 — `TargetCatalogManager.App`** (WinUI 3, WindowsAppSDK 2.2.0, unpackaged, x64, exe
`tcmui`): read-only reconciliation grid — flat (target, filter, purpose) rows, plan vs DISK vs Δ from a fresh
in-memory scan+resolve (no Catalog.db), search / source filter / flagged-only / sort, match-state badges, mosaic
rollup rows. Self-verified: launches and matches the console exactly (Both 44 / TS-only 25 / Disk-only 33, alias 1,
mosaics 6/38 panels). **Pending: user's hands-on UI pass** (filters, scroll perf, badge readability). Gotcha
captured: the console csproj sits at the repo root, so it must `DefaultItemExcludes` the nested app dir.

**Logging (slice 1, built 2026-06-10, ported from TP):** `tcm.log` under `%APPDATA%\TargetCatalogManager\Logs\`
(session rotation, WARN/ERROR, `TCM_DIAG` channels) + **Ctrl+N observation window** — modeless always-on-top,
USER_OBS START/END markers, notes + VM ctx snapshot + main-window screenshot into the log stream. Use it during
the M1 pass to annotate findings in place. **Pending: interactive verification** (Ctrl+N → note → Ctrl+Enter/OK →
check log + screenshots). Slice 2 open: wire `DIAG/Load` + `DIAG/UI`; M2 rule — the writer logs every TS write. TS read+write remains a **stop-gap** until IS/ISP reads `Catalog.db` directly — but the Phase-3 **UI
shell is permanent** (retargets Catalog.db when IS arrives); only the TS data layer is disposable.

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

## Phase 3 — TCM app: TS Editor (WinUI 3)  ◀ NEXT (planned 2026-06-10)

**Purpose.** TS remains the daily scheduler until IS exists; TCM is the bridge: view + edit TS's database with
disk-ACTUAL beside every number. A pragmatic editor, **not** a TS Database Manager replacement. The TS *data
layer* is the disposable stop-gap; the **UI shell is permanent** — it retargets `Catalog.db` plans when IS
arrives. Supersedes the old "migrate XFM's scheduler tab" framing: the grid replaces that tab (XFM's is deleted
at Phase 5 cutover).

- **DB touched:** the **local TS working copy only** (same default as `writeback`); manual copy to/from
  BIRDWATCHER stays the sync; push-to-live stays a separate future feature (Phase 4's tail).
- **Structure:** new **`TargetCatalogManager.App`** (WinUI 3) beside the untouched `tcm` CLI (WinExe can't host a
  clean console). Edit layer **`TargetSchedulerEditor`** in `Astronomy.Catalog/TargetScheduler/` next to
  Reader/Writer — tests live in the library; same cleanly-deletable contract; no consumer terminology.
- **UI shape — grid-first:** home screen is a flat filterable **(target, filter, purpose)** reconciliation grid —
  plan vs DISK vs Δ; disk columns from a **fresh scan on load** (~1 s, the same self-contained path `writeback`
  uses; `Catalog.db` isn't needed for the editor screen). Tree (Profile ▸ Project ▸ Target) is secondary nav.
  Mosaics appear **per panel** (TS granularity) + a rollup row. In-grid editing for Tier 1; detail panel for
  Tiers 2–3; toolbar/context commands for Tier 4. **`acquired`/`accepted` are read-only** — Phase 4 write-back
  owns those columns.
- **Edit tiers (all in scope):** **T1** counts & toggles (`desired`, plan `enabled`, target `active`, target
  priority) · **T2** identity & pointing (`name`, RA/Dec, epoch, rotation, ROI) · **T3** project knobs (state,
  priority, description, altitude/horizon/meridian, filterSwitchFrequency, ditherEvery, smartExposureOrder,
  enableGrader, flatsHandling, ruleWeights) · **T4** structural (add/delete target & plan, template swap, move
  between projects). Templates/profiles render read-only.
- **Differences are first-class:** all three sources shown, match-status **badge column + filter bar**, per-class
  resolution commands — **Disk-only** (33) → *create TS target from disk* (dir name, plate-solved coords, plans
  seeded from existing filters); **TS-only** (25) → leave (legit not-started) / *rename-to-disk* showing nearest
  disk candidates with distances (catches `Forsaken`-0.50° near-misses) / delete; **name-mismatch / ambiguous** →
  *adopt disk name* / accept; **dup-fold** → alias rule (genuine variants only, post-option-B).
- **Milestones:** **M1 view** (✅ built 2026-06-10, see Status) — read-only grid + badges + filters, verified
  against the known 44 / 25 / 33 → **M2 edit** — Tier-1 in-grid + detail panel (T2–T3) + save → **M3 resolve** —
  resolution commands + Tier 4.
- **Hazards (carry into M2/M3):** cadence-breaking edits (T3/T4 — filterSwitchFrequency, ditherEvery, plan
  add/remove, …) must replicate TS's `filtercadence`-clear behaviour (lift the exact rules from TS's
  `TargetViewVM` / `SchedulerDatabaseContext` when building M2/M3); T2 name edits must round-trip the matcher's
  name-validation without minting new mismatch classes.

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
