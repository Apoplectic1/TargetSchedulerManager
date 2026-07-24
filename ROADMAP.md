# TargetSchedulerManager (TSM) — Roadmap

**Charter:** the forward-looking plan + current status + a short Recently-shipped digest. Read it for
*what's next / where things stand*. Full shipped history lives in `CHANGELOG.md` (git is the commit-level backstop).

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

> **Naming:** this project was **TargetCatalogManager (TCM)** until 2026-06-11. Dated entries below the rename
> keep the names they shipped under (TCM, `tcm`, `tcmui`, `tcm.log`, `TCM_DIAG`) — they match the git history.

## Status — pick up here (2026-07-23)

TSM is the WinUI **TS-database manager**, app-only (CLI removed 2026-06-11): a reconciliation grid of TS plan vs
disk-ACTUAL — fresh in-memory scan each load (no `Catalog.db`), per-(target, filter, purpose, seconds) plane rows,
over the **local TS working copy** under the pull → edit-local → push-as-replay **sync model** (shipped
2026-07-06: baseline-skipped pull at open, journaled local edits, reviewed Push to BIRDWATCHER, automatic
write-back each load — user-verified against the rig same day). Editing shipped so far: target enable checkbox
+ in-grid `desired` (verified live in NINA pre-sync-model) + the **edit flyout** (hover glyph / right-click on
target + filter rows → schema-generated form: target `priority`/`rotation`, plan `exposure`; per-field guarded
commit). Each load reconciles disk targets against TS targets into Both / Planned-only / Actual-only, with
mosaics resolved per panel; live counts show in the app + `tsm.log` (not pinned here — they move with every
edit and every imaging night). Match tolerance **0.5°** (validated 2026-06-04).
**Next:** editing-surface Parts 1–4 all shipped + verified (Part 4 below, 2026-07-07). The **load-split is
RETIRED** (2026-07-08): the full scan (~2 s, ~97% of load) is acceptable even at 2× the library, so a
cross-load scan cache would buy the stale-ACTUAL window for time that isn't felt — every load keeps scanning
fresh ("the grid can never show stale ACTUAL" stays unconditional). The `ScanLibraryAsync`/`ResolveAsync`
seam stays (it serves the in-load write-back re-resolve). If scan time ever hurts, reach for per-target
`ScanUnitsAsync` rescans or LCM's persistent catalog — not a session scan cache.
**~~Next (2026-07-09)~~ DONE 2026-07-23:** the whole queue completed — user's BIRDWATCHER hand-fix pass
(Swan stray plan deleted · Rosette settled · **Dumbell twin consolidated**; FishHead renamed earlier), clean
re-run (dups=0, zero held cells, **ambiguity report: 0 action items** — tsm.log 21:07), task 4.2 ticked,
`ts-ambiguity-report` ARCHIVED (main spec seeded at `openspec/specs/ts-ambiguity-report`), and
`remove-alias-fold` shipped + verified + ARCHIVED the same day (SHIPPED block below). **Zero open changes.**
Next lanes: strategic = the ISP transition (lift/regenerate + intent store); deferred ROADMAP flags
#7/18/20/21 (post-BIRDWATCHER refresh).

**▶ DECIDED 2026-07-24 — disk-matcher design lane CANCELLED.** The lane (a phrase from the 2026-07-08
resolver-rejection entry, never defined further) assumed TSM would bridge TS ↔ ISP's `Catalog.db`; under
the corrected model TSM manages TS, period — ISP is its own project, and merging TS's targets into it is
a future, separate effort. No live deficiency exists (matching is validated, ambiguity report zero), the
join's semantics already live in the shared `Astronomy.Catalog` (available to any future consumer with
that effort's real requirements in hand — the "don't design for IS until IS has real needs" guardrail),
and the orphaned sub-items were never TSM's: disk-dir promotion + the TS→store lift belong to the future
LCM/ISP efforts (recorded in `docs/2026-07-08-resolver-rejection-isp-lane.md`, decisions 5 + 7); the rig
key remains extend-when-it-lands.

## Recently shipped (digest — full history in `CHANGELOG.md`)

- **2026-07-24** — view-model partial split: `MainViewModel` → core / `.Sync` / `.Edits` / `.Reports`
  partials, members verbatim (review M4; plain commit — no requirement delta to spec).
- **2026-07-24** — review polish: journal durability doc honesty (M2), badge count cached off the UI
  thread (N2), clamp/router/format dedups + `FireAndLog` + naming (`review-polish`; remaining N-items).
- **2026-07-24** — serial commits: `CommitChain` serializes flyout + inline-Desired commits in
  confirmation order — kills the double-commit spurious-revert race (`serial-commits`; review cross-check).
- **2026-07-24** — row parameter objects: `ReconciliationRow` 29 ctor params → `RowIdentity` +
  `RowNumbers` records, identity built once per emit (`row-param-objects`; review M3).
- **2026-07-24** — push decomposition: `TsSync.Push` → orchestrator + named legs (`PushReplayState`,
  probe/write-back/field/commit), abort cascade spec-pinned (`push-decomposition`; review M1).
- **2026-07-24** — push-rule dedup: one `CountEntry` rule for review+replay, one `BaselineMatches` for
  skip-rule+staleness warning (`push-rule-dedup`; review M6).
- **2026-07-24** — truthful outcomes: closing-pull fault contained (push reports success honestly), Discard
  is pull-first (a cancelled discard-pull keeps dirty state intact) (`truthful-outcome`; review cross-check).
- **2026-07-24** — busy exclusion: load/push/visible-tonight mutually exclusive, row edits refused + surfaces
  disabled while busy, visible-tonight batches on one editor session (`busy-gate`; review C1+M5 + grid gating).
- **2026-07-23** — alias fold removed in full: a multi-claim is always a flagged duplicate (`remove-alias-fold`).
- **2026-07-23** — Visible-Tonight toolbar group: Duration + Horizon up-downs + Find (`enable-visible-tonight`).
- **2026-07-23** — pull hardening: atomic tmp+swap pull, torn-local heal, % + cancel (`harden-ts-pull`).
- **2026-07-08** — printable ambiguity report (`ts-ambiguity-report`); resolver rejected → hygiene by hand, ISP lane opened.
- **2026-07-06** — TS sync model: pull → edit local → push-as-replay; edit-flyout Parts 1–4 (project/template/cadence).

→ everything before this, back to 2026-06-08, lives in **`CHANGELOG.md`** (newest first).

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
- **TCM headless host** (`Program.cs`): `tcm [--catalog --library --ts --tolerance]` ran the build + printed the
  reconciliation report and goal-vs-actual summary (CLI removed 2026-06-11 — app-only since; see Status).
- Catalog + NINA library suites pass (145 Catalog / 33 NINA tests as of 2026-07-23).

## Phase 3 — TCM app: TS Editor (WinUI 3)  ◀ IN PROGRESS (planned 2026-06-10; M1 ✅ · M2 ✅ editing surface shipped, load-split retired 2026-07-08 · M3 pending — see Status above)

**Purpose.** TS remains the daily scheduler until IS exists; TCM is the bridge: view + edit TS's database with
disk-ACTUAL beside every number. A pragmatic editor, **not** a TS Database Manager replacement. The TS *data
layer* is the disposable stop-gap; the **UI shell is permanent** — it retargets `Catalog.db` plans when IS
arrives. Supersedes the old "migrate XFM's scheduler tab" framing: the grid replaces that tab (XFM's is deleted
at Phase 5 cutover).

- **DB touched:** the **local TS working copy only** — since 2026-07-06 synced by the pull/push model
  (`TsSync`; see the Status digest): pull at open, journaled edits, reviewed push-as-replay to BIRDWATCHER.
- **Structure:** new app project (WinUI 3; born **`TargetCatalogManager.App`**, renamed
  `TargetSchedulerManager.App` 2026-06-11) beside the then-extant `tcm` CLI (removed 2026-06-11). Edit layer **`TargetSchedulerEditor`** in `Astronomy.Catalog/TargetScheduler/` next to
  Reader/Writer — tests live in the library; same cleanly-deletable contract; no consumer terminology.
- **UI shape — grid-first:** home screen is a flat filterable **(target, filter, purpose, seconds)** reconciliation grid —
  plan vs DISK vs Δ; disk columns from a **fresh scan on load** (~2 s, the same self-contained path `writeback`
  uses; `Catalog.db` isn't needed for the editor screen). Tree (Profile ▸ Project ▸ Target) is secondary nav.
  Mosaics appear **per panel** (TS granularity) + a rollup row. In-grid editing for Tier 1; a **row-anchored
  edit flyout** for Tiers 2–3 (the docked dossier panel was built then dropped, 2026-07-06); **no Tier-4
  structural verbs** (resolver rejected 2026-07-08 — see below). **`acquired`/`accepted` are read-only** —
  Phase 4 write-back owns those columns.
- **Edit tiers (scope narrowed 2026-07-08 — see DECIDED: resolver rejected, in Status):** **T1** counts &
  toggles (`desired`, plan `enabled`, target `active`, target
  priority) · **T2** identity & pointing (`name`, RA/Dec, epoch, rotation, ROI) · **T3** project knobs (state,
  priority, description, altitude/horizon/meridian, filterSwitchFrequency, ditherEvery, smartExposureOrder,
  enableGrader, flatsHandling — `ruleWeights` dropped: a separate one-to-many table, not a scalar knob) ·
  **T4** structural (add/delete target & plan, template swap, move between projects) — **superseded
  2026-07-08:** structural fixes are hand-edits in NINA's TS UI on BIRDWATCHER; TSM ships no structural
  verbs. **Shipped surface is value-only:** of T2, only `rotation` is editable (guarded); `name`/RA/Dec/`epoch`
  are deliberately excluded, so no edit re-opens the name-matcher. Profiles render read-only (templates gained a
  full edit surface 2026-07-06 — Part 3).
- **Differences are first-class:** all three sources shown, a match-status **badge column + filter bar**, and a
  dated **Ambiguities…** report (disk-only, TS-only near-misses, name-mismatch / ambiguous, duplicates) with the
  hand fix per row. **No structural resolution verbs** (create / rename / delete / adopt were all rejected with
  the resolver, 2026-07-08 — TSM must not encode planning decisions it doesn't own): every fix is a hand-edit in
  NINA's TS UI on BIRDWATCHER, and `desired` / membership stay user-owned intent. Rationale:
  `docs/2026-07-08-resolver-rejection-isp-lane.md`.
- **Milestones:** **M1 view** ✅ (2026-06-10) — read-only grid + badges + filters → **M2 edit** ✅ (2026-07-06) —
  Tier-1 in-grid + the T2–T3 edit flyout + the sync model (pull → edit-local → push-as-replay) + automatic
  write-back → ~~**M3 resolve**~~ **retired** — the structural-resolution milestone was dropped with the resolver
  rejection (2026-07-08); ambiguities are surfaced (report + badges) and hand-fixed in NINA. Remaining forward
  work is the ISP transition, not more TS-editing surface.
- **Hazards (resolved):** cadence-breaking edits (filterSwitchFrequency, ditherEvery, plan `enabled`) now clear
  the invalidated `filtercadenceitem` rows in the same transaction as the write (lifted from TS's
  `SchedulerDatabaseContext`; mechanism in `ARCHITECTURE.md` → *TS write-back* + `DOMAIN.md` → *Editing*). The
  name-round-trip hazard is moot — `name`/RA/Dec/`epoch` are excluded from the editable surface, so no name edit
  exists to re-validate.

## Phase 4 — Write back to TS  ✅ DONE (built 2026-06-08)

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS/ISP reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Built 2026-06-08 (grill-me design + real-data validation, 58 library
tests). Verb was `tcm writeback [--apply]` (dry-run default; CLI removed 2026-06-11 — the engine now runs as
the app's automatic write-back + push replay, see `ARCHITECTURE.md` → TS write-back).

The full write-back contract — cached-columns-only writes (`acquired`=`accepted`=disk, `desired` ratchets up
only), disk-wins one-way conflict, the `(target, filter, purpose, seconds)` join, manual-bucket gating, the
open-sidecar / read-only / private-cache guards, and the surgical single-target (`--target "<dir>"`, per-panel
for mosaics) path — is the load-bearing spec in **`ARCHITECTURE.md` → TS write-back** (single-sourced there;
don't re-document the mechanism here). The 2026-06-08 real-data validation run (182 written / 13 held / 92
ignored-missing, the `Sh2-142` fix, the `Mosaic - Cygnus Loop` surgical run) is recorded in `CHANGELOG.md`.
- ~~**Out of scope (later phase):** automated network push of the local copy back to the imaging PC~~ —
  **shipped 2026-07-06** as the sync model's push-as-replay (never a file copy; see the Status digest).
  ~~Creating missing targets~~ — **not a TSM verb** (resolver rejected 2026-07-08); target creation is a
  hand-edit in NINA's TS UI.

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TSM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).

> **Plan supersession:** this replaces the earlier "IS owns `scheduler.db`" plan — `Catalog.db` is the hub and
> IS becomes a consumer.
