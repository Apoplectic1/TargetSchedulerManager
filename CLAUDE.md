# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TargetSchedulerManager (TSM) is a .NET 10 **WinUI 3 app** (assembly `tsmui`) that **manages the N.I.N.A. Target
Scheduler database** — view + edit TS plans with disk-ACTUAL beside every number. It scans the disk image library
*read-only* (a fresh in-memory scan each load) purely to show plan-vs-actual; it does **not** own or write
`Catalog.db`.

> **History (2026-06-11):** This project was **TargetCatalogManager (TCM)** — it *also* used to be a headless
> console host (`tcm`) that built `Catalog.db`. That CLI was removed — catalog-building moves to a future
> **LibraryCatalogManager (LCM)** (sibling dir `..\LibraryCatalogManager`, ROADMAP there) — and the project was
> then **renamed to TargetSchedulerManager** (same day) to match its real role: a TS-database manager. The
> catalog-build engine is one AL call (`CatalogBuilder.BuildAsync`, disk-only via `tsDb: null`); nothing was
> lost. Dated docs and git history before the rename say TCM/`tcm`/`tcmui`.

**Almost all logic lives in the sibling shared library `Astronomy.Catalog`** (a different git repo at `..\Library`).
When a change is about schema, scanning, reconciliation, or TS interop, you are almost certainly editing files
under `..\Library\Astronomy.Catalog`, not this repo. See `..\Library\CLAUDE.md` for the library's own guidance.

`ARCHITECTURE.md` (design), `ROADMAP.md` (phased plan + current status), and `docs/UI-CONVENTIONS.md` (the grid's
settled look-and-feel rules + a "when you add a UI element" checklist) are load-bearing — keep them current after
substantive changes, per the user's docs-as-memory convention.

## Two-repo layout

| Repo | Path | Role |
|---|---|---|
| **TargetSchedulerManager** (this) | `E:\Projects\…\TargetSchedulerManager` | the WinUI 3 app: a TS-database manager (view + edit TS; disk read-only for plan-vs-actual). App-only since 2026-06-11. |
| **Astronomy.Catalog** + deps | `E:\Projects\…\Library` | the shared schema/build **contract** every consumer references |

TSM has a cross-repo `ProjectReference` straight to `..\Library\Astronomy.Catalog\Astronomy.Catalog.csproj`
(local disk is source of truth; no NuGet/package hop). `Astronomy.Catalog` pulls in `Astronomy.XISF` (XISF
header reader for the scanner). Both are **pure-managed** (Microsoft.Data.Sqlite only), AnyCPU/x64, no native
deps — so this project graph builds with plain `dotnet build` (the `.vcxproj` MSBuild caveat does *not* apply
here; the native PCL projects are not in TSM's solution).

## Build & run

```bash
# Build (slnx pulls in Astronomy.Catalog + Astronomy.XISF from ..\Library)
dotnet build TargetSchedulerManager.slnx -v:m -nologo

# Run the WinUI app: TS plan vs disk grid (fresh in-memory scan on load, no Catalog.db needed); edit TS live
TargetSchedulerManager.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/tsmui.exe

# Tests (App.Tests only)
dotnet test TargetSchedulerManager.slnx -v:q --nologo
```

Path defaults live in `TargetSchedulerManager.App\Shared\DevDefaults.cs` (a normal App file since the CLI was
removed). **TS db: `TsDatabaseResolver` prefers the LIVE BIRDWATCHER db
(`\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite`, over SMB) when network-reachable, else the local working
copy** under `Processing\Catalog\TS Database\schedulerdb.sqlite` (`schedulerdb - Copy.sqlite` restores it). A
toolbar badge says LIVE vs LOCAL; live writes are guarded (refuse open sidecar / read-only) + read-back verified;
daily Macrium imaging of BIRDWATCHER is the recovery path. The library + catalog defaults also live in
`DevDefaults.cs` (used by the in-memory scan+resolve).

**Write-back** (push disk-derived counts into TS) was a CLI verb; its engine (`WriteBackPlanner` /
`TargetSchedulerWriter`) stays in AL and will resurface as a TSM **app action**, not a console command.

## Tests

One test project in this repo (`dotnet test TargetSchedulerManager.slnx`):

- **`TargetSchedulerManager.App.Tests`** — the app's real logic: `ReconciliationLoader.BuildRows` (internal,
  via `InternalsVisibleTo`), `MainViewModel` filter/toggle pipeline (`SetRowsForTest` seam), row Hours/search
  rules, `RowAggregates`, `Format`, `ExpansionState`, `VisibleRowTree` (the flatten==splice invariant),
  `TsDatabaseResolver` (moved here from the retired
  `Cli.Tests`), and the guarded-TS-write seam — `TsSource` (LIVE/LOCAL state machine, probe injected) and
  `TsEditGate` (one guarded write over a stub `ITsEditor`). Runs in a **plain test host (no XAML runtime)**: never touch the `Brush` getters
  (`SecondsBackground`/`HoursBackground` need `Application.Current`) — those stay app-verified. `TestEnv` blanks
  `TSM_DIAG` so VM tests can't write the user's session log.

> **Trap (xUnit v3):** `App.Tests` is xUnit v3 (`OutputType=Exe`). **Never let `xunit.v3` land on
> `TargetSchedulerManager.App`** (or any non-test project) — a "Manage NuGet Packages for Solution → all
> projects" action sprays it silently, and the `mtp-v1` targets then fail the build with "test projects must
> be executable" (this hit the whole `.slnx` graph on 2026-06-21). Full detail in `..\Library\CLAUDE.md`.

The heavy logic (schema / scan / resolve / write-back) is covered in the **library repo**:

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
- **Single writer + WAL** — one writer per db: the TS db's in-app editor here, `Catalog.db`'s builder (future
  LCM) there; consumers open via `SchemaManager.OpenReadOnly`. WAL is unhappy over network shares (relevant if
  a consumer runs on another PC).

## Shared-library discipline

`Astronomy.Catalog` is consumed by XFM / TP / IS / ISP. When editing the library, **do not bake
consumer-specific terminology into its public surface** — use "caller"/"consumer" framing; doc strings describe
the abstract contract, not how one app happens to use it. Consumer-specific behavior belongs in TSM or the
consumer, not the contract. The catalog's actual-only world for XFM is `CatalogStore.GetShotTargets()`
(source `Actual` | `Both`).
