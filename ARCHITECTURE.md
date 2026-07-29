# TargetSchedulerManager (TSM) — Architecture

**Charter:** how TSM works — design, components, and the load-bearing invariants (matching/harden
rules, mosaic model, single-writer). Read it for *why the code is shaped this way*. The blow-by-blow of the
four long-running subsystems — sync model, sync-direction marks, write-back, visible-tonight — moved to
**`SUBSYSTEMS.md`** on 2026-07-26; the invariant *mirrors* below stay here, and each points there.

TSM is a .NET 10 WinUI 3 app that **manages the N.I.N.A. Target Scheduler database**: view + edit TS plans on a
local working copy (pull from BIRDWATCHER at open, reviewed push-as-replay back — `SUBSYSTEMS.md` → *TS sync model*),
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
  reconciliation editor over the **local TS working copy** (synced with BIRDWATCHER by pull/push — `SUBSYSTEMS.md` →
  *TS sync model*): plan vs disk-ACTUAL per (target, filter, purpose, exposure seconds), tiered
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
- **The capture configuration is the cell key** (2026-07-27, openspec `capture-config-keys`; framing added
  2026-07-29, openspec `rotation-framing-key`). A reconciliation cell is `(target, filter, purpose,
  whole-second exposure, gain, offset, binning, framing cluster)` — everything that decides whether frames
  combine into one integration. A plan and a disk aggregate share a cell, and therefore render as one `Both`
  row, **only when they agree on every one of those**; otherwise the grid emits a TS row and one or more Disk
  rows, and that separation is the diagnostic (a plan specifying gain 0 never absorbs frames captured at
  gain 53; a re-framed plan never absorbs the old framing's frames). **Camera is deliberately NOT in the key**:
  a TS profile cannot name a camera, so including it would split cells against a plan that can never match
  them — it rides disk-side cells as a label and never prevents pairing. Scanner aggregates carry the same
  configuration, so gain/offset/binning are *uniform* within an aggregate rather than a mode over mixed frames.
  Offset is read **raw**: the writer stores it already in the scale TS's templates use (its "divided by N"
  comment is descriptive), so it must never be rescaled per camera. `WriteBackPlanner` keeps the coarser key
  `(target, filter, purpose, seconds)` and *sums* inventory rows — but since 2026-07-29 only rows whose
  **framing serves the target's rotation** join the sum (see the framing invariant below), so a re-framed
  plan stamps its true progress instead of staying credited with the old framing's frames.
- **Framing = (field-center, sky-rotation), clustered per unit before the aggregate grouping**
  (2026-07-29, openspec `rotation-framing-key`; formal contract → the `framing-keys` spec).
  `FramingClusterer` partitions a unit's frames by rotation **expression** first — sky (`OBJCTROT`),
  mechanical-only (`POSANGLE`), unknown — then single-linkage clusters the angle **folded mod 180°** (5°
  tolerance) and splits each angle group by field center (0.5° single-linkage), so a **pier flip merges**
  (identical footprint; the fold) while 180°-apart fields with genuinely different centers — and translated
  strays at an unchanged angle — still separate (the centroid guard, as a consequence of ordering).
  A single stray frame IS a cluster: low-count off-footprint framings are the PixInsight reference-frame
  hazard this exists to surface. **Rotation joins the pairing test only as expressed by both planes**: a
  cluster's sky fold-angle vs the TS *target's* rotation (target-level — TS cannot express per-plan rotation),
  fold-180 within the same 5°; mechanical/unknown clusters and rotation-less targets skip the term (the camera
  precedent). Mechanical is **never converted** to sky — the zero point drifts 19–35° across remounts.
  Disk rows whose sky rotation fails the plan carry the warning `framing` badge. The participation predicate
  lives ONCE as `FramingCluster.ServesPlanRotation` with three consumers — pairing/badge cue, bulk write-back
  crediting, surgical write-back routing — so the badge and the stamped counts can never tell different
  stories (a non-serving cell on the surgical path surfaces as a `FramingMismatch` note, never a silent
  skip). Tolerances are constants on `FramingCluster`, not settings (measured: real framings ≥ 9° apart,
  jitter ≤ 0.2°). Editing a target's `rotation` re-keys pairing AND re-credits write-back on the next load —
  the first edit that changes row *identity*, by design.
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
  on another PC.) *In-process threading model* (true by construction, 2026-07-24 review): the UI thread
  serializes every command via the busy gate, workers do I/O only, and `TsJournal` + `TsInboundStore` are the
  **only** cross-thread mutables (both coarsely locked). There is no lock-then-await and no sync-over-async on
  the UI thread — `TsDatabaseResolver.Stat`'s blocking wait always runs on a worker, and its continuation
  writes `LastProbe`/`HasProbed` back on the UI thread.
- **Busy exclusion (shipped 2026-07-24, openspec `busy-exclusion`):** in-app, the one-writer rule is enforced
  structurally, not by convention — every bulk db-touching operation (load/reload, pull, push, visible-tonight)
  acquires `MainViewModel.TryBeginBusy()` / `EndBusy()` (check-and-set on the UI thread; the only writers of
  `IsLoading`); row edits are **refused in the view-model funnel** while one runs (`RefuseIfBusy` — status note,
  control reverts) and their surfaces disable off `CanEdit` (whole-ListView + busy-sensitive toolbar buttons;
  Cancel/search/filters/Ambiguities stay live — the button is phase-scoped, `DOMAIN.md` → *Chrome*). The
  reverse direction is closed too: an in-flight funnel
  call (edit *or* read — both hold a db connection) makes `TryBeginBusy` **refuse immediately** (status note,
  "try again in a moment" — it never silently waits for quiescence), so no
  edit can overlap a load's write-back, a pull's atomic swap, or a push replay. The visible-tonight pass applies
  its flips as **two sequenced worker batches** (`TsEditGate.ApplyManyAsync`; `ApplyAsync` is its one-element
  case) — targets, then the project flips derived from the target flips that **landed** — under one unbroken
  busy scope, so the UI-thread seam between the batches admits no bulk op and no row edit (2026-07-24,
  `visible-tonight-applied-states`). Batching is *also* why `ApplyManyAsync` exists rather than a loop over
  `ApplyAsync`: the gate would make a per-edit loop equally atomic, but it pays one connection-open and one
  `Task.Run` hop **per flip** (`openspec/changes/archive/2026-07-24-busy-gate/design.md` D4) — don't
  "simplify" it back. The gate covers **bulk-vs-edit only** — deliberately *not* edit-vs-edit:
  per-surface commit ordering is `CommitChain` (a UI-thread task chain, one per `TsFieldsEditor` instance and
  one per window for inline Desired), so rapid re-confirmations of a field apply in confirmation order and
  `_lastKnown` can never disagree with the db (2026-07-24,
  `openspec/changes/archive/2026-07-24-serial-commits/design.md`). Authoring rule for any new bulk command:
  sequence its trailing reload **after** `EndBusy()` — a reload left inside the busy scope silently no-ops,
  since it tries to acquire the gate it is already holding
  (`openspec/changes/archive/2026-07-24-busy-gate/design.md` D1).
- **TS interop:** reads via `TargetSchedulerReader` (opened `Mode=ReadOnly` + busy-timeout, explicit column
  lists, schema-version aware); edits via the in-grid editing path (reference-driven `TsEditableSchema`) onto
  the local copy; write-back runs automatically each load (`SUBSYSTEMS.md` → *TS write-back*). All TS interop (read *and* write) is a
  **stop-gap** until IS/ISP reads `Catalog.db` directly — though a *long* one (TS is the daily scheduler until
  IS exists), and the UI shell built on it is permanent; only the TS data layer is disposable. The live TS DB
  lives on the imaging PC (BIRDWATCHER, cross-machine); **TSM never edits it over SMB** — it pulls a copy and
  pushes a reviewed replay (`SUBSYSTEMS.md` → *TS sync model*). This re-reverses the 2026-06-26 live-SMB-writes
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
  `acc = acq = disk` *write* is the write-back contract (`SUBSYSTEMS.md` → *TS write-back*), applied **automatically to the local copy on
  every load** (`WriteBackStep`) and reviewed — decreases first — at push.
- **Reuse:** the scan is `Astronomy.Catalog.Scan.ImageLibraryScanner` (on `Astronomy.XISF`'s header reader); the
  SQLite mapper pattern came from XFM.


## The subsystems — `SUBSYSTEMS.md`

The four long-running machines have their own file (carved out 2026-07-26, verbatim). *Key facts* above keeps
the one-line invariant each is bound by; the detail — the decisions, the guards, the rejected alternatives —
lives there:

| Subsystem | Read it for | Formal contract |
|---|---|---|
| **TS sync model** | pull/skip rule, atomic pull, journal, push-as-replay, truthful outcomes | `openspec/specs/ts-sync-model/` |
| **Sync-direction marks** | how `←` / `→` / `⇄` are derived on rows, headers, and fields | `openspec/specs/edit-direction-marks/` |
| **TS write-back** | the disk-is-master contract, the join, manual gating, the guards | `openspec/specs/write-back/` |
| **Visible-tonight pass** | the predicate, the flip rules, the two batches | `openspec/specs/visible-tonight-toggle/` |
