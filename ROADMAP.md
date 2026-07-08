# TargetSchedulerManager (TSM) — Roadmap

**Charter:** the forward-looking plan + current status + a Recently-shipped digest (git is the full
changelog). Read it for *what's next / where things stand*.

Phased build. Each phase stands on its own. See `ARCHITECTURE.md` for the design.

> **Naming:** this project was **TargetCatalogManager (TCM)** until 2026-06-11. Dated entries below the rename
> keep the names they shipped under (TCM, `tcm`, `tcmui`, `tcm.log`, `TCM_DIAG`) — they match the git history.

## Status — pick up here (2026-07-08)

TSM is the WinUI **TS-database manager**, app-only (CLI removed 2026-06-11): a reconciliation grid of TS plan vs
disk-ACTUAL — fresh in-memory scan each load (no `Catalog.db`), per-(target, filter, purpose, seconds) plane rows,
over the **local TS working copy** under the pull → edit-local → push-as-replay **sync model** (shipped
2026-07-06: baseline-skipped pull at open, journaled local edits, reviewed Push to BIRDWATCHER, automatic
write-back each load — user-verified against the rig same day). Editing shipped so far: target enable checkbox
+ in-grid `desired` (verified live in NINA pre-sync-model) + the **edit flyout** (hover glyph / right-click on
target + filter rows → schema-generated form: target `priority`/`rotation`, plan `exposure`; per-field guarded
commit). Real data: 77 disk × 102 TS targets → 44 Both / 25 Planned-only / 33 Actual-only, 6 mosaics
(28/10/7 panels), 783 grid rows. Match tolerance **0.5°** (validated 2026-06-04).
**Next:** editing-surface Parts 1–4 all shipped + verified (Part 4 below, 2026-07-07). The **load-split is
RETIRED** (2026-07-08): the full scan (~2 s, ~97% of load) is acceptable even at 2× the library, so a
cross-load scan cache would buy the stale-ACTUAL window for time that isn't felt — every load keeps scanning
fresh ("the grid can never show stale ACTUAL" stays unconditional). The `ScanLibraryAsync`/`ResolveAsync`
seam stays (it serves the in-load write-back re-resolve). If scan time ever hurts, reach for per-target
`ScanUnitsAsync` rescans or LCM's persistent catalog — not a session scan cache. Leading candidate now:
the write-back app action (primary-workflow priority, 2026-07-06).

**▶ SHIPPED 2026-07-08 — sync-direction marks (`openspec/changes/edit-direction-marks`).** New leftmost
grid column: one mark per row level — `←` BIRDWATCHER arrived different (new pull-time field differ
`TsInboundDiff`: `TsSync.Pull` snapshots the authored displayed/editable field set before the backup, diffs
after, unions into a session-sticky in-memory `TsInboundStore`) · `→` unpushed journal writes (manual +
write-back; pure journal re-read, restart-safe) · `⇄` both · blank clean. Actuals mask: an
acquired/accepted write-back stamp drops those inbound entries (disk supersedes the rig's totals → `→`,
clean after push; `desired` never masked). Headers union their subtree via `Services/SyncMarks` — graph
plan-key map covers plans folded into multi-plan rollups; a mosaic project edit marks the parent only.
Tooltips: per-field old→new (leaves), direction counts (headers). One in-place sweep
(`RefreshAllMarks` — never a collection rebuild) from load/edit/push/discard. Accepted gap: template edits
mark no row (badge + push review still carry them). 159 App.Tests (21 new: differ/pull matrix, mask,
resolver, VM lifecycle). Visual pass clean; **archived 2026-07-08**
(`openspec/changes/archive/2026-07-08-edit-direction-marks`; main spec seeded `edit-direction-marks`).

**▶ SHIPPED 2026-07-07 — cadence-safe TS edits (Part 4; `openspec/changes/archive/2026-07-07-cadence-safe-ts-edits`; library
`76bbae0` + `c606ba5`).** Per-filter `enabled` (checkbox on 1:1 plan rows + plan flyout) and
`project.filterswitchfrequency` (project flyout) with the **transactional cadence clear**: library
`TsField.Clears` (`None`/`Target`/`Project`, replaced `CadenceSafe` — breaking, no shim);
`TargetSchedulerEditor` deletes the scoped `filtercadenceitem` rows in ONE transaction with the column write
(TS restores cadence verbatim, regenerates only from empty), skips unchanged values (verified no-op, no
clear), and refuses target-scope edits with `RefusalReason.HasOverrideOrder` when hand-authored override
orders exist. **Sync-model composition falls out free:** the clear lives inside `TrySetField`, so the push
replay re-derives the delete scope on the remote; a locally toggled-back field replays as a no-op and
correctly keeps the remote's still-valid cadence (seam-tested), and an OEO refusal at replay is a retained
partial failure. UI: direct commits, no confirm (revised 2026-07-07 -
the atomic clear makes toggles produce the TS-expected result; push review stays the gate); plan `enabled`
flows reader → resolver → projection cell → row
(1:1 rule like `PlanTsKey`). 170 lib tests (scoped clears, no-op, OEO, rollback-atomicity via RAISE(ABORT)
trigger) + 131 App.Tests. **User-run pass verified clean 2026-07-07** (direct toggles — no dialog — local `filtercadenceitem` cleared + journaled, push → BIRDWATCHER cleared + TS regenerates, fsf via project flyout).

**▶ SHIPPED 2026-07-06 — template manager (editing-surface Part 3; `openspec/changes/archive/2026-07-06-template-manager`; library
`201dd50`).** The full exposure-template surface, edit-only: the library's `TsEditableSchema` grew 11
exposuretemplate rows (18 total) — `twilightlevel` (new `TwilightLevel` enum map, codes from the TS source;
column spelling is TS's EF rename of `twilightlevel_col`), `minutesoffset` (±720, negatives legal), the moon
avoidance suite (relax min altitude floor −90° — TS ships −15), `moondownenabled`, `ditherevery`,
`maximumhumidity` — all cadence-safe, so the schema-generated form renders them with zero UI code. Two entry
points (user decision — templates have no grid rows): toolbar **Templates…** picker (graph-sourced: name ·
filter · used-by count, zero-use included; empty before a load → status note) and plan-row **"Edit template…"**
(plan → template resolved through the retained graph, no row-model change). Blast radius always stated:
flyout title + push-review label read "Template '<name>' — used by N plan(s)". Add/delete/duplicate stay TS
functions. 163 library tests (surface/bounds/enum pins) + 128 App.Tests (picker list order/used-by/zero-use/
keyless-skip, plan→template resolution, template journal seam). **User-run pass verified clean 2026-07-06**
(picker sanity, row item, moon-suite + twilight edits, gain/offset sentinels intact, push → NINA verify).

**▶ SHIPPED 2026-07-06 — project-settings flyout (editing-surface Part 2; `openspec/changes/archive/2026-07-06-project-settings-flyout`).**
Right-click "Edit project…" on any row resolving a TS project key (target groups, panels, plan rows — the
context menu went additive: a row offers its own editor plus the project's; mosaic parents keep "Edit mosaic
project…") opens the schema-generated flyout for `TsTable.Project` — all 12 cadence-safe fields including
`state`, whose recorded hazard was **retired by reading the TS source**: nothing stamps
`ActiveDate`/`InactiveDate` on transitions (schema setters, planner, and `ProjectViewVM.Save()` are plain
writes), so state is an ordinary `ProjectState` enum edit. TS's one cross-field save rule replicates as
**warn-never-block** (`Shared/ProjectRules.IsNeverSelected`): committing `minimumtime`/`meridianwindow` while
the pair means never-selected shows a persistent caution under the form (evaluated from seed + verified
commits, clears when fixed, warns at open too) + a status note; the write always proceeds. Commits ride the
existing gate → journal → reviewed push untouched. 122 App.Tests (+ pair-rule theory, project-key journal
seam). **User-run pass verified clean 2026-07-06** (each row shape's menu, a knob per field type, state
flip in the push review, warn appear/clear, push → NINA verify).

**▶ SHIPPED 2026-07-06 — TS sync model: pull → edit local → push-as-replay (`openspec/changes/archive/2026-07-06-sync-model`).**
Replaces the LIVE/LOCAL two-world editing (radios, direct SMB writes, sticky-fall, `EditOutcome.LiveDropped`,
post-write `ClearAllPools` — all deleted) with one editing world: **`Shared/TsSync`** pulls BIRDWATCHER's db
over the local copy at open via the SQLite **online backup API**, skipped when the persisted baseline
(`*.tsm-sync.json`: remote size+mtime) matches and no remote sidecar exists — rapid test relaunches skip the
copy. Every verified edit lands locally and appends to the persisted **journal** (`*.tsm-edits.jsonl`;
dirty ≡ journal non-empty, crash-safe by derivation). **Push** (toolbar, review `ContentDialog`) replays the
collapsed journal: write-back entries per-plan via `TargetSchedulerWriter` (desired ratchets against *remote*),
manual entries per-field via `TrySetField` — only journaled fields are touched, so NINA's nightly
counts/`acquiredimage`/XFM grades can't be clobbered; remote sidecar refuses the whole push; per-entry failures
retain loudly; full success ends in a fresh pull (baseline invariant: recorded ⇔ local mirrors remote).
**Write-back went automatic**: `Services/WriteBackStep` stamps drifted counts into the local db after every
load and journals them; the push review lists them decreases-first. Open-with-dirty prompts push/discard/not-now
BEFORE any pull; offline sessions journal and become pushable at reconnect (softened rule — Discard preserves
the debug path). Toolbar: sync badge (`synced HH:mm · N unpushed`) + Push… + Pull now; Reload never pulls.
113 App.Tests (pull/skip matrix on real temp SQLite, push-replay seams, journal round-trip/collapse/torn-line,
write-back step). **User-run pass verified clean 2026-07-06** (fresh pull / skip / offline / edit→push→NINA
verify / decreases review / dirty-prompt-after-kill).

**▶ SHIPPED 2026-07-06 — context-sensitive edit flyout (editing-surface Part 1;
`openspec/changes/archive/2026-07-06-field-editor-flyout`).** One schema-generated form (`Controls/TsFieldsEditor.cs`) renders any
TS row's cadence-safe editable fields straight from the library's `TsEditableSchema` (Bool→ToggleSwitch,
Whole/Real→NumberBox with Min/Max clamp, Enum→ComboBox from the new `EnumValues` maps, Text→TextBox; Unit
suffix, Notes tooltip) — adding a field to the reference lights it up with zero UI code. Triggers: a
hover-revealed pencil glyph and a right-click menu on TS-backed target rows ("Edit target…") and 1:1 plan rows
("Edit exposure plan…"), both opening a row-anchored `Flyout`. Values seed **fresh from the current db**
(`TsEditGate.ReadFieldsAsync`; drifted columns omitted, row-missing/read-fault → error content, never fabricated
defaults); each field commits independently through the guarded gate (light-dismiss can't lose work), failures
revert the control, `active`/`desired` route through their existing setters so the grid mirrors in place (the
enable checkbox went `Mode=OneWay` + `ApplyEnabled`). Cadence-breaking fields are excluded via
`IsCadenceBreaking` until the parked cadence change ships. Library side: declarative `TsEnumValue` maps
(`6a2cabf`, 156 lib tests). 89 App.Tests, 0 warnings; ships the queued "target `priority` editing" item.
**Visual verification pending (user).**

**▶ SHIPPED 2026-06-26 — closed the `TargetCells` projection leak (review's full set now done).** `BuildRows`
was indexing `graph.Targets` to read one target's `ImportedFromTsGuid` for a planned-only mosaic panel's key —
reaching past the projection into graph internals. It turns out `TargetCells` already carries that value as
`TsTargetKey` (the projection assigns `t.ImportedFromTsGuid` to it), so the fix was a 2-line consumer-side swap
(`child.TsTargetKey ?? child.Name`) that drops the `graph.Targets` dictionary — `TargetCells` is now the complete
contract for the grid shaping (`graph` stays only as the projection's input). No library change, zero behavior
change (identical value, same source), 85 App.Tests, 0 warnings. Candidate C — the last of the four
architecture-review candidates (A/B/D shipped above).

**▶ SHIPPED 2026-06-26 — the two header rows share an `AggregateHeaderRow` base.** `TargetGroupRow` and
`PanelGroupRow` were ~90% identical — both wrapped `RowAggregates` and hand-rolled the same `IsExpanded`/chevron,
`*Text`, `HoursText`/`HoursBackground`, and `Recompute` (a fill/format change meant editing both — a live drift
risk). New `ViewModels/Rows/AggregateHeaderRow.cs` (an abstract **base class** — not an interface, so WinUI
`x:Bind` resolves the members on each template's concrete `x:DataType`) owns every shared aggregate-display rule
once; the two concretes keep only their specifics (target: enable checkbox · panels · project · target id;
panel: key · label). The vestigial always-0 `TargetGroupRow.FilterCount` was deleted. Zero behavior change (the
chevron glyphs are byte-identical); +3 parity tests prove the two headers render identically for the same
children. 85 App.Tests, 0 warnings. Candidate D from the architecture review.

**▶ SHIPPED 2026-06-26 — grid flatten/splice extracted to a tested `VisibleRowTree`.** The "visible rows"
derivation was encoded twice — a wholesale rebuild (`AppendGroupContent`/`AppendLeaves`) and three toggle
methods whose **remove** side re-derived the structure by scanning runtime types (`while next is not
TargetGroupRow`, `while next is ReconciliationRow`, `remove detail.Count`) while their **insert** side already
shared the rebuild rule. New `ViewModels/VisibleRowTree.cs` owns ONE `ExpandedContent(node)` ("the rows a node
contributes when expanded") driving the rebuild **and** every toggle's insert *and* remove, the in-place
`Toggle` splice, and the node-identity / expansion-key formats (target · `target|panel` ·
`target|panel|filter|purpose`) that were scattered across the loader, `MainViewModel.RollupKey`, and
`ExpansionState`. The three `MainViewModel` toggles collapse to one-liners; `AppendGroupContent`/`AppendLeaves`/
`RollupKey` + the type-scanning removes are gone; `ExpansionState` is now a dumb string-set behind the tree.
**Zero behavior change** (the existing toggle/filter/sort tests pass unchanged); +11 module tests pin
`ExpandedContent` per node type, the key formats, and the headline **splice == rebuild** invariant (collapse
removes exactly what expand inserts). Pure over the row objects' `IsExpanded` flags — no VM, no XAML. 82
App.Tests, 0 warnings. The second "Strong" deepening from the architecture review (Candidate A; B = the TS-write
seam below).

**▶ SHIPPED 2026-06-26 — guarded TS write extracted to `TsSource` + `TsEditGate` (deep, injected, tested).**
The LIVE/LOCAL state machine + the guarded write — previously smeared across `MainViewModel` + `TsDatabaseResolver`
+ the library editor (the safety-critical rig-write path, with **zero test coverage**) — became two App-side
`Shared\` modules: **`TsSource`** (LIVE/LOCAL paths · injected reachability probe · mode + sticky state; consulted
by the load *and* the gate) and **`TsEditGate`** (one `ApplyAsync(...) → EditOutcome` =
`Applied`/`Refused`/`Failed`/`LiveDropped`, over an injected `ITsEditor`; delegates the sticky-fall to `TsSource`
on a live drop; audits every write attempt). The library half is the consumer-neutral
**`TargetSchedulerEditor.TrySetField → (FieldEditResult?, RefusalReason)`**, folding the four open-db guard
predicates (required-columns / read-only / open-sidecar / column-present) into one structured-refusal call.
`MainViewModel.ApplyFieldEditAsync` (~60 lines) + the four `_tsMode/_liveDisabled/_tsProbed/_tsDbPath` fields
deleted; the VM now holds the gate and maps `EditOutcome` to status + side-effects. **~16 new tests** drive the
whole machine (first-probe / re-probe-drop / sticky / `TrySelectMode` / refusal / verify-fail / live-drop-on-write)
with **no SQLite or SMB** — the probe and editor are injected. Built **subagent-driven** from
`docs/PLAN-2026-06-26-guarded-ts-write.md` (per-task TDD + reviews + an opus whole-branch review that caught a
swallowed live-drop exception, now fixed). 71 App.Tests + 153 library, **0 warnings**. A future **WriteBack** app
action reuses the gate via an `ApplyPlanAsync` sibling (deliberately not built — YAGNI). Commits: TSM
`1cda326`→`6150b7d`, Library `8d863e5`. **Pending: user's live-app pass** — one `desired`/`enable` write hitting
the actual TS db (unit tests can't cover the live write).

**▶ SHIPPED 2026-06-20 — count columns reframed: `Acq`→`TS`, `Disk`→`Actual`, `Acc` hidden + `acc≠acq` badge.**
Display-only grid change (grill-me design). The old `Acq`/`Acc`/`Disk` trio mixed two TS-side bookkeeping numbers
with the on-disk truth; now it reads **`Desired`** (TS goal) · **`TS`** (TS's recorded `acquired` — the number TS
schedules on with the grader off) · **`Actual`** (on-disk frames). `Acc` (accepted) left the grid: with grading
off TS increments acquired + accepted together (`ImageSaveWatcher` auto-accepts) and write-back re-sets them
equal, so it only mirrors acquired — a rare in-session `accepted ≠ acquired` drift now shows as a flagged
**`acc≠acq` badge** (data kept in the row model, column gone). The `TS` header intentionally doubles the per-row
Source token (user's call); the em-dash on a Disk-only row (no TS plan) survives. *Rationale from the grill:*
TS's grader-off `PercentComplete` divides acquired by `ExposureThrottle × Desired` (default **125 %**), so a
target shot to `Desired` reads ~80 % and TS over-schedules it — TSM's `remaining = max(0, Desired − Actual)` is
the honest view, which is why `Actual` is the headline. **Deferred:** the `acc = acq = disk` *write* stays the
Phase-4 contract and lands with a future **WriteBack button** (today the app writes only `enable`/`desired`).
Caveat for that button: NINA's TS plugin can ignore external db writes mid-session until a NINA restart
(user-reported, unfixed upstream) — live write-back will want a between-sessions / restart-to-apply note. Reflow
across the header + 3 row templates; `BuildRows` adds the badge + flag; 49 App.Tests (+2), 0 errors. **Pending:
user's visual grid pass.**

**▶ SHIPPED 2026-06-11 — project renamed TargetCatalogManager → TargetSchedulerManager (TSM).** The "Catalog"
name was legacy (the catalog builder left with the CLI; `Catalog.db` goes to the planned LCM) — the app manages
the N.I.N.A. **Target Scheduler** db, so it is now named for what it does. Deep rename: solution
`TargetSchedulerManager.slnx`, projects/namespaces `TargetSchedulerManager.App[.Tests]`, assembly **`tsmui`**,
log identity **`tsm.log` / `TSM_DIAG` / `%APPDATA%\TargetSchedulerManager\Logs\`** (old notes stay in the old
folder; no migration by design), window title, app.manifest, docs (CLAUDE / ARCHITECTURE re-framed to app-only
reality; dated history kept verbatim). Top source dir renamed `…\Astronomy\TargetCatalogManager` →
`…\Astronomy\TargetSchedulerManager` (user-performed). Same day, earlier: the Ctrl+N window was renamed
**Observation → Diagnostics** (`ca97d89`; helper label dropped; TP mirrored in `a48f7f2` incl. its verify-ui
literals) — the shared `USER_OBS_*` log protocol keeps its name in `Astronomy.Diagnostics`. Library doc-comments
that named TCM were degenericized to consumer-neutral wording per shared-lib discipline (separate Library
commit). 47 tests green, 0 warnings.

**▶ SHIPPED 2026-06-11 — logging extracted to shared `Astronomy.Diagnostics`; TCM + TP adopt it.** The
hand-ported `Support/Log.cs` (TCM had copied it from TP, and they'd drifted) became a new pure-managed library
**`Astronomy.Diagnostics`** in the `Library` repo (lib `f0b0fda`) — *convention-as-code*: `Log` (two verbosity
axes — always-on Info/Warn/Error + gated Diag channels, default all-in-Debug / off-in-Release via the app's env
var; session rotation; `%APPDATA%\<app>\Logs\` structure; USER_OBS protocol — all driven by `AppLogIdentity`) +
`ScreenCapture.ToPng`. **TCM** (`7e908f1`) deleted its `Support/Log.cs` and calls `Log.Init("TargetCatalogManager",
"tcm.log", "TCM_DIAG", …)` at startup (the Debug/Release diag default is passed by the app — a shared lib can't read
the consumer's `#if DEBUG`); `System.Drawing.Common` moved to the library. **TP** (WinForms, `2a60d65`) adopts the
*same* lib — **proving the contract is consumer-agnostic (one engine, two UI frameworks)**; a `global using` kept
its ~140 call sites. TCM's Ctrl+N observation window also gained a **repeatable Capture button** (mid-session
screenshots + a status readout) and **local-time** filenames (`8491e3b`). 47 TCM + 187 TP + 148 library tests, 0
warnings. The per-app dialog stays per-app (WinForms `Form` vs WinUI `Window`); an `ObservationSession` to share the
START/CAP/END/CANCEL orchestration is deferred.

**▶ SHIPPED 2026-06-11 — M2 editing slice 2: in-grid `desired` editing + the `TsEditableSchema` reference.**
The Desired cell on a **1:1 plan leaf row** is now an editable `NumberBox` (headers, disk rows, and mixed-seconds
rollups stay read-only — the 1:1 rule, tested); committing on focus-loss writes `exposureplan.desired` to the
**live BIRDWATCHER db**, **verified end-to-end in NINA**. Built **reference-driven** (the user's "a global
reference to our copy of the TS tables, not guess-per-field" call): new library **`TsEditableSchema`** — one
declarative row per editable TS column (table · exact SQLite column · type · cadence-safe? · enum/range), authored
from the TS plugin schema, since `PRAGMA` yields column names/types but not *which* are user-editable vs stats/keys
nor which break cadence (domain knowledge). The editor drives off it: generic **`SetField`/`ReadField`** validated
against the reference (which doubles as the SQL-injection whitelist) + **`IsFieldAvailable`** (a `PRAGMA` drift
guard); the three typed setters became thin wrappers. Cadence-breakers (`exposureplan.enabled`,
`project.filterswitchfrequency`) are **flagged, not handled** — a plain UPDATE, so a caller must warn/defer. App
side: one guarded primitive **`ApplyFieldEditAsync(table, key, column, value)`** now shared by the enable checkbox
*and* desired (LIVE/LOCAL + open-sidecar/read-only/column-absent refusal + read-back verify + audit +
BIRDWATCHER-drop sticky-fall). Edits apply **in place** — the leaf takes the new count and its group/panel totals
re-aggregate via INPC — instead of reloading, so **scroll position and a half-typed next cell survive and rapid
edits aren't torn down**; `SqliteConnection.ClearAllPools()` after each write fixes a stale read over SMB (a pooled
reader was serving cached pages, `tsRead=0.00s`, showing a verified write as if it hadn't taken). Library
`ReconciliationCell` now carries `PlanTsKey`/`TemplateTsKey` (single-plan) + `TargetCells.ProjectTsKey` as the
write-back addresses. Library 148 tests, TCM 46, 0 warnings. Commits: library `563836d`, TCM `d4dc39d`
(on `70bace1` panel-removal).

**Two UI decisions this session — recorded so they're not re-litigated.** (1) The **docked dossier panel was
built then dropped** ("a waste of space") — editing goes **in-grid**, in the existing flattened-`ListView` idiom.
(2) **WinUI.TableView was evaluated and rejected** as the editing surface: the overview grid is a *hierarchical
tree* (target → panel → leaf → rollup) a flat data-grid can't render, and the app's coherence — one paradigm, zero
deps, DB-as-truth re-derive — wins over a foreign editable grid (re-addable on a branch if a flat whole-catalog
spreadsheet ever emerges). **NEXT (same lane, ~one field each):** `priority` (target/project, cadence-safe) →
per-filter `enabled` (cadence-**breaking** — adds the `FilterCadenceItem` clear, lifting TS's `ToggleExposurePlan`)
→ the **load-split** (Reload re-reads TS-only against the cached disk scan, ~0.3 s vs ~2 s).

**▶ SHIPPED 2026-06-11 — CLI removed; TCM is app-only.** The transitional `tcm` console host (`Program.cs`,
`Cli\`, the root csproj, `Cli.Tests`) was deleted — it had become a dual-head maintenance tax (every feature done
twice). TCM is now purely the WinUI **TS-database manager**. `DevDefaults` + `TsDatabaseResolver` moved into the
App (`App\Shared\`); the resolver tests moved to `App.Tests` (43 green, 0 warnings). **Nothing lost:** the
catalog-build engine is one AL call (`CatalogBuilder.BuildAsync`, disk-only via `tsDb: null`) and the write-back
engine stays in AL. Catalog-build moves to a planned **LibraryCatalogManager (LCM)** (sibling dir
`..\LibraryCatalogManager`, ROADMAP template there); write-back resurfaces later as a TCM app action. `Catalog.db`
is currently unbuilt — unconsumed, so fine. Reframe: TS = disposable (TCM manages it), `Catalog.db`/IS = permanent
(LCM owns it); the two no longer tangle. Phases 1–4 below describe the **library's** catalog/write-back engine,
which is unchanged and still consumed by TCM (in-memory) + the future LCM.

**▶ SHIPPED 2026-06-11 — live BIRDWATCHER TS db (read + write), local fallback.** TCM no longer edits only a
local copy. `TsDatabaseResolver` (`Shared\`, both heads) probes `\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite`
under a ~1.5 s timeout (so a down host can't hang startup on SMB) → **LIVE when reachable, else the local working
copy**. The CLI `--ts` default + the app's load both flow through it (an explicit `--ts` still wins); the CLI
banner + a caution-colored app toolbar badge say which, and writeback's audit logs `live=`. This **reverses the
old "never the live db" invariant** — risk accepted + mitigated: daily Macrium image of BIRDWATCHER (corruption →
restore), night-image/day-edit rhythm (rig idle when editing), plus the existing open-sidecar / read-only refusals
+ read-back verify. Verified: `tcm` reads the live db over SMB (banner LIVE, 102 TS / 44·25·33, 2.0 s); resolver
tests (reachable→live, missing/bad-path→local); 56 TCM tests, 0 warnings. **Pending user's pass:** app badge reads
LIVE, a target-enable toggle lands on the live db (`py`+sqlite3 on the UNC), BIRDWATCHER-off shows LOCAL.

**▶ SHIPPED 2026-06-11 — M2 editing slice 1: target-enable checkbox (immediate write).** First TS edit. A compact
checkbox is the grid's new **leftmost column**, on the **target group-header only** (hidden on disk-only +
mosaic-parent rows — no TS target behind them). Toggling writes `target.active` to the **local TS working copy
immediately** — read-back verified + audited to `tcm.log` (`EDIT target.active …`), off the UI thread, **no grid
reload** (active changes no counts/hours); a failed/unverified write reverts the box. `target.active` is the
**one T1 edit that doesn't touch filter cadence**, so it's a plain `UPDATE` — the safest first edit. New library
`TargetSchedulerEditor` (`SetTargetActive`, resolves by-guid-or-Id + verify; mirrors the Writer's hardening —
private cache, column guards); `TargetCells` now carries `Enabled` + TS provenance (the provenance R1 deferred).
A VM override keeps a flip consistent across filter/sort rebuilds (cleared on Reload). Real data: 102 TS targets
all guid-keyed, 59 active. Library 134 tests (+5), TCM 53, 0 warnings. **Pending: user's click-test** (toggle a
target, confirm via `py`+`sqlite3` that `target.active` flipped). Next T1 edits (`desired`, `priority`, per-filter
`enabled`) reuse this editor; `enabled` adds the filtercadence-clear.

**▶ SHIPPED 2026-06-11 — M2 prep refactor: R1 cell-projection → library + ExpansionState.** Behaviour-preserving
cleanup of the 215-line `BuildRows` ahead of the TS editor (own reviewable slice, no functional change). **R1:**
the cell join (plans + inventory → per-`(target, filter, purpose, seconds)` cells tagged with match-state) moved
to the library as `Reconcile/ReconciliationProjection.Project` → `IReadOnlyList<TargetCells>` (UI-agnostic;
reusable by IS); the app's `BuildRows` shrank to **shaping only** (planes / rollups / signed hours / fills /
panels / sort over the cells) with its signature unchanged, so the 7 `BuildRowsTests` pin behaviour. **ExpansionState:**
the three expansion `HashSet`s left `MainViewModel` for a tested `ExpansionState` value object. Library 129 tests
(+8 projection); TCM 53 (+6 ExpansionState); 0 warnings; grid output unchanged. **Next — the editing slice:** a
**dialog-based** TS editor (select target → `desired`/`enabled` per filter + `active`/`priority` → Save in one
txn via a new library `TargetSchedulerEditor`, clearing the target's `filtercadence` iff `enabled` toggled,
audited; reader gains the `enabled` column). Structural add/remove of projects + filter-plans stays M3.
**Pending: user's visual grid pass** (confirm 786 rows / 102 groups / 44·25·33 unchanged).

**▶ Phase 4 — `TargetSchedulerWriter` — DONE (built 2026-06-08).** `tcm writeback [--apply]` fresh-rebuilds the
catalog, then pushes disk-derived counts into a **local** TS copy (dry-run by default). Validated on real data:
**182 plans written / 13 held for manual / 92 ignored-missing**, the motivating case `Sh2-142 Wizard H 0 → 140` is
fixed, re-apply idempotent. **`tcm writeback --target "<dir>"`** adds a surgical single-target write — no catalog
rebuild, and for a **mosaic it writes each panel's** counts to that panel's own TS plan (`Mosaic - Cygnus Loop` →
16 panels, 96 cells matched / 80 writes, apply-verify OK, idempotent). Details in Phase 4 below.

**Shipped 2026-06-08 (this session):** `tcm writeback --target` (surgical single-target; per-panel for mosaics) —
**verified live on BIRDWATCHER with NINA/TS running**. CLI hardened so the verb is dash-tolerant (`--writeback`
routes) and `--target` without the verb prints a hint instead of silently running a full build. All committed
(library + TCM, branch `dev`).

**▶ SHIPPED 2026-06-10 — M27/Dumbell = alias, option B** (treat aliases as one object). An **alias** = every
colliding TS name **exactly** matches a disk identity facet (directory / catalog / common / object; normalized, no
substring) — `M27` + `Dumbell` are the two halves of disk `M27 - Dumbell`; the strict rule keeps genuine variants
like `M42` + `M42 core` flagged as real duplicates. Implemented: `AliasTsTarget` in `CatalogBuildReport`,
`TargetResolver.IsAliasName`, and `WriteBackPlanner` auto-writes an alias cell when its plan count equals the alias
member count (disk count to **every** member's plan; any other multiplicity stays `MultiPlan` manual). Verified on
real data: duplicates 1→0, aliases 1, the 6 held cells became 12 writes (both members converge, one `desired`
ratchet 129→169), manual bucket M27-free, dry-run idempotent. 78 library tests (+3).

**▶ Phase 3 planned (grill-me session 2026-06-10) — TS Editor (WinUI 3).** TS stays the daily scheduler until IS
exists; TCM bridges: view + edit the **local TS working copy** with disk-ACTUAL beside every number. Full spec in
Phase 3 below. Build order: **(1) alias rule (above — ✅ shipped) → (2) M1 read-only grid (✅ built 2026-06-10) →
(3) M2 edits → (4) M3 resolution + structural.**

**▶ M1 BUILT 2026-06-10 — `TargetCatalogManager.App`** (WinUI 3, WindowsAppSDK 2.2.0, unpackaged, x64, exe
`tcmui`): read-only reconciliation grid — flat (target, filter, purpose, seconds) per-plane rows, plan vs DISK from a fresh
in-memory scan+resolve (no Catalog.db), search / source filter / flagged-only / sort, match-state badges, mosaic
rollup rows. Self-verified: launches and matches the console exactly (Both 44 / TS-only 25 / Disk-only 33, alias 1,
mosaics 6/38 panels). **Pending: user's hands-on UI pass** (filters, scroll perf, badge readability). Gotcha
captured: the console csproj sits at the repo root, so it must `DefaultItemExcludes` the nested app dir.

**Target grouping (built 2026-06-10, from the M1 pass):** the grid is now a flattened tree — one collapsible
`TargetGroupRow` header per target (chevron disclosure, **collapsed by default**) aggregating its visible
filter rows (Σ desired/acq/acc/disk, Δ, badge union); whole-row click or chevron toggles, Expand/Collapse-all
in the toolbar. Expansion keyed by target name (survives filter changes + reloads); toggle edits the bound
`ObservableCollection` in place so scroll position holds; sort dropdown orders groups by aggregates. Search
respects manual expand state (headers of matching groups appear collapsed; aggregates cover only surviving
children). WinUI shape: two `DataTemplate`s + `DataTemplateSelector`, no real TreeView — the VM owns the
visible-row list (TreeListView-in-VirtualMode style). Smoke-tested: launch clean, `groups=102 expanded=0`.
**Pending: user's visual pass** (chevron alignment, group-row readability, click feel). The accelerator's
floating Ctrl+N hover hint is suppressed (`KeyboardAcceleratorPlacementMode=Hidden`).

**Seconds column + per-exposure rows (built 2026-06-10):** exposure time joined the cell identity end to end.
Library (`b195e31`): scanner buckets `FilterAggregate`s per (filter, purpose, whole-second exposure);
`inventory_filter.exposure_seconds` (renamed from `typical_exposure_seconds`) joins the PK — schema change,
so `Catalog.db` was deleted + rebuilt (482 inventory rows). **Write-back contracts unchanged** (planners
fold/sum splits back to their (filter, purpose[, bin]) keys; 82 tests, +4 covering the folds). App: grid rows
key on (filter, purpose, seconds) — plan and disk join when sub lengths agree, drift shows as separate
plan-only/disk-only rows; "Seconds" column right of Filter; group headers count distinct filters. Verified on
live data: 715 rows / 102 groups, report counts unchanged. (The "match by exposure in write-back" follow-up
shipped the same day — see below.)

**Hours column + plane-split rows (built 2026-06-10, from the user's Ctrl+N notes):** the Δ column is gone;
leaf rows carry per-plane Hours — a TS row (Desired/Acq/Acc; **Hours = desired × seconds**) and/or a Disk row
(frame count; **Hours = count × seconds**). Group header Hours = **disk hours − desired hours** (signed F1,
pill fill: caution = needs telescope time, green = plan goals met). Frame-count Δ survives as the sort key
only; the Source dropdown still classifies whole targets. **Refined same day — Both-row rollups with nested
disclosure (from the user's Ctrl+N notes):** one `Both` rollup per (filter, purpose) that has both a plan and
a disk side, aggregating **every** sub length (counts + hours). Sub lengths all one value → plain merged row.
**2+ distinct times → Seconds reads `mixed`** (caution pill) and the rollup gets its own chevron, expanding
in place into **one source line per sub length** (seconds ascending, deeper indent): a bucket carrying both
planes is a nested `Both` line (plan values + disk count, its own gap hours + fill), one-sided buckets are
TS/Disk lines — answering "where do these times come from". **Hours model (user-refined to fully additive signs):**
every cell is the row's signed contribution — TS lines show **−(desired × seconds)** (the deficit), Disk lines
+(frames × seconds), Both/header cells the disk−plan gap — so each parent is the literal sum of its children's
displayed Hours. Fills: gap cells caution/green by sign; **TS lines filled at every level** (caution while
outstanding; a desired-0 plan shows the **critical/error fill** — data that shouldn't exist); Disk lines stay
plain by choice. Tiny non-zero values render F2 so they never read as 0.0. A Both rollup's Hours = its **disk − desired gap** with
the caution/green fill (per-filter mini header); one-plane rows stay plain everywhere. Rollup expansion is
keyed `target|filter|purpose` (survives filters/reloads); whole-row click toggles, exactly like target
groups. Group `Remaining` is per-row (rollups self-pair). Per-row hours are loader-computed
(`PlanHours`/`DiskHours`) since a mixed rollup has no single seconds value. Verified live: 582 top-level
rows / 102 groups, header deltas unchanged (Abell 21 −10.3 = 15.8 disk − 26.1 desired). **Pending user's
visual pass** (nested chevron feel, `mixed` pill readability) — dark-theme caution/success fills are subtle;
stronger brushes are a one-line swap (`ThemeBrushes.cs`) if wanted.

**▶ SHIPPED 2026-06-10 — review round 2: verified + XS fixes** (`docs\CODE-REVIEW-2026-06-10-round2.md`,
fixes `b67ddfa` + library `7ce569d`). Independent re-review verified every slice-1 claim against the code
(library mounted; all round-1 caveats resolved; no regressions). Fixed its findings: **B1** `--apply <value>`
now warns + stays dry-run (was: any value armed apply on a db-writing verb), **B2** unknown options warn AND
are ignored (key + value pair), **B3** thread-safety doc line on the report's lazy indexes, **B4**
console-capture caveat comment. Cli tests 11 → 13 (48 TCM total). M2 backlog confirmed: R1 opening move,
§7.2 cancellation threading, TsEditSession + loader seam, §7.5 ExpansionState.

**▶ SHIPPED 2026-06-10 — TCM test projects** (`2f74a9f`): the repo's "no tests, thin host" rationale retired.
`TargetCatalogManager.Cli.Tests` (11 — `CliOptions` parsing/warnings; dies with the transitional CLI head) +
`TargetCatalogManager.App.Tests` (34 — `BuildRows` cell projection pinned ahead of R1, `MainViewModel`
filter/toggle/expansion pipeline via the internal `SetRowsForTest` seam, Hours sign convention, `RowAggregates`
additivity, `Format.Hours`). The WinUI head tests run in a **plain test host** — no XAML runtime; only the two
`Brush` getters are out of bounds. `dotnet test TargetCatalogManager.slnx` runs everything. Also this date:
no-migration rule confirmed portfolio-wide (none present; TS guards are refusal-only), doc dates corrected to
user-local (machine clock runs ahead in the evening), logs switched to local-time stamps (`603f3a9`).

**▶ SHIPPED 2026-06-10 — code-review slice 1** (review in `docs\CODE-REVIEW-2026-06-10.md`, executed-status
table at its top; library `c381a2e`+`8bf1aef`, host `b3d8b5d`, app `651abb6`). Three drift hazards
single-sourced into the library — `EffectiveExposure` (THE effective sub-length rule; was 3 hand copies),
`CatalogBuildReport.IssuesFor(...)` issue-membership API (planner + loader stop hand-indexing the report),
`Reconciler.MergeFamilies` (parent rollup; console consumes) — plus host `Program.cs` split into
`Cli\{CliOptions, BuildCommand, WriteBackCommand, ConsoleRenderer, WriteBackAuditLog}` with one shared
writeback `ExecutePlan` tail and unknown-option warnings; app row VMs → `ViewModels\Rows\` (V1) and dev
defaults single-sourced via linked `Shared\DevDefaults.cs` + `ResolveOptions.Default` (V2). 121 library
tests (+13); tcm output + app DIAG verified number-identical. **R1 (full cell projection → library) +
M2-prep (TsEditSession, loader interface, app tests) deferred as M2's opening move.**

**▶ SHIPPED 2026-06-10 — mosaic panels are first-class targets** (library `b296d58`→`f11bee5`, host
`8c1b7ee`, app `1191278`). *A panel is a normal target whose key is composite* (user's architectural
principle): the scanner's one walk retains per-panel sub-reports; `target` gains `parent_target_id` (schema
change → Catalog.db deleted/rebuilt); the resolver flows panels through the ONE standard loop — **scope
keys** (anchor within your key-space; no mosaic conditional in matching), token name-validation
(`Panel 01of16` → `P1`), and the **aligned-outranks-unaligned rule** (an unshot panel inside tolerance of
its shot neighbour stays planned instead of becoming a false duplicate — the Witch Head fix). Bulk
write-back auto-writes panels (`ManualReason.Mosaic` retired; manual went 38 mosaic groups → 6 flagged
Rosette `Panel Center` cells + 1 MultiPlan); console rolls panels up under parents (`Reconciler.Merge`);
the grid gains the panel level (target → panel → filter → seconds detail, collapsed by default, labels
"Panel 01of16 · CygnusLoop P1"). Real data: 6 mosaics → 28 matched / 10 planned-only / 7 disk-only panels;
786 rows / 102 groups; 108 library tests. "Rig" (telescope/camera/mode) flagged as a future key dimension —
deliberately deferred. **Pending: user's visual pass on the panel level; `--apply` not yet run post-panels.**

**▶ SHIPPED 2026-06-10 — exposure-aware write-back (library `87ae471`, host `ba23f06`).** The write key is now
**(target, filter, purpose, whole-second exposure)** — *the plan's seconds is the spec* (user-decided strict
semantics): each plan receives the disk count at exactly its effective duration (plan exposure ?? template
default), **0 when none match** (flagged decrease — 600 s frames never satisfy a 900 s plan). Same-purpose
plans at different durations auto-resolve (no longer manual); disk buckets no plan targets are
**`UnplannedFrames` notes**, never written, never manual (plan creation is M2's). Surgical `--target` matches
(filter, purpose, bin, seconds) and deliberately never zeroes plans with no matching cell (bulk does — see
ARCHITECTURE). Output: per-row `@900s`, `--target` lists no-ops explicitly, new `unplanned` section + summary
count. **New `tcm-cli.log`** (append-only, `%APPDATA%\TargetCatalogManager\Logs\`) audits every run's full
decision trail. 91 library tests (+9). Live dry-run verified: Medusa H/S/O @900 → 0, R → 0, B → 2; M17 H
stays MultiPlan (its two plans share 900 s); bulk totals 105 decreases / 154 no-ops / 38 manual (mosaics) /
140 unplanned. **`--apply` not yet run — user's call** (working copy restorable from `schedulerdb - Copy.sqlite`).

**Logging (slice 1, built 2026-06-10, ported from TP):** `tcm.log` under `%APPDATA%\TargetCatalogManager\Logs\`
(session rotation, WARN/ERROR, `TCM_DIAG` channels) + **Ctrl+N observation window** — modeless always-on-top,
USER_OBS START/END markers, notes + VM ctx snapshot + main-window screenshot into the log stream. Use it during
the M1 pass to annotate findings in place. Slice 1 **verified interactively** (checkpoint / note / cancel /
rotation / clean screenshot). **Slice 2 built + log-verified 2026-06-10:** `DIAG/Load` (per-stage timings — scan
1.77 s of 1.83 s total, the XISF walk is the whole load cost — + report counts) and `DIAG/UI` (filter trail with
row counts), `TCM_DIAG`-gated. Standing M2 rule — the writer logs every TS write. TS read+write remains a **stop-gap** until IS/ISP reads `Catalog.db` directly — but the Phase-3 **UI
shell is permanent** (retargets Catalog.db when IS arrives); only the TS data layer is disposable.

Write-back's **manual bucket** (never auto-written — presented with full info to resolve): **dup-folds**
(`M27`/`Dumbell`: two TS targets onto one disk target, plans accumulate) and **identity conflicts** (name-mismatch
/ ambiguous coord match, e.g. `CygnusLoop P3` ↔ `NGC 6995` — auto-writing a false-positive match would zero a real
TS target's counts).

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
- **TCM headless host** (`Program.cs`): `tcm [--catalog --library --ts --tolerance]` runs the build + prints the
  reconciliation report and goal-vs-actual summary.
- 42 Catalog tests + 45 NINA tests pass.

## Phase 3 — TCM app: TS Editor (WinUI 3)  ◀ IN PROGRESS (planned 2026-06-10; M1 ✅ · M2 ✅ editing surface shipped, load-split retired 2026-07-08 · M3 pending — see Status above)

**Purpose.** TS remains the daily scheduler until IS exists; TCM is the bridge: view + edit TS's database with
disk-ACTUAL beside every number. A pragmatic editor, **not** a TS Database Manager replacement. The TS *data
layer* is the disposable stop-gap; the **UI shell is permanent** — it retargets `Catalog.db` plans when IS
arrives. Supersedes the old "migrate XFM's scheduler tab" framing: the grid replaces that tab (XFM's is deleted
at Phase 5 cutover).

- **DB touched:** the **local TS working copy only** — since 2026-07-06 synced by the pull/push model
  (`TsSync`; see the Status digest): pull at open, journaled edits, reviewed push-as-replay to BIRDWATCHER.
- **Structure:** new **`TargetCatalogManager.App`** (WinUI 3) beside the untouched `tcm` CLI (WinExe can't host a
  clean console). Edit layer **`TargetSchedulerEditor`** in `Astronomy.Catalog/TargetScheduler/` next to
  Reader/Writer — tests live in the library; same cleanly-deletable contract; no consumer terminology.
- **UI shape — grid-first:** home screen is a flat filterable **(target, filter, purpose, seconds)** reconciliation grid —
  plan vs DISK vs Δ; disk columns from a **fresh scan on load** (~1 s, the same self-contained path `writeback`
  uses; `Catalog.db` isn't needed for the editor screen). Tree (Profile ▸ Project ▸ Target) is secondary nav.
  Mosaics appear **per panel** (TS granularity) + a rollup row. In-grid editing for Tier 1; detail panel for
  Tiers 2–3; toolbar/context commands for Tier 4. **`acquired`/`accepted` are read-only** — Phase 4 write-back
  owns those columns.
- **Edit tiers (all in scope):** **T1** counts & toggles (`desired`, plan `enabled`, target `active`, target
  priority) · **T2** identity & pointing (`name`, RA/Dec, epoch, rotation, ROI) · **T3** project knobs (state,
  priority, description, altitude/horizon/meridian, filterSwitchFrequency, ditherEvery, smartExposureOrder,
  enableGrader, flatsHandling, ruleWeights) · **T4** structural (add/delete target & plan, template swap, move
  between projects). Templates/profiles render read-only.
- **Differences are first-class:** all three sources shown, match-status **badge column + filter bar**, per-class
  resolution commands — **Disk-only** (33) → *create TS target from disk* (dir name, plate-solved coords, plans
  seeded from existing filters); **TS-only** (25) → leave (legit not-started) / *rename-to-disk* showing nearest
  disk candidates with distances (catches `Forsaken`-0.50° near-misses) / delete; **name-mismatch / ambiguous** →
  *adopt disk name* / accept; **dup-fold** → alias rule (genuine variants only, post-option-B).
- **Milestones:** **M1 view** (✅ built 2026-06-10, see Status) — read-only grid + badges + filters, verified
  against the known 44 / 25 / 33 → **M2 edit** — Tier-1 in-grid + detail panel (T2–T3) + save → **M3 resolve** —
  resolution commands + Tier 4.
- **Hazards (carry into M2/M3):** cadence-breaking edits (T3/T4 — filterSwitchFrequency, ditherEvery, plan
  add/remove, …) must replicate TS's `filtercadence`-clear behaviour (lift the exact rules from TS's
  `TargetViewVM` / `SchedulerDatabaseContext` when building M2/M3); T2 name edits must round-trip the matcher's
  name-validation without minting new mismatch classes.

## Phase 4 — Write back to TS  ✅ DONE (built 2026-06-08)

`TargetSchedulerWriter` (in `Astronomy.Catalog/TargetScheduler/`, mirroring the Reader) writes disk-derived counts
back into TS so its planner stops over/under-scheduling. **Stop-gap** until IS/ISP reads `Catalog.db` directly —
minimal surface, cleanly deletable at Phase 5. Built 2026-06-08 (grill-me design + real-data validation, 58 library
tests). Verb: `tcm writeback [--apply]` (dry-run default).

The full write-back contract — cached-columns-only writes (`acquired`=`accepted`=disk, `desired` ratchets up
only), disk-wins one-way conflict, the `(target, filter, purpose, seconds)` join, manual-bucket gating, the
open-sidecar / read-only / private-cache guards, and the surgical single-target (`--target "<dir>"`, per-panel
for mosaics) path — is the load-bearing spec in **`ARCHITECTURE.md` → TS write-back** (single-sourced there;
don't re-document the mechanism here). Real-data validation: **182 plans written / 13 held manual / 92
ignored-missing**, motivating `Sh2-142 Wizard H 0 → 140` fixed, re-apply idempotent; surgical
`Mosaic - Cygnus Loop` → 16 panels, 96 cells matched / 80 writes, apply-verify OK. (75 library tests.)
- ~~**Out of scope (later phase):** automated network push of the local copy back to the imaging PC~~ —
  **shipped 2026-07-06** as the sync model's push-as-replay (never a file copy; see the Status digest).
  Creating missing targets (TS-only kept, disk-only deferred) remains a UI-phase item.

## Phase 5 — Consumer cutover

- Point XFM / TP / IS at `Astronomy.Catalog` to read `Catalog.db`; remove XFM's scheduler tab.
- Add TSM to TP's glossary; reconcile the IS design docs (Catalog.db is the hub; IS is a consumer).

> **Plan supersession:** this replaces the earlier "IS owns `scheduler.db`" plan — `Catalog.db` is the hub and
> IS becomes a consumer.
