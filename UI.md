# UI.md — TargetSchedulerManager

**Charter:** the UI design language — the grid's settled look-and-feel, the editing surfaces, chrome, the
WinUI gotchas, and the "when you add a UI element" checklist. Carved out of `DOMAIN.md` 2026-08-03 (the
*charter, not size* test that produced `SUBSYSTEMS.md`); `DOMAIN.md` keeps the domain conventions (*What
TSM is for* + *TS authoring conventions*). **Current state only** — *how we got here* → `ROADMAP.md`; *why
the code is shaped this way* → `ARCHITECTURE.md`. Not a frozen spec; look-and-feel is still
idea→implement→adjust.

> The author is a WinForms expert / WinUI novice — idioms below are flagged against their WinForms analogues
> where it helps.

## The grid idiom

- The home screen is a **flattened, fully-virtualized tree**, not a real `TreeView`: the view-model owns the
  visible-row list (group headers + expanded children), like a WinForms `TreeListView` in VirtualMode.
  (`MainWindow.xaml` `ListView` + `RowTemplateSelector`.) The flatten + in-place splice + expansion-key identity
  live in `ViewModels/VisibleRowTree.cs` — one `ExpandedContent(node)` rule drives the rebuild and every toggle's
  insert *and* remove (so they can't drift); the VM keeps the `ObservableCollection`, filter/sort, and logging.
- **Three row templates**, one `DataTemplateSelector`: `GroupRowTemplate` (target header), `PanelRowTemplate`
  (mosaic-panel mini-header), `FilterRowTemplate` (filter/leaf + nested detail). All share the header's column
  widths so everything lines up.
- **Column headers are hand-rolled** — a separate header `Grid` mirroring the row widths. WinUI's `ListView` has
  no built-in column headers (the one DataGridView feature being reproduced).
- **Collapsed by default.** Whole-row click toggles a group/panel/rollup (no-op on plain leaf rows); chevron
  discloses; Expand/Collapse-all in the toolbar. Expansion is keyed so it survives filter changes + reloads, and
  toggling edits the bound list **in place** so scroll position holds.

## Levels & indentation

Target group → mosaic panel → filter/leaf → nested detail (one source line per sub-length under a mixed rollup).
Indentation steps in per level (`ReconciliationRow.SourceMargin`); panel children shift one extra step.

## Columns (current)

`[mark] · [enable] · Source · Target · Project · Camera · Gain · Offset · Bin · Rot · Filter · Purpose · Seconds · Desired · TS · Actual · Hours · Plans · Badges`

- **Alignment rule: every data column is CENTERED — header, values, and edit boxes alike** (user obs 53c5,
  2026-07-29; replaced the earlier right-aligned-numerics convention). Only the wide text columns
  (Source · Target · Project · Badges) stay left-aligned. Tradeoff accepted: centered counts don't align by
  units digit the way right-aligned ones do; reading columns as centered stacks under their headers won.

- **Camera · Gain · Offset · Bin · Rot** = the **capture configuration** (2026-07-27, openspec
  `capture-config-keys`; Rot 2026-07-29, openspec `rotation-framing-key`).
  Gain/Offset/Bin/Rot are **reconciliation keys** — a row stands apart from its siblings *because* one of them
  differs — so the value responsible is always legible on the row that separated. **Camera is disk-side only**
  (TS cannot name one), so a TS row's camera cell shows the em dash while its gain/offset/bin come from the
  exposure template. On a rollup each cell shows the shared value, or a **`mixed` caution pill** when the rows
  beneath disagree — so a header names the inconsistent dimension *before* you expand it. **A dash never
  counts as disagreement** (user obs 2026-07-29): the em dash means that plane expresses nothing (a TS row's
  camera, an unexpressed rotation), so `mixed` appears only when two *expressed* values differ — a rollup over
  a TS line and Z183 disk lines reads `Z183`, not `mixed`. Camera is searchable
  ("Z533"); the numeric cells are not (a bare number would collide with the count columns). The camera alias
  (183→`Z183`, 533→`Z533`, 178→`Q178`, 144→`A144`) is **display only** — it never enters a key — and a capture
  directory matching none of them shows raw beside a `camera` warning badge.
- **Rot** shows the row's framing rotation **fold-180**: a disk-backed row shows its framing cluster's angle —
  sky plain (`65.1°`), mechanical marked (`172.3°(M)` — user obs 53c5: the bare `m` suffix read as a stray
  character), em dash when frames record neither — and a TS row shows
  the target's own rotation folded, so an agreeing pair reads identically. A disk row whose sky rotation fails
  the plan's carries the warning **`framing` badge** (filter on it to enumerate every stray framing).
  A TS-backed target with **no rotation at all** carries the target-scope warning **`no-rotation` badge**
  on every row (2026-08-04, obs 7c5e — rotation is a required TS-target parameter; the mechanical-only
  adoption case) until the user sets one; distinct from `framing`, which needs a rotation to disagree with.
  A plan whose template carries a camera-default sentinel on **gain or offset** (`-1`) carries the warning
  **`sentinel` badge** on every row using that template (2026-08-04, openspec `pairing-credited-write-back`):
  there the sentinel marks an incorrect state (`DOMAIN.md` → *TS authoring conventions*), the plan can
  never pair or credit while it stands, and TSM never auto-corrects it — the badge says where to hand-fix,
  and disappears on the reconciliation after the template is made explicit. (Sentinels that are a field's
  designed representation of a correct state never badge: template readoutmode `-1`, plan exposure `-1`,
  template `ditherevery` `-1`.)
  **Every row-scoped badge** (`camera` · `cam≠` · `framing` · `sentinel` — `Badges.IsRowScoped`) displays at the
  **deepest visible level** (user rule 2026-07-29): always on the target summary row; on a collapsed rollup
  (the triggering line is hidden inside it); on the triggering line itself once expanded — the rollup then
  hands it down rather than repeating it. Flagging and header aggregation use the full union and are
  unaffected by expansion; target-scope badges are untouched (their trigger IS the whole target). A
  one-frame framing row is intended, not noise — it is the PixInsight reference-frame hazard, quantified by
  its own count. On its deepest visible line the framing badge **prices itself** — `framing 57%`, the share
  of those frames' footprint the plan asked for (2026-07-29, openspec `framing-overlap-column`); rollups and
  summaries keep the bare token (one rollup can span several strays), and the percentage is display-only —
  search, the flagged filter and headers reason over bare `framing`. Overlap facts with no badge (a serving
  framing pointed off-plan; the mixed-sensor qualifier) live in the ambiguity report's Info section.

- **Purpose** prints only the exceptional value: `Light` (the default purpose, near-universal) shows a **blank
  cell**; `Stars` prints (2026-08-05, obs 342a — the repeated `Light` was noise). Display-only
  (`ReconciliationRow.PurposeText`): the underlying `Purpose` still carries `Light` for sort, the visible-row
  tree key, and adoption's enum round-trip.

- **Mark** (column 0, 24 px, unlabeled): the sync-direction mark on every row level — `←` = arrived changed
  from BIRDWATCHER (pull diff, sticky for the session) · `→` = unpushed local writes (journal: manual edits
  *and* write-back stamps) · `⇄` = both · blank = clean. Headers roll up the union of their subtree; a mosaic
  project edit marks the **parent only**, never panels; disk-plane leaves are structurally blank (marks key on
  the plan; target/project changes mark the header). Tooltip: per-field `old → new` on leaves, direction
  counts on headers. Cleared: `→` by Push/Discard; `←` at the next open's pull. (Mechanics:
  `SUBSYSTEMS.md` → *Sync-direction marks*.) The `←`/`→`/`⇄` set is the app's **one sync vocabulary and is
  not grid-only**: the same marks lead every field row of the schema-generated edit dialogs (a blank mark still
  reserves its slot so labels stay aligned) and tag each item in the Templates… picker (2026-07-26, openspec
  `flyout-field-marks` / `template-change-marks`).
- **Desired** = TS goal · **TS** = TS's recorded `acquired` (the count TS schedules on with the grader off) ·
  **Actual** = on-disk frames (ground truth). TS `accepted` is **not** a column — write-back keeps it ==
  acquired; a drift shows as an `acc≠acq` badge. (Full rationale: `ARCHITECTURE.md` → *Grid count columns*.)
- **Source** column shows the row's *plane*: `TS` / `Disk` / `Both`. The `TS` count-header deliberately doubles
  this token (author's call).
- Widths are fixed per column except **Target** (`*`), and live **once** in the ruler (`GridColumns.cs`), stamped
  into the header `Grid` and all three row templates via the `ApplyRuler` attached property (2026-07-24, openspec
  `grid-column-ruler`) — never hand-edited in XAML. Only cell `Grid.Column` placement still spans the four grids,
  so changing a column means renumbering those indexes per template (see the *add a UI element* checklist).

## Sorting

Sort precedence follows the columns **left-to-right**: `Target → Project → Filter → Purpose → Seconds`, **natural
order** on Target/Project (`NaturalComparer` — "IC 405" before "IC 1318", "Abell 6" before "Abell 21";
Purpose compares plain ordinal). **Filter compares by display rank, not alphabetically** (2026-08-05, obs c73e,
openspec `filter-rank-row-order`): the fixed passband order **H · S · O · L · R · G · B**
(`Format.FilterRank` — the single edit point when the filter set changes); codes outside the rank land after B,
natural order among themselves. Project
only separates same-named targets in different projects. Structural keys sit outside the column order: a mosaic's
**panels** stay under their parent, and **plane** is the final tiebreak — **Disk above TS** ("commitments sit
under evidence", same reading as the expanded-rollup rule below).

**Expanded Both rollups** present their source lines in **two blocks** (obs c73e): disk-backed lines first
(pure Disk lines *and* merged Both lines — both are evidence of actuals), plan-only TS lines last, seconds
ascending within each block — the bare commitment always sits under the evidence, even on a seconds tie.

**One deliberate exception** (2026-07-27, openspec `capture-config-keys`): the **capture-configuration columns
(Camera · Gain · Offset · Bin · Rot) sit left of Filter but sort *after* Seconds.** Sorting them in column
position would group every gain-53 row across *all* filters ahead of every gain-0 row, splitting one filter's
rows apart — exactly when you have expanded a target to follow that filter's story. Keeping them late leaves
each filter's rows contiguous, with configuration breaking ties *within* a filter (Rot, 2026-07-29, follows the
same rule and sits outside sort precedence entirely). Full precedence:
`Target → Project → Panel → Filter(rank) → Purpose → Seconds → Gain → Offset → Bin → Camera → Plane(Disk<TS)`. The toolbar
sort picker's other modes (`remaining` / `disk` / `Δ ↓`) are **number-first** with a natural `Target → Project`
tiebreak. `NaturalComparer` is pure-managed (no `shlwapi` P/Invoke).

## em-dash convention

`—` = this row's plane has nothing for that cell (like an empty DataGridView cell). A Disk-only row shows `—`
under Desired/TS → "no TS plan for this exposure"; that signal is load-bearing — keep it. **Exception — Actual
(2026-07-23):** the disk scan is a measurement over everything, so a TS row's Actual is a real `0` (zero frames
captured), never `—`. The convention is asymmetric on purpose: authored plan-side absence = `—` (no plan ≠ a
goal of zero); measured disk-side absence = `0`. **One deliberate conflation rides this convention**
(graduated from `NOTEBOOK.md` 2026-07-07, landed 2026-08-03): `ReconciliationRow` uses `PlanSeconds == 0`
as its own "no seconds" marker, so a TS-only row renders a literal zero-second plan as the em dash — only
plan+disk rows display a literal `0`. Left in place on purpose: the invariant that matters is that the
in-place mirror and the next reload agree on every plane, and they do (checklist item 8; the "exposure 0
is literal" rule itself is single-sourced in `openspec/specs/schema-driven-field-editor/spec.md`). It is
not a bug — don't "fix" it into showing `0`, which would break mirror == reload.

## Visual language

- **Hours is a progress gauge, not a signed sum** (user decision, obs 01b7 2026-07-29 — replaced the
  additive parents-are-the-literal-sum model): while any plan beneath a level still owes images, the level
  shows **−(remaining time)** brown; once nothing is owed — goals met, or no goals at all — it shows the
  **captured disk total** green. "Owed" is **acquired-based** (desired − TS acquired, clamped per plan
  cell): write-back stamps acquired from serving frames only, so the gauge is framing-aware — M81-R with a
  "full" disk of mostly stray frames reads brown, not done. Debt **survives a disable** (Visible-Tonight
  flips `target.active` nightly; progress must not churn with the sky). Deepest lines state plain facts: a
  Disk line its total, a TS line its owed time (dash once complete — its frames live on the disk sibling).
  A parent is deliberately **not** the sum of its children anymore; the `+` surplus prefix died with the
  gap semantics (a positive value is a total, never a surplus). The "Sort: remaining ↓" key uses the same
  acquired basis so sort and gauge never disagree. Tiny non-zero values render F2 so they never read `0.0`
  (`Format.Hours`).
- **Filter wash** (2026-08-05, openspec `filter-colored-rows`): every filter-level row (filter leaf,
  mixed rollup, nested detail line) carries a **low-alpha background band spanning the Camera→Actual
  columns inclusive** (the filter's own story; identity text left of Camera and Hours/Plans/Badges
  right of Actual stay unwashed — user call after first render) in its filter's
  filter hue — O cyan · H crimson · S magenta · B blue · G green · R red
  (`Models/FilterBrushes.cs`, one `WashAlpha` knob, dark-theme tuned; hues are contrast-separated
  from the natural passband colors — at wash alpha luminance vanishes, so neighbors split by hue —
  with R the pure-red anchor: letter-fidelity over passband-fidelity, user call 2026-08-05). **`L` and any off-palette code
  are deliberately plain** — no fallback hue, no warning. Headers and panel mini-headers span filters
  and stay plain. The wash is an **identity layer beneath the state language**: the fills/pills below
  render on top of it, hover chrome reads through it, and it touches no search/flag/sort/key behavior.
  Accepted eyes-open: a `G` row's green wash coexists with green-means-goals-met.
- **Fills** (`ThemeBrushes`): **caution** = time still owed beneath · **success** (green) = nothing owed —
  the value is the captured total · **critical** = data that shouldn't exist (e.g. a desired-0 plan). Disk
  lines stay **plain** — quiet positive facts; green belongs to levels that could owe and don't. Dark-theme
  fills are intentionally subtle (stronger brushes are a one-line swap in `ThemeBrushes.cs`).
- **Pills** (rounded fill behind a cell): Seconds reads **`mixed`** with a caution pill when a rollup spans 2+
  sub-lengths; Hours carries the caution/success fill by sign.
- **Badges** (rightmost, **two-tier color per token** — 2026-07-26, openspec `badge-severity-color`):
  - **warning** (caution foreground) = repairable authoring or frame provenance:
    `duplicate · name≠ · ambiguous · multi-plan · acc≠acq · no-coords · camera · cam≠ · framing` — the tier
    is declared once in `Models\Badges.cs` (`IsWarning`; `IsFlagged` follows it), which wins over any list here
  - **informative** (`ThemeBrushes.Secondary`, quiet) = a fact with no call to action: `mosaic · no data`

  Severity resolves **per token**, so `mosaic · multi-plan` shows one of each rather than promoting the whole
  cell — one `TextBlock` whose `Inlines` are filled by the `Controls/BadgeRuns.Tokens` attached property (a
  `StackPanel` of TextBlocks was rejected: it can't ellipsis-trim). The informative tier uses a dimmed *brush*,
  not the grid's usual `Opacity="0.7"`, because a `Run` is a `TextElement` and **`TextElement` has no
  `Opacity`**; green was rejected because it already means "goal met" in the fills.

  Vocabulary + severity live in `Models\Badges.cs` (the token strings are a soft contract — they're what the
  search box matches); rows are built in `ReconciliationLoader.BuildRows`. Badges **bubble to the header** as a
  **token-level** distinct union (`RowAggregates`). The warning set **is** the `IsFlagged` set driving the
  **flagged-only** filter — colour and filter must never disagree, which is why `no-coords` (a TS target with
  no RA/Dec: unschedulable by TS, can never accrue disk credit) is flagged while `no data` (valid coords, no
  plans, no frames — queued work) is not. (The `alias` badge died with the fold mechanism, 2026-07-23.)

## Alignment & spacing

- **Every item on a line is vertically centered, line by line** — `VerticalAlignment="Center"` **explicitly per
  cell** (not a window-wide implicit style; see *WinUI gotchas*). Add it on every new cell.
- Column alignment is the *Columns* section's centering rule (every data column centered — header, values,
  and edit boxes; wide text columns left); the enable checkbox centers in its 36 px gutter.

## Editing

- **Edit-plus-one-verb — never destructive.** Every TSM editor changes *fields on rows that already exist*,
  with exactly **one structural verb**: the explicit per-row adoption of a disk-only cell (spec
  `disk-row-adoption` — `target` + `exposureplan` inserts, never unasked). TSM never deletes or duplicates
  a TS row at any level (target, project, plan, template); that change is
  TS's own job, done by hand in NINA's TS UI. Two of the 2026-07-06 flyout efforts declared it a Non-Goal
  independently — "no add/delete/duplicate (TS function, user decision)"
  (`openspec/changes/archive/2026-07-06-template-manager/design.md`) and "project add/delete (user rule:
  major surgery is a TS function)" (`…/2026-07-06-project-settings-flyout/design.md`) — and the resolver
  rejection re-confirmed it (2026-07-08). `ruleWeights` is out for the same reason — a one-to-many table, so editing it is TS-side
  surgery, not a per-field commit. Don't re-propose a delete/duplicate verb for the Templates picker or
  any editor. This is the editor-surface form of the membership rule under `DOMAIN.md` → *TS authoring
  conventions* (membership is the user's planning intent), extended to every row kind — templates and
  plans included.
- **In/at the grid only.** A docked dossier panel was built then dropped; `WinUI.TableView` was evaluated and
  rejected (the grid is a hierarchical tree a flat data-grid can't render). **Do not re-litigate.** The edit
  dialog (below) is per-invocation — opened per gesture, centered and movable, not a persistent panel.
- **Direct in-grid controls** (high-frequency scalars): the **target-enable checkbox** (column 1, immediately
  right of the sync-mark gutter; on target headers only — hidden on disk-only + mosaic-parent rows) and **Desired** (a `NumberBox` on 1:1 plan leaf rows;
  read-only on headers, disk rows, and mixed rollups — **each plan is inline-editable in exactly one place**, the
  row showing its own exposure time, so a mixed rollup's box moves down to the plan's detail line (TS or nested
  Both); the rollup keeps its dialog/pencil as the deliberate secondary gesture, 2026-07-23).
- **Edit dialog** (everything else, 2026-07-06; flyout → movable dialog 2026-08-03): a **hover-revealed
  pencil glyph** (Opacity 0→1 via the template-root pointer handlers; `x:Name="EditGlyph"`) and a
  **right-click menu** ("Edit target…" / "Edit exposure plan…", built in code — `Row_RightTapped` — so
  items gate on row data; this menu is the extension point for future row actions — first exercised
  2026-08-03 by the adoption items "Add TS plan…" / "Add to TS…" on eligible disk-only rows, spec
  `disk-row-adoption`: each opens the one **assignment dialog** — project dropdown (locked when the TS
  target exists) + existing-template dropdown (same filter + bin, best match preselected, non-pairing
  caution), no editable plan fields — the plan's counts seed by the pairing verdict (born complete when the
  assigned template pairs with the cell, 0/0/0 under the non-pairing caution — no disk files correspond to
  such a plan) and are adjusted afterward in this editor.
  Since 2026-08-04 (openspec `adopt-target-rollup`) target **rollup** rows carry the bulk grain — "Add to
  TS…" / "Add TS plans…" when ≥1 child cell is eligible → one **combined dialog**: project once, then per
  cell an include checkbox + its own template dropdown + caution (the per-cell controls factored into
  `AssignmentRowControls`, shared with the single-cell dialog so behavior is identical by construction);
  empty-scope cells grey with the reason, the cell list scrolls, Accept = one atomic insert batch).
  The form opens in a **centered `AppDialog`** draggable by
  any non-interactive spot (behavior rides the type — see below; reposition gotcha — flyouts structurally can't
  move, so form-hosting surfaces are dialogs and menus stay flyouts; open-near-the-row seeding retired
  2026-08-03, user call). Both host `Controls/TsFieldsEditor` — a form
  **generated from `TsEditableSchema`** (Bool→ToggleSwitch, Whole/Real→NumberBox clamped to schema Min/Max,
  Enum→ComboBox from `EnumValues`, Text→TextBox; Unit beside, Notes as tooltip; cadence-breaking fields commit
  directly (see the cadence convention below); **Guarded** fields — `rotation` and target `name` (the rename
  verb, 2026-08-12 openspec `add-target-rename` — a rename redirects NINA's future file naming, so it must be
  deliberate) — start disabled behind an arm-to-edit checkbox on their line, re-locked every open; a
  **whitespace-only text commit never reaches the gate** — the box reverts like an out-of-bounds number).
  Values seed fresh from the current db; **each field commits itself** on
  change/focus-loss (so closing the dialog can never lose work — no Apply button, ever); a failed write reverts the
  control. **A committing surface stays interactive** — never disable it to prevent overlap: disabling moves
  focus, which re-fires the `LostFocus` commit re-entrantly (the cure invokes the disease). Commit *ordering*
  is solved by `CommitChain`, never by `IsEnabled`
  (`openspec/changes/archive/2026-07-24-serial-commits/design.md` D3). Fields with a direct in-grid control also appear in the dialog — both paths converge on the same
  setters, so the grid mirrors in place. The exposure-plan dialog also renders a write-through **template
  capture section** (gain/offset/bin): an edit there *is* a template edit — it journals as one, lights marks
  on every plan sharing the template, and the section header carries that blast radius (2026-08-03,
  obs ec6d). **Sentinel columns** (TS stores a reserved −1 meaning "defer to the
  default" — declared per field in `TsEditableSchema` via `Sentinel`/`SentinelLabel`: plan `exposure` →
  template, template `gain`/`offset`/`readoutmode` → camera, template `ditherevery` → project) render as their meaning
  — a "use default (…)" checkbox over the number box, never the raw −1; checked ⇔ the column holds −1 (box
  disabled, showing the resolved value when known), unchecking arms the box (the override commits only when a
  number commits). **The render-as-meaning rule is general** (2026-07-29, user obs: a push-review line read
  "exposure −1 → 600" and the −1 read as an ID): a sentinel never displays raw on ANY surface. Old→new
  displays (push review lines, mark tooltips) route through `TsValueText.ForField`, which shows the schema
  label ("template default", "camera default"); the grid's Gain/Offset cells show the cell-width form
  `default` (`Format.TemplateNumberCell`, schema-driven — no hard-coded −1). Display only, everywhere; the
  journal, replay, and cell keys stay on the canonical value (a deferring plan still pairs with nothing). **Editing `rotation` re-keys rows on the next load** (2026-07-29, openspec
  `rotation-framing-key`): rotation is a reconciliation key, so after a rotation edit the framing pairing
  re-evaluates — clusters that matched may separate (old-framing frames stop serving the re-framed plan) and
  vice versa. The first edit that changes row *identity* rather than a value; designed behavior, not drift.
  **Editing target `name` rides the same close-time trigger** (2026-08-12): not a pairing key but group
  identity (header, sort, name claims, mosaic parent grouping) — no live mirror; the grid shows the old name
  until the dialog closes, then the no-pull re-reconcile moves all of it at once.
- **Mosaics are a special case (user decision 2026-07-06):** a mosaic *parent* row is a grouping node (no TS
  target), so its dialog edits the two whole-mosaic knobs — **"Enable all panels"** (fan-out `target.active`
  to every TS-backed panel; tri-state display when panels disagree; each write individually guarded + audited)
  and **project priority** (one `project.priority` write; per-panel priority overrides survive — mechanism in
  `ARCHITECTURE.md` → *Key facts* / Mosaics). **Panels are normal targets**: standard target
  glyph/dialog on the panel mini-header rows.
- **Cadence-breaking edits write directly - no confirm (user decision 2026-07-07):** plan `enabled` (checkbox
  on 1:1 filter rows + dialog) and project `filterswitchfrequency` (project dialog) commit like any field.
  Safety is structural, not dialog-based — the library clears the invalidated `filtercadenceitem` rows in the
  same transaction as the write (mechanism: `SUBSYSTEMS.md` → *TS write-back* + `ARCHITECTURE.md`'s `TsEditGate` paragraph; the
  cadence-clear scope contract is specced in `openspec/specs/per-filter-enabled-editing`). **Scope caveat:** a hand-authored
  override exposure order blocks only a **target-scope** clear (plan `enabled`); a **project-scope**
  `filterswitchfrequency` edit clears cadence for every target under the project and does **not** check for an
  override order (mirroring TS's own `filterswitchfrequency` behavior). Nothing reaches BIRDWATCHER until the
  reviewed push.
- **Templates are shared config with no rows — so their editor is list-first (user decision 2026-07-06):**
  the toolbar **Templates…** picker lists every template from the loaded graph (name · filter · used-by count,
  zero-use templates included), and plan rows offer "Edit template…" for the template behind that plan. The
  dialog title and the push-review label always state the **blast radius** — "Template '<name>' — used by
  N plan(s)" — because one template edit affects every plan using it. Caution: editing a template's
  `filtername` re-keys its write-back cells at the next resolve (legitimate, but know what it does).
- **Projects are a column, not rows — so their editor is right-click-only (user decision 2026-07-06):**
  "Edit project…" appears in the context menu of any row that resolves a TS project key (target groups,
  panels, plan rows) and opens the schema-generated project dialog; no second hover glyph (the hover-reveal
  is one glyph per row, and the pencil stays the row's own editor). All cadence-safe project fields are
  editable, `state` included — verified against the TS source that state is a plain enum column (no
  date-stamping on transitions). One courtesy rides the commits, **warn-never-block**: when
  `Min time > 2 × Meridian window` (with a window in use), TS would never select the project — TS's own Save
  refuses this pair; TSM's per-field commit instead writes the value and shows a persistent caution in the
  dialog (clears when a commit fixes the pair) plus a status note.
- Edits write to the **local** TS copy through one guarded path (refuse open-sidecar / read-only, read-back
  verify, audit, journal-for-push) and apply **in place** (no grid rebuild — scroll + a half-typed next cell
  survive). Nothing reaches BIRDWATCHER until the reviewed Push.
- **Mirror rule (user-set, 2026-07-06):** any dialog-editable value that is also a visible grid column must
  reflect **immediately on commit** (dialog still open), never waiting for a reload — including header
  re-aggregation. Row **positions hold** even when the edit changes a sort key (order refreshes on the next
  reload/filter pass; rows never jump mid-edit). When a mirror value isn't locally derivable (reverting an
  overridden exposure to the template sentinel), it is **resolved from the db** (plan→template join via
  `ReadPlanEffectiveSecondsAsync`), not left stale. **An in-place mirror addresses the *plan*, never the row
  instance** (obs b4d2, 2026-08-12): one plan renders at several grid levels — a disclosed rollup's summary
  leaf and its TS detail line share `PlanTsKey`, and the edit box lives on exactly one of them while the
  enable checkbox renders on both — so a commit must sweep every rendered instance of that plan by plan key
  and re-aggregate owners (`MirrorPlanEdit` over `_allRows` + detail lines; it covers desired, exposure and
  enable alike). Any future per-plan inline edit routes through it; mirroring only the edited instance leaves
  a collapsed sibling asserting the old value while the data plane holds the new one. **Cell-keying edits
  re-reconcile on editor close**
  (obs 4798, 2026-08-03): plan exposure, template gain/offset/bin/default-exposure/filter/name, target
  rotation, and — since the 2026-08-12 rename verb — target `name` (group identity: header, sort, name
  claims, mosaic parent grouping) re-key reconciliation cells — the mirror holds while the dialog is open, then closing it runs a
  no-pull reload so a merged row never keeps asserting a pairing the edit broke (`IsPairingKey` in
  `MainWindow.Flyouts`).
- **Integer edit boxes are sized to their digit budget.** Real/decimal fields are exempt — they need room
  for the ".". Two cases, two different controls — and the split is **general** (made explicit 2026-08-03,
  user obs 7fc0): **every numeric input inside a dialog or form is a plain editable box with NO spin
  buttons** (grid cells, schema-generated forms, one-off dialog fields);
  visible spinners exist **only** on the toolbar's `UpDownBox` knobs:
  - **No spin buttons** (`NumberBox`, `SpinButtonPlacementMode="Hidden"` — the grid's Desired cell):
    **~3 characters, `Width` ~40 px** (fits 999; ≥ 1000 clips in the box but the full value still
    commits); text **centered in code-behind** via `NarrowNumberBox_Loaded` (a NumberBox can't center via
    XAML, and its template-internal `TextBox` `MinWidth` otherwise overflows a narrow `Width` — see
    *WinUI gotchas*).
  - **Visible spin buttons** (the Visible-Tonight knobs): use **`Controls/UpDownBox`** — our own
    WinForms-style NumericUpDown (TextBox + stacked chevron `RepeatButton`s, integer `Value` clamped to
    `Minimum`/`Maximum`, steps by `SmallChange` via chevrons and ↑/↓ keys, commits on focus-loss/Enter,
    reverts unparseable input). Width = digits + ~32 px chrome: Duration (max 480) `Width="60"`, Floor
    (max 89) `Width="52"`. In XAML set `Minimum`/`Maximum` **before** `Value` (the setter clamps).
    A narrow *inline* `NumberBox` is a **dead end** — decided 2026-07-26 after three failed passes; see
    *WinUI gotchas* for the three hard-coded template widths that make it so. Also rejected: `Compact`
    placement (spinners hidden behind hover).

  The clear (✕) button doesn't appear on these, so there's nothing to suppress.

## TS sync (badge · Push · Pull now)

Design principle: **buttons carry decisions, guards carry facts** — correctness never depends on the user
remembering cross-session state (replaced the LIVE/LOCAL radios 2026-07-06).

- **Sync badge** (toolbar, always visible): `synced yy/MM/dd hh:mm AM|PM · N unpushed` — the last moment
  local == BIRDWATCHER was *proven* (a pull, or an open whose verified skip proved the copy current —
  refreshed 2026-08-02, obs 0cf4) + the collapsed journal count; `BIRDWATCHER offline · …` when the probe
  failed; `never pulled` before a first pull. State is *displayed*, never recalled.
- **Push…** (caution-colored — this is the moment writes reach the rig): enabled exactly when unpushed edits
  exist; opens the review `ContentDialog` — **Creates** first (new TS rows from adoption, `＋ label — new entity:
  summary`), then write-back count stamps (**decreases first**, caution-colored,
  `▼ target · filter @secs — TS old → new`; a desired-only raise shows just `desired old → new`), then
  manual edits (`label — column old → new`), with an InfoBar
  warning when BIRDWATCHER changed since the pull (warn, not block) or an error bar when its db is busy. The
  ellipsis is the dialog convention: Push… always reviews first.
- **Pull now**: the skip-heuristic override; routed through the dirty guard (unpushed edits prompt first).
- **Open-with-dirty dialog**: push (default) / discard-and-pull / not-now — shown BEFORE any pull can overwrite
  local edits; same review body as Push….
- **Reload (rescan)** keeps meaning "rescan disk + re-read local" — it never pulls.

## Chrome

- **Toolbar:** Reload (rescan) · progress ring · Cancel (shown while any cancellable phase runs) · sync badge · Push… ·
  Pull now · Templates… · Ambiguities… · **Visible Tonight:** (Project dropdown + Duration + Floor up-downs +
  Set). (The old toolbar load-summary text was removed 2026-07-23 when the Visible-Tonight group replaced
  it.) The Project dropdown (openspec `project-scoped-tonight`, 2026-08-05) defaults to **All projects**;
  selecting a project **fills** Duration/Floor from its TS `minimumtime`/`minimumaltitude` (a read — the boxes
  are a viewport, switching selections refills over edits), and **Set is the only write gesture** (the button, relabeled from Tonight 2026-08-05): changed
  values journal onto the project before the scoped enable pass runs. Knob ranges are the TS schema's
  (Duration 0–999 whole minutes; Floor 0–89.9° with tenths — `UpDownBox` `DecimalPlaces=1`; TS asserts a
  minimum altitude below 90), so a fill never silently clamps a stored value. A landed Floor write also
  rewrites an existing trailing "- N" altitude clause in the project name (legacy "- Above N" migrates;
  never invented, never on a refused write).
  Mechanics → `SUBSYSTEMS.md` → *Visible-tonight pass*. Ambiguities…
  (enabled once a load exists) writes a dated printable Markdown report of every
  TS/disk ambiguity — what · why · the hand fix in NINA's TS UI — to `%APPDATA%\TargetSchedulerManager\Reports\`
  and opens it; the status line carries `· N ambiguities` when the tripwire is non-zero. The report speaks
  **NINA's TS-UI vocabulary**, since that is where the fix happens: plans are named by *template name* and
  targets as `project › target`, **never by plan Id**. (This reverses the archived design's D8, which had
  preferred Ids — `openspec/changes/archive/2026-07-23-ts-ambiguity-report/design.md`; the implementation won.
  Ids appear only where the name itself is the defect, e.g. duplicate template names.)
- **Cancel is phase-scoped, and deliberately so** (2026-07-26). One button covers a whole load because the
  phases run in sequence, but it cancels *whichever phase is in flight* rather than the load as a whole —
  cancelling a **pull** still lets the load continue on the intact local copy (the discard path depends on
  it: a cancelled discard-pull must change nothing), while cancelling the **scan/resolve** ends the load and
  the grid keeps the rows it was already showing (`load cancelled — showing the previous scan`). A cancel is
  never reported as a failure and never blanks the grid. Write-back and push replay stay uncancellable: they
  write.
- **Filter bar:** search (target / project / filter) · source filter · flagged-only · sort picker ·
  Expand/Collapse all.
- **Status bar:** library path + sync/write-back notes + load time.
- **Window title = app name + version, nothing else** (2026-08-02). It carries the MinVer informational
  version and no sync-status suffix (the old one was removed when the sync badge took that job — status
  belongs to the badge and the status bar, which update live; a title doesn't). The version is there to
  answer *which build am I looking at* — the dev-vs-installed disambiguator once the Velopack installer
  ships side-by-side with a dev run.
- **Ctrl+N** opens the Diagnostics window (notes + screenshot into `tsm.log`); the floating accelerator
  hover-hint is suppressed. **Capture in 5 s** hides the window for the countdown so transient light-dismiss UI
  (context menus, pickers) can be opened and survives into the shot — plain Capture can never contain one
  (focus shift dismisses it). Edit dialogs need no countdown: every dialog carries its own Ctrl+N
  (`Controls/AppDialog` wires `PreviewKeyDown` in its constructor — on the type, not the show-site).

## WinUI gotchas (and the workarounds)

Platform landmines we've hit — they're *why* some of the rules above look the way they do. (The author runs and
screenshots the app to confirm visual fixes; the build only proves the code compiles.)

- **Implicit `TextBlock` styles apply unevenly inside a `ListView` `DataTemplate`** and leak into control
  internals — so vertical centering is set **explicitly per cell**, not via a window-wide implicit style. (Tried
  the implicit route 2026-06-20; it produced uneven columns and was reverted.)
- **Flyouts can't be repositioned — at all. Host forms in dialogs instead** (settled 2026-08-03 after
  three field-verified failures): a flyout renders in its own top-level popup window whose placement the
  `Flyout` owns — translating the content slid it inside a stationary frame, translating the
  `FlyoutPresenter` pinned it against its own window's bounds, and writing the owning `Popup`'s
  `Horizontal/VerticalOffset` didn't stick either (the flyout's positioning logic owns them). A
  **`ContentDialog` is its own chrome in the main tree**, where `Controls/DragMove.Attach`'s
  `TranslateTransform` works: drag by any **non-interactive** spot (buttons/inputs swallow
  `PointerPressed` first), deltas read in *window* space (element-relative coords self-feed).
  Consequence: every form-hosting surface is a dialog; `MenuFlyout`s
  (context menus, pickers) stay flyouts — transient, movability meaningless.
- **Dialogs always open centered — never seed the open position from an anchor** (user call 2026-08-03
  after two field failures, obs 3eba). The `ContentDialog` element is a full-window overlay; the visible
  box is the centered template child `BackgroundElement` (generic.xaml: `Container` → smoke `LayoutRoot`
  → box). Translating that overlay against a clicked-row anchor races layout: with the overlay measured
  the clamp collapses and seeding no-ops, and with `Opened` outrunning layout (`ActualWidth` 0) the box
  lands **off-screen** — an invisible modal that eats all input and reads as a UI hang; a box-based
  reseed still opened "almost off screen." Centered + drag is the whole model.
- **A lone dialog button centers — and app dialogs are `Controls/AppDialog`, never raw `ContentDialog`**
  (user 2026-08-05, obs f4d0 + c200). The template's CommandSpace is five columns
  (`Primary(*) · spacer · Secondary(0) · spacer · Close(*)`, buttons stretched), so a single visible
  button fills its half-width column and reads off-center; 2–3 buttons fill the row symmetrically and
  are left alone. The repair lives in `AppDialog.OnApplyTemplate` via `GetTemplateChild` (the template-
  part contract). **Gotcha that forced the type:** an `Opened`-time visual-tree walk from the dialog
  element finds *nothing* — not even one dispatcher tick later (field-failed twice, obs c200's
  DIAG/Dialog tripwire) — so never reach into a `ContentDialog`'s tree from outside; subclass and use
  `GetTemplateChild`. A DIAG/Dialog line trips if the template part ever goes missing.
- **`KeyboardAccelerator`s are dead inside a `ContentDialog`** — the window-level one never sees the key
  (focus lives in the dialog's popup tree), and one attached to the dialog itself is *ignored entirely*
  (the dialog's inner popup doesn't participate in accelerator collection — microsoft-ui-xaml
  [#2408](https://github.com/microsoft/microsoft-ui-xaml/issues/2408) family; field-verified 2026-08-03,
  the accelerator variant shipped and did nothing). Workaround: `PreviewKeyDown` on the dialog (tunnels
  before its children), modifier state via `InputKeyboardSource.GetKeyStateForCurrentThread` — wired
  once in `Controls/AppDialog`'s constructor against the static `DiagnosticsHook` the window sets at
  startup (openspec `dialog-behaviors-on-type`: behaviors ride the type, so even dialogs shown outside
  `ShowDialogAsync` — the update prompt — get it; the funnel itself is a thin await seam typed
  `AppDialog`, so a raw `ContentDialog` cannot compile its way in). Flyouts/context menus
  are popups too and may need the same treatment if a shortcut goes dead there.
- **A `NumberBox` can't center its text via XAML.** `TextAlignment` doesn't reach its template-internal TextBox
  (microsoft-ui-xaml [#7399](https://github.com/microsoft/microsoft-ui-xaml/issues/7399) /
  [#2896](https://github.com/microsoft/microsoft-ui-xaml/issues/2896)). Workaround: one shared `Loaded` handler
  (`NarrowNumberBox_Loaded`, used by the grid's Desired cell — its only call site; the toolbar knobs are
  `Controls/UpDownBox` and need no repair)
  walks to the inner `TextBox` and sets `TextAlignment=Center` on the instance, trimming its `Padding`/`MinWidth`
  so digits fit a narrow box. **Zeroing that `MinWidth` is what makes a narrow `Width` take effect at all.**
  The same handler zeroes the inner `MinHeight` (theme floor **32 px** > the grid's 30 px row minimum — editor
  rows read taller than their neighbours; 2026-08-05, obs 342a). Height, unlike the widths below, IS reachable
  per-instance: the template's `BorderElement` template-binds `MinHeight`, so the local value propagates.
- **A narrow inline-spinner `NumberBox` is unreachable — use `Controls/UpDownBox` instead.** Decided
  2026-07-26 after three failed visual passes against three hard-coded template widths (WASDK 2.2's
  `generic.xaml`): the input's forced **120 px** `MinWidth` (the `SpinButtonsVisible` state), the
  **76 px** chevron pair in the outer template (2 × MinWidth 32 + 4 px margins), and a **72 px**
  `SpinButtonsColumn` reserved for the text inside the *inner* TextBox's own template — a constant, so
  shrinking the actual buttons reclaims nothing, and any box narrower than ~72 + digits starves the text
  column. Defeating all three means per-instance surgery on two nested templates (app-resource style
  shadowing can't work: a `StaticResource` inside a framework `ControlTemplate` resolves within
  `generic.xaml`, not `Application.Resources`). `UpDownBox` (~100 lines, zero template reach) is the
  answer whenever visible spinners must be narrow.
- **Grid column widths can't live in a `ResourceDictionary`** — there is no implicit `double`→`GridLength`
  conversion on resource assignment, and `ColumnDefinition` instances can't be shared across grids. That is
  why the one ruler is an **attached property** (`GridColumns.ApplyRuler`) that stamps definitions in its
  callback, before children lay out; a template that forgets the attribute fails *loudly* — every cell
  collapses into column 0 — rather than drifting silently
  (`openspec/changes/archive/2026-07-24-grid-column-ruler/design.md` D1).
- **Members shared across row templates go on an abstract base class, not an interface** — `x:Bind` resolves
  members against each template's concrete `x:DataType`, so the idiomatic C# choice compiles and then fails at
  bind time (hence `AggregateHeaderRow`, 2026-06-26).
- **`x:Bind` inside a `DataTemplate` scopes to the row item**, not the page ViewModel — so VM-driven row state
  must bind at the `ListView` level (how busy-disable works) or via a per-row INPC property swept on each
  transition (priced and deliberately deferred:
  `openspec/changes/archive/2026-07-24-busy-gate/design.md` D2).
- **Imperatively-built cell content must be recycle-safe** — clear and rebuild on every value change, because
  the grid is a virtualized `ListView` that reuses containers, so a build-once cell goes stale on scroll
  (`openspec/changes/archive/2026-07-26-badge-severity-color/design.md`). `BadgeRuns.Tokens` is the second
  instance of the attached-property pattern above: that is how row **state or content** is reached, never
  `MainWindow` code-behind. The one exception is `NarrowNumberBox_Loaded` — a per-instance *visual* repair for
  a framework template defect (above), not a content or state write.
- **`NumberBox`/`TextBox` vertical centering** breaks under a fixed `Height` (the inner ScrollViewer top-aligns).
  Give the box **no fixed `Height`** (let it auto-size; center the box with `VerticalAlignment`) — or template the
  `ContentElement` ScrollViewer to `VerticalAlignment=Center`.
- **A row template's root `Background="Transparent"` is hit-test surface, not decoration** — it is what makes
  a row's empty cells raise the pointer events that reveal the edit glyph, so **never repaint the template
  root to carry a full-row visual**. Any row-spanning fill (the filter wash; a future banding or highlight)
  goes in a **`Border` underlay placed behind the cells and spanning the intended columns**, leaving the root
  transparent (2026-08-05, `openspec/changes/archive/2026-08-05-filter-colored-rows/design.md` D4). Painting
  the root instead kills hover on every empty cell in the row — a silent editability regression, not a
  visual one.

## When you add a UI element — checklist

1. New **column**? Add it to the ONE ruler (`GridColumns.cs` — name + width; table position = column
   index), then place the cell in each row template and the header caption (cell `Grid.Column` indexes
   stay per-template; renumber those that shifted). The four grids stamp their `ColumnDefinitions` from
   the ruler (2026-07-24, openspec `grid-column-ruler`) — never hand-edit widths in XAML. Its sort slot
   follows its header position (left-to-right, per *Sorting*) **unless it belongs to the capture-configuration
   block**, whose documented exception sorts after Seconds. If the column is a **reconciliation key** (rows
   separate on it), it must be legible on the rows that separated and must render `mixed` on a rollup whose
   children disagree — a separation the reader cannot explain is worse than no column.
2. New **cell**? Add `VerticalAlignment="Center"`. Center it (header, value, and any edit box) per the
   Columns alignment rule. Text conventions come from
   `Models\Format.cs` — the one home (2026-07-24, `presentation-conventions`): `Format.Dash`/`CountOrDash`
   for empty cells (— means "nothing to say"; a measured 0 renders 0), `Format.Hours`, `Format.When`,
   `Format.Cell` ("H @900s"), `Format.Label` ("target · filter" — journal-persisted, shape is contract).
   Code-side brush lookups go through `ThemeBrushes` (app root; defensive null-on-missing), never raw
   `Application.Current.Resources` casts. Editor numeric inputs come from `TsFieldsEditor.MakeNumberBox`.
3. New **state worth flagging**? Add the token to `Models\Badges.cs` (const + its tier in `IsWarning`) and
   emit it in `BuildRows`. Decide the **severity tier**, and set `IsFlagged` to match it — warning tier and
   `IsFlagged` are one set, so a warning-coloured row is never hidden by the flagged-only filter. Confirm it
   bubbles via `RowAggregates` (token-level distinct). Also decide its **scope**: most tokens describe a whole
   target and mark every one of its rows, but a token describing particular *frames* (the camera-provenance
   pair) marks only the rows drawing on them and reaches the collapsed view through the ordinary rollup —
   never by spreading to siblings.
4. New **fill / color**? **State** color comes from `ThemeBrushes` (caution / success / critical fills;
   `CautionText` / `Secondary` foregrounds); **filter-identity** color from `Models/FilterBrushes.cs`
   (domain passband constants — the one sanctioned hard-coded palette). Never hard-code elsewhere. Note
   `Run`/`Inline` foregrounds can't use `Opacity`; reach for `Secondary`.
5. New **count / number**? Decide its plane (TS / Disk / Both) and show `—` when the plane is empty; center it.
6. New **integer edit box**? Digit-budget width. In-grid, no spinners → `NumberBox` + `Loaded="NarrowNumberBox_Loaded"` (centers digits, lets a narrow `Width` stick). Visible spinners → `Controls/UpDownBox` (never a narrow inline `NumberBox` — see *WinUI gotchas*).
7. Touching **look-and-feel**? The build verifies code; **visual correctness is the author's call** — they
   run/screenshot the app (don't do it unprompted).
8. New **editable field whose value shows in a grid column**? Wire its in-place mirror (an `Apply*` on the row
   + owner re-aggregation) — the mirror rule above is a hard convention, not polish. The invariant it promises
   is **mirror == reload**, not "the committed value renders literally": `ReconciliationRow.PlanSeconds` uses 0
   as its own no-seconds marker, so a committed exposure `0` shows `—` on a plan-only row both on commit and
   after reload (`openspec/changes/archive/2026-07-07-exposure-zero-literal/design.md` D5).
9. New **handler or binding**? One-line forward to the view-model, `x:Bind` only, and every awaiting handler
   through `UiTask.FireAndLog` — the full seam rules (and their one documented exception) live in
   **`CONVENTIONS.md`** → *The view/view-model seam* / *Async and the UI thread*.
