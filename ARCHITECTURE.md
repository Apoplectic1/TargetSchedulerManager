# TargetSchedulerManager (TSM) — Architecture

**Charter:** how TSM works — design, components, and the load-bearing invariants (matching/harden
rules, mosaic model, single-writer). Read it for *why the code is shaped this way*; grep by subsystem.

TSM is a .NET 10 WinUI 3 app that **manages the N.I.N.A. Target Scheduler database**: view + edit TS plans on a
local working copy (pull from BIRDWATCHER at open, reviewed push-as-replay back — the sync-model section below),
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
Tom Palmer's TS database; its grid replaces XFM's Target Scheduler tab (already deleted — XFM went TS-free
2026-07-07, independent of the Phase 5 cutover).

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
  editing (counts/toggles → identity → project knobs; structural fixes are hand-edits in NINA's TS UI, not
  TSM verbs — resolver rejected 2026-07-08). Each load runs scan → resolve → project
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
  `TargetSchedulerEditor.TrySetField(...) → (FieldEditResult?, RefusalReason)`, which folds the five guard predicates (four
  open-db checks + the cadence-scope `HasOverrideOrder` refusal) into one structured-refusal call. UI-side, `Controls/TsFieldsEditor` generates the edit
  form from `TsEditableSchema` (control type per `TsFieldType`, bounds/enum maps/units from the reference;
  cadence-breaking fields commit directly — the library clears `filtercadenceitem` atomically with the
  write, so no confirm dialog is needed) and commits per field back through the gate — the reference is the
  single source of truth from SQL whitelist to rendered control.
- **`Catalog.db` and its consumers** — the persistent catalog is the planned **LCM**'s output (was the retired
  CLI's job). TP / IS / ISP will open it read-only via `SchemaManager.OpenReadOnly` (XFM opted out 2026-07-07 —
  TS-free, never consumes `Catalog.db`). The actual-only world for actuals-only consumers is
  `CatalogStore.GetShotTargets()` (source `Actual` | `Both`).

## Key facts

- **Catalog DB location (when built):** `E:\Photography\Astro Photography\Processing\Catalog\Catalog.db`
  (co-located with the data it indexes). Currently unbuilt — nothing consumes it yet; LCM will own it.
- **Reconciliation:** coordinate-primary, scope-equal — every disk unit (a top-level dir OR one mosaic panel)
  carries a *scope key* (the default scope for top-level units; the mosaic's normalized name for its panels;
  none for a mosaic parent, which matches by project name); each TS target derives its scope from its own
  grouping (isMosaic project → that scope, else default). ONE rule: anchor to the nearest in-tolerance unit
  of the same scope (default 0.5° haversine; a panel claim whose directory token does NOT align is limited
  to a tight 0.1° radius — aligned directories anchor at the full tolerance since the name confirms identity
  and absorbs real recenter drift, while an unrelated framing merely nearby stays unclaimed, 2026-07-23);
  name validates (a panel validates via its directory token —
  `Panel 01of16` → `P1`); **an aligned claim outranks an unaligned one** (a nearby-but-differently-named
  target releases back to planned instead of piling onto a directory a correctly-named target owns — the
  Witch Head shape). Disk plate-solved coords win on merge; the TS guid is retained on `Both` for write-back.
  Cross-scope matches are impossible by construction (`CygnusLoop P3` can never grab `NGC 6995`). Duplicates /
  mismatches / ambiguous / unanchored rows are reported in `CatalogBuildReport`, never dropped — a multi-claim
  is always a duplicate (the alias-fold escape was removed 2026-07-23; one TS row per position, no exceptions).
- **Mosaics = target hierarchy:** a panel **is a normal target** whose key is composite. A `Mosaic - <Name>`
  dir nests an extra panel level; the scanner's one walk feeds both the whole-target aggregate and per-panel
  sub-reports; the resolver emits one **parent row** (grouping node — no plans, no inventory) plus one
  **child target per panel** (`parent_target_id` set; `directory_name` = `<mosaic dir>/<panel label>`; own
  centroid, own TS provenance, own plans + inventory). Bulk write-back therefore treats panels as ordinary
  targets (the old mosaic→manual routing is gone); `GetShotTargets()` returns top-level rows only (panels
  via `GetChildTargets`); reporting rolls panel reconciliations up under the parent's name
  (`Reconciler.Merge`). A mosaic-parent **project-priority** edit is one `project.priority` write: TS's own
  scoring cascade has panels at priority Default (−1) inherit the project value, while per-panel priority
  overrides survive — so the single write reprioritizes the mosaic without disturbing intentional per-panel
  differences. Each panel reconciles independently (matched / planned-only / disk-only) and rolls up under the parent; live
  counts are a run-time measurement (app + `tsm.log`), not pinned here.
- **Schema rules:** GUID `BLOB(16)` PKs (big-endian, see `GuidBlob`), `snake_case`, NULL not sentinels, enum
  lookup tables + CHECK, substantive FKs indexed (small enum/lookup FKs may ride a composite index or go
  unindexed), UNIX-seconds timestamps. **No `schema_migration` / `user_version`** —
  a schema change just means deleting the regenerable `Catalog.db`.
- **Harden rule:** never pass a raw TS integer into a CHECK/FK column — `TargetResolver` coerces unknown
  epoch/state/priority codes to a safe default and normalizes/clamps planned RA/Dec, so one bad external TS row
  can't abort the rebuild.
- **Concurrency:** one writer per db — TSM is the only writer of the **local** TS copy (its own edits +
  write-back stamps); BIRDWATCHER's db is written only inside an explicit push replay (TS itself writes during
  imaging; the push's sidecar guard refuses that overlap). The future LCM is `Catalog.db`'s single writer with
  WAL on so consumers read without blocking. (WAL is unhappy over network shares — relevant if a consumer runs
  on another PC.)
- **Busy exclusion (shipped 2026-07-24, openspec `busy-exclusion`):** in-app, the one-writer rule is enforced
  structurally, not by convention — every bulk db-touching operation (load/reload, pull, push, visible-tonight)
  acquires `MainViewModel.TryBeginBusy()` / `EndBusy()` (check-and-set on the UI thread; the only writers of
  `IsLoading`); row edits are **refused in the view-model funnel** while one runs (`RefuseIfBusy` — status note,
  control reverts) and their surfaces disable off `CanEdit` (whole-ListView + busy-sensitive toolbar buttons;
  Cancel-pull/search/filters/Ambiguities stay live). The reverse direction is closed too: an in-flight funnel
  call (edit *or* read — both hold a db connection) blocks `TryBeginBusy` until its worker completes, so no
  edit can overlap a load's write-back, a pull's atomic swap, or a push replay. The visible-tonight pass applies
  its flips as **one worker batch on one editor session** (`TsEditGate.ApplyManyAsync`; `ApplyAsync` is its
  one-element case), so the pass has no UI-thread seams at all.
- **TS interop:** reads via `TargetSchedulerReader` (opened `Mode=ReadOnly` + busy-timeout, explicit column
  lists, schema-version aware); edits via the in-grid editing path (reference-driven `TsEditableSchema`) onto
  the local copy; write-back runs automatically each load (see below). All TS interop (read *and* write) is a
  **stop-gap** until IS/ISP reads `Catalog.db` directly — though a *long* one (TS is the daily scheduler until
  IS exists), and the UI shell built on it is permanent; only the TS data layer is disposable. The live TS DB
  lives on the imaging PC (BIRDWATCHER, cross-machine); **TSM never edits it over SMB** — it pulls a copy and
  pushes a reviewed replay (the sync-model section below). This re-reverses the 2026-06-26 live-SMB-writes
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
- **Pull is atomic, observable, cancellable (hardened 2026-07-23).** The backup lands in `<local>.pull-tmp`
  and is swapped over the local db only on completion (`ClearAllPools` first — a pooled reader handle would
  fail the swap), so a process death at *any* moment leaves the previous copy intact; a dead pull's tmp is
  swept by the next pull. The copy is chunked `sqlite3_backup` steps (~2 MB): the status line shows a **text
  percentage** (deliberately no progress-bar element) and **Cancel pull** stops between chunks — tmp
  discarded, no baseline recorded, previous copy untouched (during a push only the closing pull cancels;
  replay writes never do). The log carries `PULL starting` + completion duration — an interrupted pull used
  to be invisible, which is why the incident (app killed at ~87% of a latency-degraded ~40 s pull, leaving a
  hot journal the read-only reader could never recover and the baseline skip faithfully preserved) was
  undiagnosable live. The **torn-local gate** closes that skip-rule blind spot: a `-journal`/`-wal` beside
  the local db at open is healed loudly (`LOCAL TORN` log line; discard local + sidecars + baseline; pull
  fresh; torn + offline fails the load loudly instead) — the edit journal is untouched, so unpushed edits
  survive and replay at push. `Discard` also drops the baseline, so an interrupted discard-pull can't strand
  discarded values behind a matching skip.
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

## Sync-direction marks (grid column 0; shipped 2026-07-08)

Every row level (target header / mosaic panel / filter row / rollup detail line) carries one mark in the new
leftmost 24 px column: **`←`** = inbound (BIRDWATCHER arrived different at a pull), **`→`** = outbound
(unpushed journal writes — manual edits *and* write-back stamps), **`⇄`** = both, blank = clean. Tooltips:
per-field `old → new` lines on leaves, direction counts on headers. Spec:
`openspec/specs/edit-direction-marks/`.

- **Outbound is the journal, re-read.** No new state: a row marks `→` iff a journal entry's (table, key)
  matches its `PlanTsKey` / `TsTargetKey` / `ProjectTsKey` — so marks survive restarts (the journal sidecar
  persists) and a partial push's retained failures keep exactly their rows marked.
- **Inbound is a pull-time field diff** (`Shared/TsInboundDiff`): `TsSync.Pull` — the single choke point all
  four pull paths share (open / Pull-now / discard-and-pull / the closing pull after push) — snapshots the
  local db's diffable fields before the backup overwrites it, diffs against the fresh copy, and unions into a
  **session-sticky in-memory store** (`TsSync.Inbound`). The diffed set is authored (the columns TSM displays
  or edits — the `TsEditableSchema` convention), never `PRAGMA`-discovered, so TS-internal bookkeeping can't
  produce noise. First-ever pull (no local file) diffs nothing; no-pull sessions (offline / Continue-local)
  have no `←`; a remotely-added row reports one "new row" entry; deletions report nothing.
- **The actuals mask:** when write-back stamps a plan's `acquired`/`accepted`, `RecordWriteBack` drops those
  columns from the plan's inbound entries — disk supersedes the rig's totals, so the row reads `→` (never
  `⇄`) and goes clean after push, not stale-`←`. `desired` is deliberately not masked: a rig-side goal change
  coexisting with a ratchet raise is a genuine `⇄`.
- **Headers roll up the union of their subtree's directions** (`Services/SyncMarks`): own target key +
  project key (group header / mosaic parent only — a project edit never lights panels) + every plan key of
  their target ids from the retained graph (`CatalogGraph.Plans`) — the graph map matters because a plan
  folded into a multi-plan rollup row carries no row-level key — plus the plan keys visible child rows do
  carry. Sticky inbound means a push collapses `⇄` to `←` (the rig's change stays visible) rather than
  wiping the overnight info.
- **One in-place sweep** (`MainViewModel.RefreshAllMarks`): rebuilds the resolver from journal + inbound +
  graph and re-applies every mark via PropertyChanged (raise-on-change only, never a collection rebuild — the
  scroll-preserving in-place rule). Called from `ApplyFilters`, every applied edit, and a push without a reload
  (Discard refreshes marks via its own full reload, not a direct sweep). Known gap (accepted): exposure-template edits mark no row — templates have no grid row; the
  badge and push review still carry them.

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
  (pure) + `ImageLibraryScanner.ScanUnitsAsync` (per-panel scan). **Not app-wired yet** — the surgical path is a
  tested library capability (it backed the retired `tcm writeback --target`), but no TSM UI invokes it today; the
  app runs only the bulk automatic pass.
- **Audited.** The automatic write-back pass logs its outcome to the diagnostics log (`tsm.log` under
  `%APPDATA%\TargetSchedulerManager\Logs\`): one summary line (plans stamped, fields journaled, manual, ignored),
  a warning when cells need manual reconciliation, and a line per verify failure — no per-write `old→new` trail
  and no dry-run mode (both went with the retired CLI's `WriteBackAuditLog`). The reviewed **Push** dialog is
  where every stamp is shown `old→new` (decreases first) before it reaches BIRDWATCHER.
- **Held decisions surface as the ambiguity report** (`Services/AmbiguityReport`, 2026-07-08): a pure builder
  over the retained graph/report + a fresh in-memory `WriteBackPlanner.Plan` rolls every held cell, identity
  flag, and TS-internal check (same-key plans across all TS-sourced targets, planned-only twins, duplicate
  template names) into one printable Markdown file with hand-fix instructions — the tripwire's detail. TSM
  never resolves these itself (resolver rejected 2026-07-08; fixes are hand-edits in NINA's TS UI).

## Visible-tonight pass (toolbar group; shipped 2026-07-23)

A "Visible Tonight:" toolbar group — **Duration** (whole minutes, 15–480, default 30) and **Horizon**
(whole degrees, 0–89, default 30) numeric up-downs + a **Find** button (it replaced the toolbar's old
load-summary text, removed same day). One press reconciles the enable state with tonight's sky — no
confirm dialog (user decision: "this is why it's a button"), push stays optional.

- **Predicate (deliberately TS-independent):** a target is *visible tonight* iff it has a **single
  contiguous window ≥ Duration** above the **Horizon altitude floor** between tonight's astronomical
  dusk and dawn — one library call,
  `CoarseVisibility.IsAboveHorizonForAtLeast(target, site, night, ScalarHorizonProfile(horizonDeg), minDuration)`.
  TS's own gates (`minimumaltitude`, custom horizon/offset, `minimumtime`, twilight levels) are **not**
  consulted — TS re-applies them itself at plan time; a rejected earlier draft that mirrored the TS gate
  (and promoted TP's `.hrz` parser into the library) was reverted 2026-07-23. "Tonight" is
  `NightCalculator.ComputeNight`'s bracket: the window whose dawn is the next dawn at-or-after now (the
  current night mid-night, the upcoming night in daylight).
- **Flip rules:** `target.active ← verdict` for every target of an `Active`/`Inactive` project; then
  `project.state ← any-enabled-child ? Active : Inactive` over the **post-pass** values (a project with no
  enabled targets — including one with no targets — goes Inactive). `Draft`/`Closed` projects and their
  targets are never read or written. Panels are ordinary target rows. No-op values journal nothing.
- **Data + writes:** consumes the load's retained `TsPlanData` snapshot (`LoadResult.Ts` — the single TS
  read; no re-open), plans as pure records (`Services/VisibleTonightPass`, unit-tested without SQLite),
  applies each flip through `TsEditGate.ApplyAsync` — so every flip journals, marks, badges, and replays
  at Push exactly like a hand edit — then reloads (no pull) and reports counts on the status line.
  Fail-fast: a processed TS target without RA/Dec aborts the whole pass **before any edit**.
- **Site input:** `DevDefaults` constants (Penns Park lat/long/TZ/elevation, mirroring TP's preset)
  materialized by `DevDefaults.Site()` — the app's first `Astronomy.Core` dependency (pure-managed; build
  model unchanged). The Duration/Horizon knobs live only on the toolbar — no `DevDefaults` constants.
