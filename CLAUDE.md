# CLAUDE.md

**Always-loaded router** for TargetSchedulerManager — read first; it orients you and points to every other doc. Keep it thin: deep detail lives in the docs it routes to.

## What this is

TargetSchedulerManager (TSM) is a .NET 10 **WinUI 3 app** (assembly `tsmui`) that **manages the N.I.N.A. Target
Scheduler database** — view + edit TS plans with disk-ACTUAL beside every number. It edits a **local working
copy** under the sync model (2026-07-06): pull from BIRDWATCHER at open (baseline-skipped when unchanged),
journaled local edits + automatic write-back, one reviewed **Push** replaying only edited fields back. It scans
the disk image library *read-only* (a fresh in-memory scan each load) purely to show plan-vs-actual; it does
**not** own or write `Catalog.db`.

> **History:** born **TargetCatalogManager (TCM)** with a `tcm` CLI that built `Catalog.db`; the CLI was removed
> and the project renamed to TSM on 2026-06-11 (catalog-building → future **LCM**, sibling `..\LibraryCatalogManager`).
> Pre-rename docs/git say TCM/`tcm`/`tcmui`. Full story: `CHANGELOG.md`.

**Almost all logic lives in the sibling shared library `Astronomy.Catalog`** (a different git repo at `..\Library`).
When a change is about schema, scanning, reconciliation, or TS interop, you are almost certainly editing files
under `..\Library\Astronomy.Catalog`, not this repo. See `..\Library\CLAUDE.md` for the library's own guidance.

## Docs — where to look (this file routes)

Reference docs (current truth — update in the same commit as the code):
- **`ARCHITECTURE.md`** — how it works: design + the load-bearing invariants.
- **`ROADMAP.md`** — phased plan + current status (shipped history → `CHANGELOG.md`).
- **`DOMAIN.md`** — the human/strategy home: UI design language (grid look-and-feel + the "add a UI element" checklist) + domain conventions (incl. the TS authoring conventions).
- **`TS-SCHEMA.md`** — the TS database external contract: exhaustive tables/columns, hierarchy + vocabulary, Id-vs-guid identity, drift-check recipe for TS upgrades.
- **`VERIFICATION.md`** — how to build, run, test, and verify a change.

Journal (dated capture — `glob docs/**/*.md` + grep; not enumerated here): `docs/YYYY-MM-DD-*.md` (decision records, reviews) + `docs/archive/` (spent records — executed plans, closed reviews; each carries a dated status banner) + `NOTEBOOK.md` (running lab notebook of small findings) + `CHANGELOG.md` (shipped-history journal, newest first — the full history behind ROADMAP's current-status summary).

Scope-excluded (not this project's docs): `.claude/`, `openspec/` **workflow files** (proposals/tasks in flight
— tooling), `bin`/`obj` (generated). **Not excluded:** `openspec/specs/` (live contract) and archived
`openspec/changes/archive/*/design.md` — the reference docs above cite these as the authoritative shipped
rationale, and an archived `design.md` is an **immutable** change record (never edit, relocate, or trim one;
graduate *from* it by writing a pointer into the reference doc). `..\Library` is a separate repo with its own docs.

## Two-repo layout

| Repo | Path | Role |
|---|---|---|
| **TargetSchedulerManager** (this) | `E:\Projects\…\TargetSchedulerManager` | the WinUI 3 app: a TS-database manager (view + edit TS; disk read-only for plan-vs-actual). App-only since 2026-06-11. |
| **Astronomy.Catalog** + deps | `E:\Projects\…\Library` | the shared schema/build **contract** every consumer references |

TSM has three cross-repo `ProjectReference`s: `..\Library\Astronomy.Catalog\Astronomy.Catalog.csproj`,
`..\Library\Astronomy.Diagnostics\Astronomy.Diagnostics.csproj` (the shared logging/observation contract),
and `..\Library\Astronomy.Core\Astronomy.Core.csproj` (night window + visibility math for the
Visible-tonight pass, added 2026-07-23) (local disk is source of truth; no NuGet/package hop).
`Astronomy.Catalog` pulls in `Astronomy.XISF` (XISF header reader for the scanner). Build specifics
(pure-managed, plain `dotnet build`, why the `.vcxproj` MSBuild caveat doesn't apply here) → `VERIFICATION.md`.

## Build, run, test, verify

See **`VERIFICATION.md`** — build/run commands, the test projects, and the xUnit-v3 build trap. TSM is
pure-managed (plain `dotnet build`); visual/UX correctness is verified by **running the app**, not the build.

## Source-of-truth model + load-bearing invariants

Disk = **ACTUAL**, TS = **PLAN**, reconciled onto **one canonical `target`** (`source_id` Actual / Planned /
Both). Full model + the build pipeline (`ImageLibraryScanner` → `TargetResolver` + `CatalogBuilder` →
`CatalogStore` → `Reconciler`) → `ARCHITECTURE.md` → *Source-of-truth model* / *Components*.

Invariants (condensed mirror of `ARCHITECTURE.md` → Key facts — **edit both**; full detail there):
- **Coordinate-primary, scope-equal matching** — each TS target anchors to the nearest disk unit *of its own
  scope* within a haversine tolerance (default **0.5°**; an *unaligned* panel claim only within **0.1°** —
  a name-aligned panel directory anchors at the full 0.5°, an unrelated framing nearby stays unclaimed);
  name validates (panels via their directory token);
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
- **Busy exclusion** — bulk operations (load, pull, push, visible-tonight) are mutually exclusive via
  `TryBeginBusy()`/`EndBusy()` (the only writers of `IsLoading`); row edits are refused in the VM funnel while
  one runs and their surfaces disable off `CanEdit`; an in-flight edit blocks a bulk op from starting. The
  visible-tonight pass batches through `TsEditGate.ApplyManyAsync` — targets, then project flips derived
  from the target flips that **landed** — under one unbroken busy scope (no seam admits an edit). The gate is
  **bulk-vs-edit only**; edit-vs-edit ordering is `CommitChain` (per editing surface).

## Shared-library discipline

`Astronomy.Catalog` is a shared multi-consumer library (TSM is today's only live consumer). When editing it,
**keep consumer-specific terminology out of its public surface** — caller/consumer framing, abstract-contract
doc strings. Full rule → `..\Library\CLAUDE.md`; the consumer split + the actual-only `CatalogStore.GetShotTargets()`
world → `ARCHITECTURE.md` → *Components*.
