# MAINTAIN sweep — 2026-08-16 (graduate & prune)

**Status:** complete. Fourth TSM maintain sweep (prior: 2026-07-26 morning + evening, 2026-07-29). Window
swept: everything new since 2026-07-29 — the 13 openspec changes archived 2026-08-02 → 2026-08-13, both new
`NOTEBOOK.md` entries, ~30 `CHANGELOG.md` entries, and the 2026-08-03 audit report (archived by this sweep to
`docs/archive/2026-08-03-audit-report.md`) — plus a re-judging
pass over the older live journal.

## Method

Three rounds, **13 workers**, batch density 4–6 documents each (the 2026-07-26 lesson: an overloaded
worker's "dry" is not evidence). Rounds 1–2 Sonnet 5 at high effort, round 3 **Opus 5** at high effort as the
R21 model-diversification round — a dry round at one model is that model's ceiling, not truth.

| Round | Worker | Batch | Result |
|---|---|---|---|
| 1 | design-A/B/C | the 13 archived `design.md` (2026-08-02 → 08-13), 4–5 each | 4 graduates, rest keep |
| 1 | notebook | `NOTEBOOK.md`, all 8 entries | 1 graduate, 2 archive-candidates |
| 1 | changelog-aug | `CHANGELOG.md` 2026-07-29 → 08-16 | 3 graduates |
| 1 | dated-records | the 2 live `docs/` records + the prior maintain report | 1 archive, 1 prune-only |
| 2 | docs-archive | the 5 records in `docs/archive/` | dry (1 keep) |
| 2 | ism-boundary | the 3 catalog-export/rename changes read by theme | 2 graduates |
| 2 | changelog-jul | `CHANGELOG.md` 2026-07-01 → 07-28 (re-judged) | dry |
| 2 | prune-side | ROADMAP + CLAUDE + the reports, cut-side only | 3 prune-only |
| 3 | design-sweep | all 13 archived changes again, Opus | 3 graduates |
| 3 | cross-cut | NOTEBOOK + CHANGELOG + audit report, Opus + the M9 bloat verdict | 4 graduates |
| 3 | code-contracts | M12 channel: guarantee-language claims vs live code | **2 code bugs** |

**Coverage: complete.** 13/13 workers returned, 0 errors, 0 retries. 132 items classified — **109 keep**,
15 graduate, 4 archive, 4 prune-only. The keep-heavy shape is rule #20 (shift-left graduation) working:
workers repeatedly found their candidate already sitting in a spec or reference doc with a line number.

**M9 bloat verdict (asked independently, Opus):** neither `UI.md` (49 KB) nor `SUBSYSTEMS.md` (37 KB) is
bloated in the M9 sense. Every one of UI.md's 13 sections maps to a charter clause; the only splittable
candidate (*WinUI gotchas*, 88 lines = 17%) is charter-claimed and is the *why* behind the rules above it,
with three inbound cross-refs. SUBSYSTEMS is exactly the four machines its charter names, each pairing 1:1
with an openspec spec. **No graduate was held on bloat grounds** — all healthy targets, all applied.
*Noted, not acted on:* UI.md § Editing is 26% of the file and lines 229–279 are one ~50-line bullet that has
absorbed six changes by accretion — a sub-heading job, not a split.

## Graduates applied (12)

| # | Standing claim | → target | source disposition |
|---|---|---|---|
| G1 | An in-place plan mirror addresses the **plan**, never the row instance (`MirrorPlanEdit`); any future per-plan inline edit routes through it | `UI.md` → Editing | **stub** — NOTEBOOK 2026-08-12 keeps the field failure, points up |
| G2 | The catalog-export contract is at **v2**, and **push is no longer the sole emitter** (pulls emit observed inbound target + project changes) | `ARCHITECTURE.md` → Key facts | **cross-ref** (M11: archived design records untouched) |
| G3 | Same, as a router hook | `CLAUDE.md` | cross-ref → ARCHITECTURE |
| G4 | Target `name` joins the close-time re-reconcile trigger (group identity, not a capture-config key) | `ARCHITECTURE.md` + `UI.md` | cross-ref |
| G5 | A row template's root `Background="Transparent"` is **hit-test surface**; full-row visuals go in a Border underlay | `UI.md` → WinUI gotchas | cross-ref |
| G6 | TS's **runtime never writes `project`** — every project column is authored via the Database Manager UI, which is what lets the export skip origin bookkeeping | `TS-SCHEMA.md` → Tables preamble | cross-ref |
| G7 | What an adopted target row carries in unseen columns (`epochcode` 2 · `roi` 100 · `active` 1 · `priority` −1) | `TS-SCHEMA.md` → target | cross-ref |
| G8 | TSM reads **no ambient clock** — every time value comes through an injected `IClock`, plumbed as an optional trailing parameter | `CONVENTIONS.md` (new section) | **archive** — the ROADMAP clock-seam section struck (see P1) |
| G9 | Mechanical rotation is a **flag, not data**: TSM builds no solver integration; the remedy is always external (run XFM) | `DOMAIN.md` | **archive** — ROADMAP deferred bullet condensed (P3) |
| G10 | The Ctrl+N diagnostics window is **not this repo's code** — it lives in `Astronomy.Diagnostics.WinUI`; fix it there | `CONVENTIONS.md` → Not owned here | cross-ref |
| G11 | Window title = app name + version, nothing else (no sync-status suffix; the version is the dev-vs-installed disambiguator) | `UI.md` → Chrome | cross-ref |
| G12 | A stale build directory is **structurally unshippable**: `release.ps1` derives the publish path from the csproj TFM and gates the packed exe's MinVer stamp against the tag | `RELEASING.md` → Distribution | cross-ref |

**Not graduated, deliberately** (recorded so a later sweep doesn't re-litigate): the Velopack
entry-point ordering rule (`VelopackApp.Build().Run()` first in `Main`) stays where it is — `Program.cs`
carries it as an XML doc at the enforcement point, which is what `CONVENTIONS.md` prescribes. The
`Application.Start(_ => …)` discard-shadowing trap is compile-time trivia the code already dodges by naming
the parameter `p`; not standing truth.

## Held — needs a decision, NOT applied

> **RESOLVED same day.** The user created a portfolio `..\DOMAIN.md`, which is exactly the home M15 names.
> The graduate below landed there under § *Releases* (with the container `CLAUDE.md` routing to the new
> file, one-way). Kept as the record of why it was held rather than improvised into the container router.

### M15 · portfolio-level: the AL-release payload-realignment practice

- **standing-claim:** when AL publishes a release — *including a docs-only one, and one that changes nothing
  the app consumes* — the portfolio re-cuts all three app installers (TP/TSM/XFM) the same day with zero app
  changes, purely so the embedded `Astronomy.*` DLL stamps read as the current AL. Visible three times in
  this window alone (TSM v1.4.1 → AL 1.5.0, v1.5.2 → AL 1.7.1, v1.5.4 → AL 1.8.0).
- **evidence it is standing:** it is a *practice*, not an incident — three instances in ten days, each
  recorded in CHANGELOG as its own release. The container's `CLAUDE.md` states release **ordering** (AL
  first, gate aborts on a dirty/untagged Library) but not this realignment cadence, and no app's
  `RELEASING.md` mentions it.
- **why held:** it spans TP/TSM/XFM, so no TSM doc's charter can own it, and the container (`..\`) has no
  portfolio `DOMAIN.md`. M15 forbids improvising the placement into the container router, a sibling repo's
  docs, or a new cross-repo pointer.
- **needs from the user:** a placement decision — (a) create a portfolio `DOMAIN.md`, (b) extend the
  container `CLAUDE.md` → *Cross-repo release ordering*, or (c) state it per-app in each `RELEASING.md`.
- **disposition of source:** stub — the CHANGELOG entries are untouched.

## Code bugs — report-only (M12/M13), unadjudicated

**MAINTAIN never edits code.** Both were found by the Opus contract-vs-code worker and independently
verified at the cited lines before being written here.

> **BOTH FIXED same day** (see the closing note under each).

### CB-1 — the open-time "Push" path never emits to ISM's inbox *(high)*

- **contract:** `openspec/specs/catalog-export/spec.md` — TSM "SHALL emit … at exactly one point: after a
  successful push-as-replay commit"; `add-catalog-export-duty/design.md` D1 — "TSM writes TS exactly once:
  at push … `TsSync.Push` is the single funnel."
- **violation:** there are **two** push-as-replay call sites and only one emits.
  `TargetSchedulerManager.App/ViewModels/MainViewModel.Sync.cs:246` — the `OpenDirtyDecision.Push` branch
  inside `PrepareTsForLoadAsync` — commits the journal to BIRDWATCHER and returns a status string. Only the
  toolbar path (`MainViewModel.Sync.cs:319-321`) calls `ExportToCatalogInboxAsync` +
  `EmitObservedInboundAsync`.
- **failure shape:** edit → close without pushing → reopen → choose **Push** at the dirty prompt. The edits
  reach TS; nothing reaches `Catalog.db`, and because the journal is consumed there is no later push to
  carry them. Silent — the feed's own rule-#16 loud-failure path never runs, since it is never reached.
- **correction to the flag as first written:** the *observed* half was never missing on this path —
  `LoadAsync:153` calls `EmitObservedInboundAsync` after `PrepareTsForLoadAsync` returns, and its comment
  names this case ("or a dirty-open push's closing pull"). Only the **authored** emission
  (`ExportToCatalogInboxAsync`, which projects `PushResult.AppliedEntries`) was absent. That narrows the
  blast radius but not the severity: the user's own intent is the half that was lost.
- **FIXED 2026-08-16** (same session, user-directed): both surfaces now go through one
  `MainViewModel.PushAndExportAsync` — the emission rides the commit, not the caller. The
  `catalog-export` spec's "at exactly one point" was the wording that made two commit sites look legal;
  it now reads "after **every** successful push-as-replay commit, whichever surface triggered it," with a
  new scenario for the dirty-prompt path. Regression test
  `CatalogInboxExporterTests.OpenTimeDirtyPush_EmitsToo_ThePushFunnelIsShared` drives
  `PrepareTsForLoadAsync` directly (LoadAsync's later phases scan the production library, which a unit
  test can't) and was **verified to fail against the un-welded code** before the fix was restored.
  Suite 455 green.

### CB-2 — a rule-#16 silent default in the exporter *(low)*

- **contract:** the v2 design's own posture — `minimumtime`/`horizonoffset` are "hard-cast required (the
  importer aborts on their absence; a null here is the same contract violation surfacing as a cast failure
  at read)" — plus global rule #16 (abort on input-contract violation, never `|| default`).
- **violation:** `TargetSchedulerManager.App/Services/CatalogInboxExporter.cs:416` reads
  `AsDouble(r[9]) ?? 0` for `project.horizonoffset`, fabricating `"horizon_offset_deg": 0`, while the
  sibling required field `minimumtime` on the same line is hard-cast (`(long)r[5]!`).
- **mitigating (verified this sweep):** TS declares `horizonOffset` as a non-nullable `double`
  (`TargetScheduler/…/Database/Schema/Project.cs:57`), so the `?? 0` can only fire on a corrupt or foreign
  row. It is rule-#16 cruft to delete rather than a live fabrication — but the asymmetry with its neighbour
  is the tell, and a fabricated authored value is the one thing the inbox contract must never carry.
- **FIXED 2026-08-16** (same session, user-directed). Fixing it surfaced **two more of the same class** in
  the adjacent reads — `exposure` as `?? -1` (line 427) and `defaultexposure` as `?? 0` (line 435) — both
  also non-nullable in TS (`exposure` carries `[Required]`), and both worse in consequence: `-1` is the
  *inherit-the-template* sentinel and `0` is a **literal** zero-second exposure in this domain, so either
  default would have published a plausible authored value rather than an obviously broken one. Rule #16's
  "when you meet an EXISTING such fallback during related work, remove it and route to the abort path"
  makes fixing the set the correct scope, not scope creep. One shared `Required(value, table, identity,
  column)` helper now aborts naming the row and the column; the `AsDouble` coercion stays inside it,
  because SQLite affinity can hand back INTEGER for a whole REAL and a plain `(double)` cast would throw
  `InvalidCastException` on a legitimate value. Checked before landing: TSM's own adoption insert always
  writes `exposure` explicitly (`AdoptionPlanner.cs:399` — the `-1.0` sentinel or the disk seconds), so no
  TSM-created row can trip the new abort. Test
  `ReadRows_NullInARequiredColumn_AbortsNamingIt_NeverFabricates` covers all three columns. Suite 458 green.

Both are tracked as one line each in `ROADMAP.md` → *Doc-system open items* (M13).

## Accounting (M16 — required slot)

**Prune / archive candidates found this sweep: 7.** Five applied, two held as `keep` on adjudication.

| # | Candidate | Verdict |
|---|---|---|
| P1 | `ROADMAP.md` § *Clock-seam migration — CLOSED 2026-08-11* | **struck** — shipped history duplicated in CHANGELOG 2026-08-11 (fuller); its standing rule graduated to `CONVENTIONS.md` (G8) |
| P2 | `ROADMAP.md` § *Doc-system open items* ("All closed 2026-08-03 … Nothing open.") | **struck** — an "open items" heading over an 8-line closure narration; replaced by this sweep's actual open items |
| P3 | `ROADMAP.md` § *Deferred* bullet 3 (°(M) mechanical rotation) | **condensed** to a decided/shipped cross-ref — it described present-tense shipped behavior under a "Deferred" header whose intro says one dimension (Telescope) remains open |
| P4 | `docs/2026-07-29-maintain-report.md` § *Held graduates — recorded, NOT applied* | **annotated closed** (M8: the rails stay; only the open-reading is removed) |
| A1 | `docs/2026-08-03-audit-report.md` | **archived** → `docs/archive/` with a dated status banner; its one report-only finding was resolved the day it was written. Inbound link in CHANGELOG repointed |
| A2 | `NOTEBOOK.md` 2026-07-23 alias-removal closure entry | **held as keep** — see below |
| A3 | `NOTEBOOK.md` 2026-07-08 morning entry (self-labeled SUPERSEDED) | **held as keep** — see below |

*Why A2/A3 were not archived:* both are closed, and both duplicate no reference-tier content (the doctrine
they illustrate is already single-sourced in `ARCHITECTURE.md` + `DOMAIN.md`). But NOTEBOOK's charter is
"read it for *did we already learn X by doing it*", and these two entries plus the 2026-07-08-late correction
form one legible arc — observation → correction → execution → closure — of a **reversal**. Splitting the arc
across `NOTEBOOK.md` and a new `docs/archive/` record would fragment the why/when M8 protects, to save ~15
lines in an 11 KB file under no size pressure. Re-examine if NOTEBOOK ever does come under pressure.

**Net reference-tier delta:** **+12 graduates** landing in 8 docs (`UI.md` ×4, `ARCHITECTURE.md` ×2,
`TS-SCHEMA.md` ×2, `CONVENTIONS.md` ×2, `DOMAIN.md`, `RELEASING.md`, `CLAUDE.md`) against **−2 ROADMAP
sections struck + 1 condensed + 1 doc archived out of the live journal**. ROADMAP shrank ~20 lines and now
carries only forward-looking content plus this sweep's three open items; the journal lost nothing.

**Incidental currency fixes** made while editing the same lines (audit-axis, disclosed for the record):
ROADMAP's Status stamp 2026-08-06 → 2026-08-16; "everything through 2026-08-06" → 08-13; "current release
TSM v1.4.0 on AL v1.4.0" → **v1.5.5 on AL v1.9.0** (verified against `git tag`); "16 capability specs"
de-valued to "one capability spec per shipped capability" (R28 — it had already drifted to 17, and the count
carries no rationale); `TS-SCHEMA.md` target `name` marked ✎(guarded) after the 2026-08-12 rename verb.

**Double-maintenance noted, not resolved:** the pairing-key/re-reconcile trigger list is enumerated in *both*
`ARCHITECTURE.md` → Key facts and `UI.md` → Editing, and both were stale in the same way (missing target
`name`). Left as-is this sweep — collapsing one into a pointer is a placement call for an audit pass.
