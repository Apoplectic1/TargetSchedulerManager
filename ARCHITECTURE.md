# TargetSchedulerManager (TSM) — Architecture

**Charter:** how TSM works — design, components, and the load-bearing invariants (matching/harden
rules, mosaic model, single-writer). Read it for *why the code is shaped this way*; grep by subsystem.

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
Tom Palmer's TS database; its grid replaces XFM's Target Scheduler tab (XFM's is deleted at the Phase 5
cutover — see `ROADMAP.md`).

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
  reconciliation editor over the **local TS working copy** (synced with BIRDWATCHER by pull/push — see the
  sync-model section below): plan vs disk-ACTUAL per (target, filter, purpose, exposure seconds), tiered
  editing (counts/toggles → identity → project knobs → structural). Each load runs scan → resolve → project
  **in memory** (`ReconciliationLoader`); no `Catalog.db` is needed or written. Its TS data layer is a
  deletable stop-gap; the **UI shell is permanent** and retargets `Catalog.db` when IS arrives.
  Guarded TS writes go through two App-side modules: **`TsSync`** (`Shared/`) owns the sync model — the two
  paths, the timeout-guarded remote probe (`TsDatabaseResolver.Stat`), the persisted baseline
  (`TsSyncState`) + journal (`TsJournal`), the pull/skip decision, and the push replay; **`TsEditGate`**
  (`Shared/`) is the one guarded write — `ApplyAsync(...) → EditOutcome` (`Applied`/`Refused`/`Failed`) over
  an injected `ITsEditor`, always targeting the local copy, journaling every verified write for push — plus
  the read half, `ReadFieldsAsync(table, key)`: the current db values of one row's editable columns
  (drift-absent columns omitted via `IsFieldAvailable`; row-missing/fault → null, never fabricated defaults),
  which seeds the edit-flyout form. Both take their dependencies by injection, so the sync machine, the
  journal, and the guarded write are unit-tested without SMB (the pull's backup path over real temp SQLite
  files). The library half is the consumer-neutral
  `TargetSchedulerEditor.TrySetField(...) → (FieldEditResult?, RefusalReason)`, which folds the four open-db
  guard predicates into one structured-refusal call. UI-side, `Controls/TsFieldsEditor` generates the edit
  form from `TsEditableSchema` (control type per `TsFieldType`, bounds/enum maps/units from the reference;
  cadence-breaking fields commit directly — the library clears `filtercadenceitem` atomically with the
  write, so no confirm dialog is needed) and commits per field back through the gate — the reference is the
  single source of truth from SQL whitelist to rendered control.
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
- **Concurrency:** one writer per db — TSM is the only writer of the **local** TS copy (its own edits +
  write-back stamps); BIRDWATCHER's db is written only inside an explicit push replay (TS itself writes during
  imaging; the push's sidecar guard refuses that overlap). The future LCM is `Catalog.db`'s single writer with
  WAL on so consumers read without blocking. (WAL is unhappy over network shares — relevant if a consumer runs
  on another PC.)
- **TS interop:** reads via `TargetSchedulerReader` (opened `Mode=ReadOnly` + busy-timeout, explicit column
  lists, schema-version aware); edits via the in-grid editing path (reference-driven `TsEditableSchema`) onto
  the local copy; write-back runs automatically each load (see below). All TS interop (read *and* write) is a
  **stop-gap** until IS/ISP reads `Catalog.db` directly — though a *long* one (TS is the daily scheduler until
  IS exists), and the UI shell built on it is permanent; only the TS data layer is disposable. The live TS DB
  lives on the imaging PC (BIRDWATCHER, cross-machine); **TSM never edits it over SMB** — it pulls a copy and
  pushes a reviewed replay (the sync-model section below). This re-reverses the 2026-07-01 live-SMB-writes
  decision: the SQLite-over-SMB risk and its `ClearAllPools` stale-page workaround are gone with the direct
  writes; the daily Macrium image of BIRDWATCHER remains the disaster-recovery path.
- **Grid count columns (display):** after `Desired` (TS goal) the grid shows **`TS`** = TS's recorded
  `exposureplan.acquired` (the count TS schedules on with the grader off) and **`Actual`** = on-disk frames
  (ground truth). TS `accepted` is deliberately **not** a column — with grading off TS increments acquired and
  accepted together (`ImageSaveWatcher` auto-accepts) and write-back re-sets them equal, so accepted only mirrors
  acquired; a rare in-session `accepted ≠ acquired` drift surfaces as a flagged **`acc≠acq` badge** instead
  (`BuildRows`; the data stays in the row model). *Why `TS`, not a TS %:* TS's grader-off `PercentComplete`
  divides acquired by `ExposureThrottle × Desired` (default **125 %**), so a target shot to `Desired` reads ~80 %
  and TS keeps scheduling it — TSM's `remaining = max(0, Desired − Actual)` is the honest completion. The
  `acc = acq = disk` *write* is the write-back contract (below), applied **automatically to the local copy on
  every load** (`WriteBackStep`) and reviewed — decreases first — at push.
- **Reuse:** the scan is `Astronomy.Catalog.Scan.ImageLibraryScanner` (on `Astronomy.XISF`'s header reader); the
  SQLite mapper pattern came from XFM.

## TS sync model (pull → edit local → push-as-replay; shipped 2026-07-06)

TSM's one editing world. Design principle throughout: **buttons carry decisions, guards carry facts** —
correctness never depends on the user remembering cross-session state. All state lives in `Shared/TsSync` +
two sidecars beside the local db (`*.tsm-sync.json` baseline, `*.tsm-edits.jsonl` journal).

- **Pull on open, baseline-skipped.** When BIRDWATCHER answers the ~1.5 s stat probe, the open refreshes the
  local copy via the SQLite **online backup API** (torn-copy-safe while NINA holds the file; never `File.Copy`)
  — EXCEPT when the persisted baseline (remote size + mtime at last pull) matches **and** no remote
  `-wal`/`-shm`/`-journal` exists (WAL hides content changes from the main file's mtime). Unbaselined always
  pulls; the baseline is recorded from the *pre*-pull stat, so a mid-copy write can only cost an extra pull,
  never a false skip. Rapid test relaunches therefore skip the copy. Offline opens proceed on the local copy.
- **Every edit writes locally and journals.** The gate targets the local path only; each verified write appends
  `(seq, kind, table, key, column, absolute value, old, label, at)` to the journal. **Dirty ≡ journal
  non-empty** — derived from the persisted file, never a stored flag, so it is crash-safe by construction. The
  toolbar badge ("synced HH:mm · N unpushed") displays the facts; Push is enabled exactly when dirty.
- **Push = journal replay, never a file copy.** A file push is a time machine — it would revert everything
  BIRDWATCHER accrued since the pull (NINA's nightly counts, `acquiredimage` history, XFM's grades). Instead
  the collapsed journal (last write per field, first write's old for review) replays: **write-back entries**
  re-execute the write-back contract per plan via `TargetSchedulerWriter` (desired ratchets against the
  *remote* desired); **manual entries** replay per-field via the guarded, read-back-verified
  `TargetSchedulerEditor.TrySetField` — writer leg first, so an explicit desired edit outranks the stamp.
  Only journaled fields are touched. A remote open sidecar refuses the whole push; per-entry failures (row
  gone, verify mismatch) are reported loudly and retained in the journal. A fully-applied push ends in an
  immediate pull — the invariant everything hangs on: **a baseline is recorded exactly when the local copy
  mirrors the remote**.
- **Open-with-dirty prompts before any pull** (reachable + dirty): push (recommended — replay makes offline
  edits pushable at reconnect) / discard-and-pull (the deliberate debug-session path) / continue local. The
  push review dialog shows manual edits + the write-back summary with **decreases first**, and warns (not
  blocks) when the remote changed since the baseline — field replay makes cross-field interleaving safe;
  same-field collisions stay covered by the edits-by-day discipline.
- **Retired (2026-07-06):** the LIVE/LOCAL radios, direct SMB writes, `TsSource`, `EditOutcome.LiveDropped` +
  sticky-fall, and the post-write `ClearAllPools` SMB workaround. Edits can no longer fail from BIRDWATCHER
  dropping because they never travel over SMB.

## TS write-back (engine built 2026-06-08; app action shipped 2026-07-06)

`TargetSchedulerWriter` pushes disk-derived counts back into TS so its planner reflects ACTUAL. The engine
(`WriteBackPlanner` / `SingleTargetPlanner` / `TargetSchedulerWriter`) lives in AL and is fully tested; in the
app it runs **automatically after every load** (`Services/WriteBackStep`): plan from the fresh scan + local TS
read, stamp every non-no-op change into the **local** db, journal each changed column with the write-back kind
— so an unchanged system journals nothing and the session stays clean. BIRDWATCHER sees write-back only through
the reviewed push (decreases first). It is a **stop-gap** until IS/ISP consumes `Catalog.db` directly, so it
stays minimal and cleanly deletable. Load-bearing invariants (full spec in `ROADMAP.md` Phase 4):

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
- **Guarded, and copy-isolated by the sync model.** The automatic pass stamps the **local** copy; BIRDWATCHER
  is written only inside the reviewed push replay. The hard guards apply at both dbs — refuse an open
  `-wal`/`-shm`/`-journal` sidecar (TS mid-transaction), a read-only file, or missing `exposureplan` columns
  (*not* an exact schema version, which the NINA-nightly bumps) — plus diff-first (no-ops produce no write and
  no journal entry), one transaction, and read-back verify. No app-side backups — the daily Macrium image is
  the recovery path and both DBs are recreatable. The writer uses a private SQLite cache (so it doesn't inherit
  the build-reader's read-only shared cache); a fresh re-scan each run can't push stale numbers.
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
