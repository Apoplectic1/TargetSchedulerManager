# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TargetCatalogManager (TCM) is a .NET 10 app whose sole job is to **own and maintain the catalog database**
(`Catalog.db`) for the astrophotography portfolio. TCM is the **single writer**; XFM, TargetPlanner (TP), and
IntervalScheduler (IS/ISP) are read-only consumers.

This repo holds the headless console host (assembly name `tcm`; `Program.cs` routes verbs, the commands /
renderer / audit log live in `Cli\`) and the WinUI 3 app (`TargetCatalogManager.App\`, assembly name `tcmui` —
Phase 3, read-only grid so far). **Almost all logic
lives in the sibling shared library `Astronomy.Catalog`** (a different git repo at `..\Library`). When a change
is about schema, scanning, reconciliation, or TS interop, you are almost certainly editing files under
`..\Library\Astronomy.Catalog`, not this repo. See `..\Library\CLAUDE.md` for the library's own guidance.

`ARCHITECTURE.md` (design) and `ROADMAP.md` (phased plan + current status) are load-bearing — keep them current
after substantive changes, per the user's docs-as-memory convention.

## Two-repo layout

| Repo | Path | Role |
|---|---|---|
| **TargetCatalogManager** (this) | `E:\Projects\…\TargetCatalogManager` | the writer app: console host now, WinUI 3 TS-editor UI next (Phase 3, spec in `ROADMAP.md`) |
| **Astronomy.Catalog** + deps | `E:\Projects\…\Library` | the shared schema/build **contract** every consumer references |

TCM has a cross-repo `ProjectReference` straight to `..\Library\Astronomy.Catalog\Astronomy.Catalog.csproj`
(local disk is source of truth; no NuGet/package hop). `Astronomy.Catalog` pulls in `Astronomy.XISF` (XISF
header reader for the scanner). Both are **pure-managed** (Microsoft.Data.Sqlite only), AnyCPU/x64, no native
deps — so this project graph builds with plain `dotnet build` (the `.vcxproj` MSBuild caveat does *not* apply
here; the native PCL projects are not in TCM's solution).

## Build & run

```bash
# Build (slnx pulls in Astronomy.Catalog + Astronomy.XISF from ..\Library)
dotnet build TargetCatalogManager.slnx -v:m -nologo

# Run the headless catalog build (rebuilds Catalog.db from ACTUAL + PLAN, prints reconciliation)
dotnet run --project TargetCatalogManager.csproj
# or the built exe:  bin/Debug/net10.0-windows/tcm.exe

# The WinUI app (Phase 3): read-only plan-vs-disk grid; fresh scan on load, no Catalog.db needed
TargetCatalogManager.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/tcmui.exe

# Write reconciled disk counts back into the local TS copy (dry-run by default; --apply commits, restorable).
# EXPOSURE-AWARE: the write key is (target, filter, purpose, whole-second exposure) — a plan only receives
# frames at exactly its duration (0 when none match, a flagged decrease); off-duration disk buckets are
# reported as "unplanned frames", never written. Every run is audited to %APPDATA%\TargetCatalogManager\Logs\tcm-cli.log.
tcm writeback              # dry-run: per-row diff (@900s etc.) + manual bucket + unplanned frames
tcm writeback --apply      # commit to the --ts db (defaults to the TS Database working copy)

# Surgical: write back ONE disk target only (no catalog rebuild); per panel for a mosaic
tcm writeback --target "NGC 6888 - Crescent"           # dry-run scoped to that target
tcm writeback --target "Mosaic - Cygnus Loop" --apply  # each panel's counts -> its own TS plan

# Override any path; all four are optional and default to this dev machine (see Shared\DevDefaults.cs)
tcm --catalog PATH --library PATH --ts PATH --tolerance DEG
```

Defaults (in `Shared\DevDefaults.cs`, a linked source file both heads compile): catalog
`E:\Photography\Astro Photography\Processing\Catalog\Catalog.db`,
library `E:\Photography\Astro Photography\Processing`, TS db the shared working copy under
`Processing\Catalog\TS Database\schedulerdb.sqlite` (one location TCM + IS read, re-copyable from **BIRDWATCHER**;
`schedulerdb - Copy.sqlite` restores it). The live TS database lives on BIRDWATCHER (cross-machine), so the
default is a local copy — `writeback --apply` against it is restorable, never the live db.

## Tests

There are **no tests in this repo** — `Program.cs` is a thin host. The logic it drives is covered by tests in
the **library repo**:

```bash
# from ..\Library
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj            # full suite
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj \
  --filter "FullyQualifiedName~TargetResolver"                                 # single test/class
```

## The source-of-truth model (why the code is shaped this way)

The disk image library is **ACTUAL** (ground truth of what was captured). N.I.N.A. Target Scheduler (TS) is the
**PLAN**. `Catalog.db` reconciles the two onto **one canonical `target`** row carrying both facets, tagged by
`source_id`: `Actual` (on disk only), `Planned` (in TS only), `Both` (resolved onto one row). Because
`inventory_filter` (actuals) and `exposure_plan` (goals/`desired_count`) both hang off the one target,
"goal vs actual" is a single join.

The pipeline `Program.Main` runs (all in `Astronomy.Catalog`):
`ImageLibraryScanner` (Scan/) → `TargetResolver` + `CatalogBuilder.BuildAsync` (Build/) →
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
- **Single writer + WAL** — TCM writes; consumers open via `SchemaManager.OpenReadOnly`. WAL is unhappy over
  network shares (relevant if a consumer runs on another PC).

## Shared-library discipline

`Astronomy.Catalog` is consumed by XFM / TP / IS / ISP. When editing the library, **do not bake
consumer-specific terminology into its public surface** — use "caller"/"consumer" framing; doc strings describe
the abstract contract, not how one app happens to use it. Consumer-specific behavior belongs in TCM or the
consumer, not the contract. The catalog's actual-only world for XFM is `CatalogStore.GetShotTargets()`
(source `Actual` | `Both`).
