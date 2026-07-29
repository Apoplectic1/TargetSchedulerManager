# TargetSchedulerManager (TSM) — Roadmap

**Charter:** the forward-looking plan + current status. Read it for *what's next / where things stand*. The
shipped history (every unit, newest-first) lives in `CHANGELOG.md` (git is the commit-level backstop).

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

> **Naming:** this project was **TargetCatalogManager (TCM)** until 2026-06-11. Dated entries below the rename
> keep the names they shipped under (TCM, `tcm`, `tcmui`, `tcm.log`, `TCM_DIAG`) — they match the git history.

## Status — pick up here (2026-07-27)

TSM is the WinUI **TS-database manager**, app-only (CLI removed 2026-06-11): a reconciliation grid of TS plan vs
disk-ACTUAL — fresh in-memory scan each load (no `Catalog.db`), per-(target, filter, purpose, seconds) plane rows,
over the **local TS working copy** under the pull → edit-local → push-as-replay **sync model** (shipped
2026-07-06: baseline-skipped pull at open, journaled local edits, reviewed Push to BIRDWATCHER, automatic
write-back each load — user-verified against the rig same day). Editing shipped: target enable checkbox
+ in-grid `desired` + the **edit flyout** (hover glyph / right-click on target + filter rows → schema-generated
form: target `priority`/`rotation`, plan `exposure`; per-field guarded commit). Each load reconciles disk targets
against TS targets into Both / Planned-only / Actual-only, with mosaics resolved per panel; live counts show in
the app + `tsm.log` (not pinned here — they move with every edit and every imaging night). Match tolerance
**0.5°** (validated 2026-06-04).

**Where things stand:** the editing surface (Parts 1–4), the sync model, pull hardening, the Visible-Tonight
toolbar group, the alias-fold removal, the full 2026-07-24 code-review campaign, and the presentation-readiness
lane (P1–P5) have all shipped and archived. 2026-07-26 added the sync-mark trilogy (template-change marks ·
per-field flyout marks · net-no-op pruning), two-tier badge color, the `UpDownBox` Floor knob, a phase-scoped
toolbar **Cancel** covering the whole load, and `CONVENTIONS.md` as a fourth reference doc. 2026-07-27 added the
two reconciliation-fidelity units — **`capture-config-keys`** (gain/offset/binning key the disk plane, camera a
label, the pairing rule made explicit) and **`skip-comet-targets`** (non-sidereal targets never enter the scan);
both shipped, verified and archived. 2026-07-29 closed the rotation + RA/DEC deferral as one unit —
**`rotation-framing-key`** (framing = fold-180 sky rotation + cluster centroid as a reconciliation key, the
`Rot` column, the `framing` badge; implemented, awaiting in-app verification). The load-split is
**retired** (2026-07-08 — the ~2 s fresh scan is acceptable even at 2× the library, so a cross-load scan cache
would buy the stale-ACTUAL window for time that isn't felt; every load keeps scanning fresh, so the grid can
never show stale ACTUAL). The next lane is
strategic — the **ISP transition** (intent store + lift/regenerate), which is *not* TSM work. Shipped history
and the 2026-07-24 decision records (docs-audit flags resolved; disk-matcher lane cancelled) live in
**`CHANGELOG.md`**.

### Deferred — the reconciliation dimensions `capture-config-keys` left on the table

`capture-config-keys` shipped and archived 2026-07-27 (see `CHANGELOG.md`): gain, offset and binning became
**reconciliation keys** and camera a **disk-side label**, with the pairing rule made explicit — a row is `Both`
only when the plan and the frames agree on every dimension both planes express. Three dimensions were deferred
deliberately; two closed 2026-07-29, one remains:

- ~~**Rotation as a key** / **RA/DEC refinements**~~ — **closed 2026-07-29 as ONE unit** (openspec
  `rotation-framing-key`): the two were one concept — a **framing** is a (field-center, sky-rotation) pair —
  and the 2026-07-29 measurement spike (18,650 frames) dissolved the open questions: real framings sit ≥ 9°
  apart with ≤ 0.2° jitter (any tolerance in 1–5° yields identical clusters — it stopped being a judgement
  call; the old "M33/M51 on the 5° boundary" note didn't reproduce under fold-180: they measure 0.56°/0.10°),
  every true flip's centroids coincide within 0.12° (fold-180 + centroid guard), and mech→sky conversion is
  unreliable exactly where it would matter (zero point drifts 19–35° across remounts) so it is never
  attempted. **Explicit follow-up left on the table: the overlap-% column** (footprint intersection of a
  stray framing vs the plan framing, the number that prices the hazard) — needs image pixel dimensions
  `XisfHeader` doesn't expose; lands later as a column addition without rework.
- **Telescope as its own UI section.** 100% uniform today (`APM107R@531` on all frames), and a second
  scope would likely bring a disk directory-layout change, so it should be designed *with* that layout rather
  than guessed at now.
**Comets — closed the same day, not deferred.** ~~Out of scope~~ **excluded at the scan** (2026-07-27, openspec
`skip-comet-targets`, spec `image-library-scan`). A `Comet …` target directory is never walked, like
`Captures/Calibration`: comets are non-sidereal, so no sidereal plan can describe them (the TS db holds zero comet
targets) and every frame is acquired by hand. Their capture trees also nest date-named session folders where
filter directories belong, so scanning them published filter codes like `2024-10-18 - Track Comet`. Removes one
target and 254 of 18,904 frames.

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
- Catalog + NINA library suites pass (live counts move with every change — see the suites, not this line).

## Phase 3 — TSM app: TS Editor (WinUI 3)  ✅ DONE (M1 view ✅ 2026-06-10 · M2 edit ✅ 2026-07-06 · M3 resolve retired 2026-07-08; remaining work is the out-of-phase ISP transition)

**Purpose.** TS remains the daily scheduler until IS exists; TSM is the bridge: view + edit TS's database with
disk-ACTUAL beside every number. A pragmatic editor, **not** a TS Database Manager replacement. The TS *data
layer* is the disposable stop-gap; the **UI shell is permanent** — it retargets `Catalog.db` plans when IS
arrives. Supersedes the old "migrate XFM's scheduler tab" framing: the grid replaces that role — XFM removed its
own TS/scheduler surface 2026-07-07 (v1.9.0, TS-free), so there is no tab left to cut over.

- **DB touched:** the **local TS working copy only** — since 2026-07-06 synced by the pull/push model
  (`TsSync`; see Status): pull at open, journaled edits, reviewed push-as-replay to BIRDWATCHER.
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
  `SchedulerDatabaseContext`; mechanism in `SUBSYSTEMS.md` → *TS write-back* + `DOMAIN.md` → *Editing*). The
  name-round-trip hazard is moot — `name`/RA/Dec/`epoch` are excluded from the editable surface, so no name edit
  exists to re-validate.

## Phase 4 — Write back to TS  ✅ DONE (built 2026-06-08)

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS/ISP reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Built 2026-06-08 (grill-me design + real-data validation, 58 library
tests). Verb was `tcm writeback [--apply]` (dry-run default; CLI removed 2026-06-11 — the engine now runs as
the app's automatic write-back + push replay, see `SUBSYSTEMS.md` → TS write-back).

The full write-back contract — cached-columns-only writes (`acquired`=`accepted`=disk, `desired` ratchets up
only), disk-wins one-way conflict, the `(target, filter, purpose, seconds)` join, manual-bucket gating, the
open-sidecar / read-only / private-cache guards, and the surgical single-target (`--target "<dir>"`, per-panel
for mosaics) path — is the load-bearing spec in **`SUBSYSTEMS.md` → TS write-back** (single-sourced there;
don't re-document the mechanism here). The 2026-06-08 real-data validation run (182 written / 13 held / 92
ignored-missing, the `Sh2-142` fix, the `Mosaic - Cygnus Loop` surgical run) is recorded in `CHANGELOG.md`.
- ~~**Out of scope (later phase):** automated network push of the local copy back to the imaging PC~~ —
  **shipped 2026-07-06** as the sync model's push-as-replay (never a file copy; see Status).
  ~~Creating missing targets~~ — **not a TSM verb** (resolver rejected 2026-07-08); target creation is a
  hand-edit in NINA's TS UI.

## Phase 5 — Consumer cutover

Consumer cutover is **not TSM work** — the authored intent store and its consumers belong to **LCM / ISP**
(parent `CLAUDE.md` → *Data-flow hubs*; rationale in `docs/2026-07-08-resolver-rejection-isp-lane.md`
decisions 4–5). The phase's original premise — *`Catalog.db` is the derived hub and IS is its consumer* — was
**inverted 2026-07-08** (an *authored* store holds intent; TS becomes the disposable projection) and the
bridging lane was cancelled 2026-07-24. That was the **second** reversal: the phase first read *"IS owns
`scheduler.db`"* before `Catalog.db`-as-hub replaced it. What survives:

- **XFM is ruled out** as a consumer (went TS-free 2026-07-07, v1.9.0 — it never consumes `scheduler.db` or
  `Catalog.db`, and it already removed its own scheduler surface, so there is no XFM tab to cut over).
- ~~Reconcile the IS design docs~~ — **moot 2026-07-08**: the docs it would reconcile describe the
  pre-inversion premise; that reconciliation belongs to LCM/ISP.
