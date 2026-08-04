# CLAUDE.md

**Always-loaded router** for TargetSchedulerManager — read first; it orients you and points to every other doc. Keep it thin: deep detail lives in the docs it routes to.

## What this is

TargetSchedulerManager (TSM) is a .NET 10 **WinUI 3 app** (assembly `tsmui`) that **manages the N.I.N.A. Target
Scheduler database** — view + edit TS plans with disk-ACTUAL beside every number. It edits a **local working
copy** under the sync model (2026-07-06): pull from BIRDWATCHER at open (baseline-skipped when unchanged),
journaled local edits + automatic write-back + right-click adoption of disk-only cells into TS
(created rows journal as inserts), one reviewed **Push** replaying only the journaled changes back. It scans
the disk image library *read-only* (a fresh in-memory scan each load) purely to show plan-vs-actual; it does
**not** own or write `Catalog.db`.

> **History:** the repo began dual-head, with a console CLI that built `Catalog.db`; the CLI was removed
> and the project took its current name on 2026-06-11 (catalog-building → future **ISM**, sibling
> `..\IntervalSchedulerManager`). Full story: `CHANGELOG.md`.

**Almost all logic lives in the sibling shared library `Astronomy.Catalog`** (a different git repo at `..\Library`).
When a change is about schema, scanning, reconciliation, or TS interop, you are almost certainly editing files
under `..\Library\Astronomy.Catalog`, not this repo. See `..\Library\CLAUDE.md` for the library's own guidance.

## Docs — where to look (this file routes)

Reference docs (current truth — update in the same commit as the code):
- **`ARCHITECTURE.md`** — how it works: design + the load-bearing invariants. Read it first.
- **`SUBSYSTEMS.md`** — the four long-running machines in detail: TS sync model · sync-direction marks ·
  TS write-back · visible-tonight pass. Carved out of `ARCHITECTURE.md` 2026-07-26; each pairs with a formal
  contract under `openspec/specs/`.
- **`CONVENTIONS.md`** — how code is written and **where it goes**: the one-plausible-home map, invariants-at-
  the-enforcement-point, single-forward-pass flows, the view/VM seam, the `FireAndLog` async rule. Read before
  choosing a file to edit.
- **`ROADMAP.md`** — phased plan + current status (shipped history → `CHANGELOG.md`).
- **`DOMAIN.md`** — the human/strategy home: UI design language (grid look-and-feel + the "add a UI element" checklist) + domain conventions (incl. the TS authoring conventions).
- **`TS-SCHEMA.md`** — the TS database external contract: exhaustive tables/columns, hierarchy + vocabulary, Id-vs-guid identity, drift-check recipe for TS upgrades.
- **`VERIFICATION.md`** — how to build, run, test, and verify a change.
- **`RELEASING.md`** — the GitHub publish rules: local repo = ground truth, `origin/main` = the public
  face (`dev` never pushes); README storefront + deliberately-public content calls.

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
`Astronomy.Catalog` pulls its own library deps (`..\Library\CLAUDE.md` has the map). Build specifics
(pure-managed, plain `dotnet build`, why the `.vcxproj` MSBuild caveat doesn't apply here) → `VERIFICATION.md`.

## Build, run, test, verify

See **`VERIFICATION.md`** — build/run commands, the test projects, and the xUnit-v3 build trap. TSM is
pure-managed (plain `dotnet build`); visual/UX correctness is verified by **running the app**, not the build.

## Source-of-truth model + load-bearing invariants

Disk = **ACTUAL**, TS = **PLAN**, reconciled onto **one canonical `target`** (`source_id` Actual / Planned /
Both). Full model + the build pipeline (`ImageLibraryScanner` → `TargetResolver` + `CatalogBuilder` →
`CatalogStore` → `Reconciler`) → `ARCHITECTURE.md` → *Source-of-truth model* / *Components*.

Invariant *names* + one-line hooks — the full statements live ONCE in `ARCHITECTURE.md` → *Key facts*
(single source; don't restate here):
- **Matching** — coordinate-primary, scope-equal; aligned claims outrank; disk plate-solved coords win;
  problem rows are reported in `CatalogBuildReport`, never dropped.
- **Cell key** — the capture configuration + framing key reconciliation; `Both` only on full agreement on
  every dimension both planes express; camera is a disk-side label, never a key.
- **Mosaic model** — a panel is a normal target with a composite key; the parent is a grouping node.
- **No migration framework** — the catalog is derived and rebuildable; a schema change deletes `Catalog.db`.
- **Harden rule** — raw TS integers never reach CHECK/FK columns; coerce + report so one bad row can't
  abort the rebuild.
- **Single writer + WAL** — one writer per db; consumers open read-only.
- **Busy exclusion** — bulk ops and row edits are structurally mutually exclusive (`TryBeginBusy`);
  edit-vs-edit ordering is `CommitChain`.

## Shared-library discipline

`Astronomy.Catalog` is a shared multi-consumer library (TSM is today's only live consumer). When editing it,
**keep consumer-specific terminology out of its public surface** — caller/consumer framing, abstract-contract
doc strings. Full rule → `..\Library\CLAUDE.md`; the consumer split + the actual-only `CatalogStore.GetShotTargets()`
world → `ARCHITECTURE.md` → *Components*.
