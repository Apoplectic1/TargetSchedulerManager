# CLAUDE.md

**Always-loaded router** for TargetSchedulerManager — read first; it orients you and points to every other doc. Keep it thin: deep detail lives in the docs it routes to.

## What this is

TargetSchedulerManager (TSM) is a .NET 10 **WinUI 3 app** (assembly `tsmui`) that **manages the N.I.N.A. Target
Scheduler database** — view + edit TS plans with disk-ACTUAL beside every number. It edits a **local working
copy** under the sync model (2026-07-06): pull from BIRDWATCHER at open (baseline-skipped when unchanged),
journaled local edits + automatic write-back, one reviewed **Push** replaying only edited fields back. It scans
the disk image library *read-only* (a fresh in-memory scan each load) purely to show plan-vs-actual; it does
**not** own or write `Catalog.db`.

> **History (2026-06-11):** This project was **TargetCatalogManager (TCM)** — it *also* used to be a headless
> console host (`tcm`) that built `Catalog.db`. That CLI was removed — catalog-building moves to a future
> **LibraryCatalogManager (LCM)** (sibling dir `..\LibraryCatalogManager`, ROADMAP there) — and the project was
> then **renamed to TargetSchedulerManager** (same day) to match its real role: a TS-database manager. The
> catalog-build engine is one AL call (`CatalogBuilder.BuildAsync`, disk-only via `targetSchedulerDbPath: null`); nothing was
> lost. Dated docs and git history before the rename say TCM/`tcm`/`tcmui`.

**Almost all logic lives in the sibling shared library `Astronomy.Catalog`** (a different git repo at `..\Library`).
When a change is about schema, scanning, reconciliation, or TS interop, you are almost certainly editing files
under `..\Library\Astronomy.Catalog`, not this repo. See `..\Library\CLAUDE.md` for the library's own guidance.

## Docs — where to look (this file routes)

Reference docs (current truth — update in the same commit as the code):
- **`ARCHITECTURE.md`** — how it works: design + the load-bearing invariants.
- **`ROADMAP.md`** — phased plan + current status + a Recently-shipped digest.
- **`DOMAIN.md`** — the human/strategy home: UI design language (grid look-and-feel + the "add a UI element" checklist) + domain conventions (incl. the TS authoring conventions).
- **`TS-SCHEMA.md`** — the TS database external contract: exhaustive tables/columns, hierarchy + vocabulary, Id-vs-guid identity, drift-check recipe for TS upgrades.
- **`VERIFICATION.md`** — how to build, run, test, and verify a change.

Journal (dated capture — `glob docs/*.md` + grep; not enumerated here): `docs/YYYY-MM-DD-*.md` (decision records, reviews) + `NOTEBOOK.md` (running lab notebook of small findings).

Scope-excluded (not this project's docs): `.claude/`, `openspec/`, `.superpowers/` (tooling), `bin`/`obj` (generated). `..\Library` is a separate repo with its own docs.

## Two-repo layout

| Repo | Path | Role |
|---|---|---|
| **TargetSchedulerManager** (this) | `E:\Projects\…\TargetSchedulerManager` | the WinUI 3 app: a TS-database manager (view + edit TS; disk read-only for plan-vs-actual). App-only since 2026-06-11. |
| **Astronomy.Catalog** + deps | `E:\Projects\…\Library` | the shared schema/build **contract** every consumer references |

TSM has two cross-repo `ProjectReference`s: `..\Library\Astronomy.Catalog\Astronomy.Catalog.csproj` and
`..\Library\Astronomy.Diagnostics\Astronomy.Diagnostics.csproj` (the shared logging/observation contract)
(local disk is source of truth; no NuGet/package hop). `Astronomy.Catalog` pulls in `Astronomy.XISF` (XISF
header reader for the scanner). Both are **pure-managed** (Microsoft.Data.Sqlite only), AnyCPU/x64, no native
deps — so this project graph builds with plain `dotnet build` (the `.vcxproj` MSBuild caveat does *not* apply
here; the native PCL projects are not in TSM's solution).

## Build, run, test, verify

See **`VERIFICATION.md`** — build/run commands, the test projects, and the xUnit-v3 build trap. TSM is
pure-managed (plain `dotnet build`); visual/UX correctness is verified by **running the app**, not the build.

## The source-of-truth model (why the code is shaped this way)

The disk image library is **ACTUAL** (ground truth of what was captured). N.I.N.A. Target Scheduler (TS) is the
**PLAN**. `Catalog.db` reconciles the two onto **one canonical `target`** row carrying both facets, tagged by
`source_id`: `Actual` (on disk only), `Planned` (in TS only), `Both` (resolved onto one row). Because
`inventory_filter` (actuals) and `exposure_plan` (goals/`desired_count`) both hang off the one target,
"goal vs actual" is a single join.

The catalog-build pipeline (all in `Astronomy.Catalog`; TSM runs it in memory each load, minus the Catalog.db
write): `ImageLibraryScanner` (Scan/) → `TargetResolver` + `CatalogBuilder.BuildAsync` (Build/) →
`CatalogStore.WriteCatalog`, then read-back + `Reconciler` / `CatalogStore.GetReconciliation` (Reconcile/).
TS is read via the hardened read-only `TargetSchedulerReader` (TargetScheduler/).

Load-bearing invariants (full detail in `ARCHITECTURE.md`):
- **Coordinate-primary, scope-equal matching** — each TS target anchors to the nearest disk unit *of its own
  scope* within a haversine tolerance (default **0.5°**); name validates (panels via their directory token);
  an aligned claim outranks an unaligned one; **disk plate-solved coords win** on merge; the TS guid is
  retained on `Both` as `imported_from_ts_guid` for write-back. Mismatches / ambiguous / duplicates /
  unanchored / coerced rows are **reported in `CatalogBuildReport`, not dropped**.
- **A mosaic panel is a normal target** with a composite key: one parent row (grouping node, no plans or
  inventory) + one child target per panel (`parent_target_id`); plans and inventory hang off children;
  write-back treats panels like any other target. `GetShotTargets()` is top-level only.
- **No migration framework** — the catalog is fully derived (scan + TS) and rebuildable. A schema change just
  means deleting `Catalog.db`. There is no `schema_migration` / `user_version`. Schema is an embedded idempotent
  `schema.sql`.
- **Harden rule** — never pass a raw TS integer into a CHECK/FK column; `TargetResolver` coerces unknown
  epoch/state/priority codes to a safe default and clamps planned RA/Dec, so one bad external TS row can't abort
  the rebuild.
- **Single writer + WAL** — one writer per db: TSM's in-app editor owns the **local** TS copy (BIRDWATCHER's db
  is written only inside the reviewed push replay — `TsSync`, see `ARCHITECTURE.md`'s sync-model section),
  `Catalog.db`'s builder (future LCM) there; consumers open via `SchemaManager.OpenReadOnly`. WAL is unhappy
  over network shares (relevant if a consumer runs on another PC).

## Shared-library discipline

`Astronomy.Catalog` is built as a shared multi-consumer library — today TSM is its only live consumer
(TP / IS / ISP are planned; XFM opted out 2026-07-07, TS-free). When editing the library, **do not bake
consumer-specific terminology into its public surface** — use "caller"/"consumer" framing; doc strings describe
the abstract contract, not how one app happens to use it. Consumer-specific behavior belongs in TSM or the
consumer, not the contract. The catalog's actual-only world for actuals-only consumers is
`CatalogStore.GetShotTargets()` (source `Actual` | `Both`).
