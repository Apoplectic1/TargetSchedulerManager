# TargetSchedulerManager (TSM) — Roadmap

**Charter:** the forward-looking plan + current status. Read it for *what's next / where things stand*. The
shipped history (every unit, newest-first) lives in `CHANGELOG.md` (git is the commit-level backstop).

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

## Status — pick up here (2026-08-06)

TSM is the WinUI **TS-database manager**, app-only (CLI removed 2026-06-11): a reconciliation grid of TS plan vs
disk-ACTUAL — fresh in-memory scan each load (no `Catalog.db`), rows keyed by the **capture-configuration cell**
(target, filter, purpose, exposure, gain, offset, binning, framing — `ARCHITECTURE.md` → *Key facts*), over the
**local TS working copy** under the pull → edit-local → push-as-replay **sync model** (baseline-skipped pull at
open, journaled local edits, reviewed Push to BIRDWATCHER, automatic write-back each load). Editing: target
enable checkbox + in-grid `desired` + the **edit dialog** (hover glyph / right-click on target + filter rows →
schema-generated form, per-field guarded commit; every form-hosting surface is a centered, movable
`ContentDialog` — menus stay flyouts), plus **one structural verb** — adoption of disk-only cells into TS via
the assignment dialog, at two grains: per-cell, or whole-rollup ("Add to TS…" on the target row → one combined
dialog, one atomic insert batch; specs `disk-row-adoption` + `target-and-plan-flyouts`). Each load reconciles disk targets against TS targets into Both / Planned-only /
Actual-only, with mosaics resolved per panel; live counts show in the app + `tsm.log` (not pinned here — they
move with every edit and every imaging night). Match tolerance **0.5°** (validated 2026-06-04).

**Where things stand:** everything through 2026-08-06 (`project-scoped-tonight` — the toolbar as a
read/write window onto a project's TS constraints, Set writes + scoped enable pass, name tracks the
"- N" altitude clause — plus `dialog-behaviors-on-type` and `filter-rank-row-order`) has shipped, been
field-verified, and archived — 16 capability specs
under `openspec/specs/`; current release **TSM v1.4.0 on AL v1.4.0**; distribution is live on GitHub Releases as a self-updating Velopack installer (openspec
change `velopack-self-update`, formal contract → spec `self-update`; the current version is the latest tag —
rules in `RELEASING.md`). The load-split idea stays **retired** (2026-07-08: the ~2 s fresh scan is acceptable,
so the grid can never show stale ACTUAL). The dated unit-by-unit history — every SHIPPED/DECIDED entry this
section used to narrate — lives in **`CHANGELOG.md`**; this section deliberately doesn't repeat it. The next
lane is strategic — the **IS transition** (intent store + lift/regenerate), which is *not* TSM work.

### Clock-seam migration — CLOSED 2026-08-11 (same day, two passes)

AL's `IClock` is the portfolio's single clock source (AL `CONSUMERS.md` clock convention).
`MainViewModel` adopted it first (settable `Clock` property, all four `Reports.cs` reads); the
service-layer residue followed the same day — `TsJournal` / `TsSync` grew an optional
`IClock? clock = null` ctor parameter (sync threads its clock down into the journal it owns),
`ReconciliationLoader.ResolveAsync` an optional trailing parameter. **TSM now has zero ambient
clock reads** (grep-verified); provenance timestamps (journal entries, sync baselines, scan
stamp) are all seam-routed — which ISM inherits when it copies the sync/journal shapes.

### Doc-system open items

**All closed 2026-08-03.** Both 2026-07-29 maintain-sweep held graduates landed: the `DOMAIN.md` split
(→ `UI.md`, carrying held graduate H2 — the deliberate `PlanSeconds == 0` em-dash conflation — in its
*em-dash convention*), and H1 — the `Astronomy.Diagnostics` ≠ `Astronomy.Catalog` boundary rationale —
placed by user decision in **Library `ARCHITECTURE.md` → § Astronomy.Diagnostics**. Rails in
`docs/2026-07-29-maintain-report.md`; the 2026-08-03 audit's one report-only flag is also resolved
(`docs/2026-08-03-audit-report.md`). Nothing open.

### Queued — project-name clause parses back into min altitude (user, 2026-08-12)

Make the name↔altitude coupling bidirectional on the surface where the "- N" clause convention
lives: an edit of the **project Name field** (edit dialog) whose committed text carries a trailing
altitude clause (`" - #"`; legacy `" - Above N"` recognized, same parse the Set press and the mosaic
name-match use) SHALL also write `Project.minimumaltitude` with the parsed value, **range-checked**
to the schema bounds (0–89.9 — TS's HorizonDefinition asserts < 90). A name without the clause is a
name-only edit — **no altitude write**. Today only the inverse exists (Visible-Tonight **Set**
writes `minimumaltitude` and rewrites the clause), so a hand-edited "… - 45" name leaves the
altitude stale — exactly the inconsistency this closes. Surface decision (user, this date): the
project Name field only — the target-rename verb stays clause-free (targets don't carry the
convention).

### Deferred — the reconciliation dimensions `capture-config-keys` left on the table

Of the dimensions deferred when gain/offset/binning became reconciliation keys (2026-07-27), one remains open;
the closures (rotation + RA/DEC as the framing key, the overlap price, the comet scan exclusion) are recorded
in `CHANGELOG.md` and pinned in `ARCHITECTURE.md` → *Key facts*.

- **Telescope as its own UI section.** 100% uniform today (`APM107R@531` on all frames), and a second
  scope would likely bring a disk directory-layout change, so it should be designed *with* that layout rather
  than guessed at now.
- **ASTAP-assisted sky angle for mechanical-only targets** (user, obs 7c5e 2026-08-04) —
  **SUPERSEDED 2026-08-07 by the flag-only decision below: TSM builds no solver integration and no
  mechanical→sky machinery.** *(Original sketch kept for the record: plate-solve a representative
  frame of the serving framing cluster to seed the true sky angle, with design questions on ASTAP
  detection, frame choice, offer-vs-auto-seed, and timing.)*
- **°(M)/mechanical rotation is a flag, not data (user, 2026-08-07)** — mechanical-only rotation is
  detected (the existing ambiguity report already surfaces it — also the verification tool for the
  next point) and shown as a simple flag whose remedy is always external: **run XFM** (checked
  browse solves + stamps `OBJCTROT`) → rescan → flag clears. Background: the user manually removed
  the °(M) backlog from the image library on 2026-08-07, and XFM's solve-on-browse should prevent
  recurrence from their own captures — the flag guards the residual cases (XFM not yet run, other
  capture programs, third-party images). The solved-rotation read constraint (previous bullet)
  is unchanged: framings consume solved angles only, through AL's `WcsOrientation`.
- **Constraint on any solved-rotation consumption (2026-08-07):** when TSM starts reading plate-solved sky
  angles for framings — the XFM-stamped `OBJCTROT` backlog rescan, or the ASTAP-assisted seed above — read
  orientation **through AL's `WcsOrientation`** (`PositionAngleDegrees` true 0–360 / `FramingAngleDegrees`
  folded [0,180), added on demand), never raw `OBJCTROT`: the PA ≡ PA+180 framing fold is a named property
  in AL, not hand math in TSM. Decided with XFM ROADMAP #9 (full rationale) / AL ROADMAP (the queued property).

## Phase 1 — Foundation (shared schema library) ✅ DONE

Delivered `Astronomy.Catalog` (schema + `SchemaManager` + store + hardened `TargetSchedulerReader`).
How it works → `ARCHITECTURE.md` → *Components*; shipped detail → `CHANGELOG.md`.

## Phase 2 — Scanner → inventory + reconciliation ✅ DONE

Delivered the scanner, canonical-target resolution, and the goal-vs-actual reconcile layer (plus the
since-removed CLI host). How it works → `ARCHITECTURE.md` → *Components*; shipped detail → `CHANGELOG.md`.

## Phase 3 — TSM app: TS Editor (WinUI 3)  ✅ DONE (M1 view ✅ 2026-06-10 · M2 edit ✅ 2026-07-06 · M3 resolve retired 2026-07-08; remaining work is the out-of-phase IS transition)

**Purpose.** TS remains the daily scheduler until IS exists; TSM is the bridge: view + edit TS's database with
disk-ACTUAL beside every number. A pragmatic editor, **not** a TS Database Manager replacement. The TS *data
layer* is the disposable stop-gap; the **UI shell is permanent** — it retargets `Catalog.db` plans when IS
arrives. Supersedes the old "migrate XFM's scheduler tab" framing: the grid replaces that role — XFM removed its
own TS/scheduler surface 2026-07-07 (v1.9.0, TS-free), so there is no tab left to cut over.

- **DB touched:** the **local TS working copy only** — since 2026-07-06 synced by the pull/push model
  (`TsSync`; see Status): pull at open, journaled edits, reviewed push-as-replay to BIRDWATCHER.
- **Structure:** new app project (WinUI 3; **`TargetSchedulerManager.App`**, carrying its current name
  since 2026-06-11) beside the then-extant CLI (removed 2026-06-11). Edit layer **`TargetSchedulerEditor`** in `Astronomy.Catalog/TargetScheduler/` next to
  Reader/Writer — tests live in the library; same cleanly-deletable contract; no consumer terminology.
- **UI shape — grid-first:** home screen is a flat filterable reconciliation grid keyed by the
  **capture-configuration cell** (`ARCHITECTURE.md` → *Key facts*) —
  plan vs DISK vs Δ; disk columns from a **fresh scan on load** (~2 s, the same self-contained path `writeback`
  uses; `Catalog.db` isn't needed for the editor screen). Tree (Profile ▸ Project ▸ Target) is secondary nav.
  Mosaics appear **per panel** (TS granularity) + a rollup row. In-grid editing for Tier 1; a centered movable
  **edit dialog** for Tiers 2–3 (the docked dossier panel was built then dropped, 2026-07-06); of Tier-4,
  only the **adoption add** ships (2026-08-03 — see below); the rest were rejected with the resolver
  (2026-07-08). **`acquired`/`accepted` are read-only** —
  Phase 4 write-back owns those columns.
- **Edit tiers (scope narrowed 2026-07-08 — see DECIDED: resolver rejected, in Status):** **T1** counts &
  toggles (`desired`, plan `enabled`, target `active`, target
  priority) · **T2** identity & pointing (`name`, RA/Dec, epoch, rotation, ROI) · **T3** project knobs (state,
  priority, altitude/horizon/meridian, filterSwitchFrequency, ditherEvery, smartExposureOrder,
  enableGrader, flatsHandling — `ruleWeights` dropped: a separate one-to-many table, not a scalar knob;
  `description` never shipped, `TsEditableSchema` is the source of truth) ·
  **T4** structural (add/delete target & plan, template swap, move between projects) — **superseded
  2026-07-08, amended 2026-08-03:** TSM ships exactly one structural verb, the per-row **adoption add**
  (target + plan via the assignment dialog, spec `disk-row-adoption`); delete / duplicate / template
  creation / move-between-projects remain hand-edits in NINA's TS UI on BIRDWATCHER. **Shipped surface is value-only:** of T2, only `rotation` is editable (guarded); `name`/RA/Dec/`epoch`
  are deliberately excluded, so no edit re-opens the name-matcher. Profiles render read-only (templates gained a
  full edit surface 2026-07-06 — Part 3).
- **Differences are first-class:** all three sources shown, a match-status **badge column + filter bar**, and a
  dated **Ambiguities…** report (disk-only, TS-only near-misses, name-mismatch / ambiguous, duplicates) with the
  hand fix per row. **No structural *resolution* verbs** (the 2026-07-08 resolver rejection — TSM must not
  encode planning decisions it doesn't own): rename / delete / re-parent fixes are hand-edits in
  NINA's TS UI on BIRDWATCHER, and `desired` / membership stay user-owned intent. The one exception is the
  explicit per-row **adoption add** (2026-08-03) — a user gesture, not a resolver. Rationale:
  `docs/2026-07-08-resolver-rejection-is-lane.md`.
- **Milestones:** **M1 view** ✅ (2026-06-10) — read-only grid + badges + filters → **M2 edit** ✅ (2026-07-06) —
  Tier-1 in-grid + the T2–T3 edit dialog + the sync model (pull → edit-local → push-as-replay) + automatic
  write-back → ~~**M3 resolve**~~ **retired** — the structural-resolution milestone was dropped with the resolver
  rejection (2026-07-08); ambiguities are surfaced (report + badges) and hand-fixed in NINA. Remaining forward
  work is the IS transition, not more TS-editing surface.
- **Hazards (resolved):** cadence-breaking edits (filterSwitchFrequency, ditherEvery, plan `enabled`) now clear
  the invalidated `filtercadenceitem` rows in the same transaction as the write (lifted from TS's
  `SchedulerDatabaseContext`; mechanism in `SUBSYSTEMS.md` → *TS write-back* + `UI.md` → *Editing*). The
  name-round-trip hazard is moot — `name`/RA/Dec/`epoch` are excluded from the editable surface, so no name edit
  exists to re-validate.

## Phase 4 — Write back to TS  ✅ DONE (built 2026-06-08)

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Built 2026-06-08 (grill-me design + real-data validation, 58 library
tests). Verb was `tsm writeback [--apply]` (dry-run default; CLI removed 2026-06-11 — the engine now runs as
the app's automatic write-back + push replay, see `SUBSYSTEMS.md` → TS write-back).

The full write-back contract — cached-columns-only writes (`acquired`=`accepted`=disk, `desired` ratchets up
only), disk-wins one-way conflict, the `(target, filter, purpose, seconds)` join, manual-bucket gating, the
open-sidecar / read-only / private-cache guards, and the surgical single-target (`--target "<dir>"`, per-panel
for mosaics) path — is the load-bearing spec in **`SUBSYSTEMS.md` → TS write-back** (single-sourced there;
don't re-document the mechanism here). The 2026-06-08 real-data validation run (182 written / 13 held / 92
ignored-missing, the `Sh2-142` fix, the `Mosaic - Cygnus Loop` surgical run) is recorded in `CHANGELOG.md`.
- ~~**Out of scope (later phase):** automated network push of the local copy back to the imaging PC~~ —
  **shipped 2026-07-06** as the sync model's push-as-replay (never a file copy; see Status).
  ~~Creating missing targets~~ — **shipped 2026-08-03** as the adoption verb (target created from the
  plate-solved disk centroid via the assignment dialog, replayed at push; spec `disk-row-adoption`). All
  other structural change stays a hand-edit in NINA's TS UI (resolver rejected 2026-07-08).

## Phase 5 — Consumer cutover

Consumer cutover is **not TSM work** — the authored intent store and its consumers belong to **ISM / IS**
(parent `CLAUDE.md` → *Data-flow hubs*; rationale in `docs/2026-07-08-resolver-rejection-is-lane.md`
decisions 4–5). The phase's original premise — *`Catalog.db` is the derived hub and IS is its consumer* — was
**inverted 2026-07-08** (an *authored* store holds intent; TS becomes the disposable projection) and the
bridging lane was cancelled 2026-07-24. That was the **second** reversal: the phase first read *"IS owns
`scheduler.db`"* before `Catalog.db`-as-hub replaced it. What survives:

- *(Cross-repo program view for the TS-replacement effort: umbrella `..\ROADMAP.md`
  § TS-replacement program.)*
- **SHIPPED 2026-08-12 — the Catalog.db export duty (TSM's one ISM-era work item).** The who-plans
  decision (`..\IntervalScheduler\docs\2026-08-12-who-plans-decision.md`) settled the architecture:
  ISM plans from `Catalog.db`; NINA executes; no db on BIRDWATCHER. TSM's part, now live (openspec
  change `add-catalog-export-duty`; spec `openspec/specs/catalog-export/`): every committed push
  also projects the applied user-authored entries into ISM's JSONL inbox as contract-v1 upserts —
  push-time-only emission ("authored intent as committed to TS"), write-back origin excluded
  (acquired/accepted AND the desired ratchet), template mirror riding every plan upsert, atomic
  `.partial`→`.jsonl` publish, rule-#16 loud failure after the committed push. TSM never opens
  Catalog.db; writer-side tests are file-level contract fixtures; *ingest-side* end-to-end
  verification waits for ISM's app — don't block on it. Mechanism detail: `SUBSYSTEMS.md` → *TS
  sync model*. Dies with TSM at TS retirement.
  **Feed v2 requirement (user directive 2026-08-12): project settings must travel.** Field-hit
  same day: a `Project.minimumaltitude 0→30` push reached TS but not Catalog.db — v1
  `project-upsert` carries only name/state/priority, so ISM's planner reads import-seeded
  settings (the D9 acceptance in ISM's `add-ism-scaffold-ingest` design, now promoted to a
  requirement). Close = bilateral contract v-bump widening `project-upsert` with the settings
  block; both emitters move together (candidates list: ISM
  `docs\design\catalog-inbox-contract.md` § V2 candidates).
  **Change SEEDED 2026-08-13: `add-inbox-v2-emission`** (paired ISM `add-inbox-contract-v2`;
  neither ships alone): v2 envelope, `project-upsert` gains the settings block + `is_mosaic`,
  the template mirror gains the moon-relax triplet, **and observed emission extends to project
  rows** (user decision 2026-08-13 — the design gate met by `add-target-rename`), so
  BIRDWATCHER-side settings edits flow to Catalog.db too.
  **Related lane SHIPPED + FIELD-VERIFIED + ARCHIVED 2026-08-12: `add-target-rename`** — the rename
  verb (target `name` as a Guarded schema field; journal → push → `target-upsert`, no contract change)
  **plus pull-diff emission adopted, targets only**: every pull now emits full-value `target-upsert`s
  for externally-authored target changes it observes arriving (existing rows only; remotely-added
  targets and inbound project/plan/template changes stay silent — the project half is exactly the
  feed-v2 gap above). The pending BIRDWATCHER `Cygnus Loop P9` rename auto-flushes at the next
  session's open pull — no manual touch needed.
- **XFM is ruled out** as a consumer (went TS-free 2026-07-07, v1.9.0 — it never consumes `scheduler.db` or
  `Catalog.db`, and it already removed its own scheduler surface, so there is no XFM tab to cut over).
- ~~Reconcile the IS design docs~~ — **moot 2026-07-08**: the docs it would reconcile describe the
  pre-inversion premise; that reconciliation belongs to ISM/IS.
