# DOMAIN.md — TargetSchedulerManager

**Charter:** the human/strategy home — the TS-management domain + UX/design language + conventions that fit
neither `ARCHITECTURE.md` (how it works) nor `ROADMAP.md` (what's next). Today its main content is the
**UI conventions** (the grid's settled look-and-feel + the "when you add a UI element" checklist); other
domain/strategy notes accrue here. **Current state only** — *how we got here* → `ROADMAP.md`; *why the code
is shaped this way* → `ARCHITECTURE.md`. Not a frozen spec; look-and-feel is still idea→implement→adjust
(reflects the grid as of the ambiguity-report work, 2026-07-08).

> The author is a WinForms expert / WinUI novice — idioms below are flagged against their WinForms analogues
> where it helps.

## What TSM is for (the two purposes)

TSM serves **two** purposes, and nearly every design question resolves by asking which one is in play:

1. **Modify the TS database on BIRDWATCHER** (primary) — the plan you will image against.
2. **Display the entire imaging history** (secondary) — what you actually captured, in full fidelity.

Four standing truths follow, and they settle most arguments about the grid:

- **Disk is actual history.** Self-describing, immutable, and *never validated against TS*. A frame captured at
  gain 53 in 2019 is not "failing to match" anything — it is simply what happened. The disk plane's job is
  fidelity, not agreement.
- **TS is the future.** An exposure template describes imaging *going forward*. When one happens to match disk
  files that is a coincidence worth showing, not a correctness condition. Nothing is "broken" when they differ.
- **A dimension can pair the two planes only if both express it.** The disk plane may key more finely than TS —
  that is a consequence of purpose 2, not a defect. Where the planes disagree the grid separates them, and the
  separation *is* the answer to "why don't these numbers add up".
- **`desired` is camera-agnostic.** One NINA profile is used with cameras exchanged between sessions, so TS
  cannot express which camera a plan is for. **TSM must never model camera↔plan attribution** — never apportion
  a desired count between cameras, never infer how many frames a given camera still owes. You decide that at the
  telescope, and automating it would hard-code complexity that is trivially resolved in the moment.

- **A 180° pier flip is the same framing.** A rectangle rotated 180° about the same center covers the
  identical footprint, so flip frames integrate perfectly — flips are routine acquisition events and must
  never split a row or read as a mismatch. That is why every rotation comparison in TSM is **fold-180**
  (measured 2026-07-29: every real flip pair's centroids coincide within 0.12°).
- **Mechanical rotator angle is never converted to a sky angle.** The mech-to-sky zero point shifts when the
  camera is remounted (measured drifting 19–35° across sessions, precisely on the multi-framing targets), so
  a conversion would silently mislabel the exact rows the framing key exists to expose. Mechanical rotation
  is shown marked (`°m`), clusters frames disk-side, and never enters the plan comparison.
- **TS's `acquired` counts only frames that serve the plan's framing** (user decision 2026-07-29, after the
  Tulip confusion: TS said 32/80 acquired while zero captured frames matched the re-framed 160° plan).
  Write-back credits by the same serving rule the pairing test uses, so a re-framed target stamps its true
  progress — possibly 0 — and TS schedules the full re-shoot. The grid's TS column and Actual column can
  therefore only diverge transiently (until the next load's write-back stamps); a persistent gap means the
  push hasn't run, not a contradiction.
- **TSM detects framing hazards; WBPP enforces; XFM neither.** A low-count off-footprint framing cluster is
  a PixInsight reference-frame hazard (a good stray reference makes ImageIntegration work a shrunken
  overlap). TSM's job ends at making it visible (the split row + `framing` badge); any grouping/exclusion
  rule belongs in the WBPP lane (PJSR reads `OBJCTROT`/`POSANGLE` itself — zero AL coupling), and XFM's
  grading role gives it no say at all.

(Landed 2026-07-27 with openspec `capture-config-keys` — gain/offset/binning reconciliation keys, camera a
disk-side label — and 2026-07-29 with openspec `rotation-framing-key` — framing (fold-180 sky rotation +
cluster centroid) as a key, per the same-day measurement spike over 18,650 frames. Telescope remains
deferred — see `ROADMAP.md`.)

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
  sky plain (`65.1°`), mechanical marked (`172.3°m`), em dash when frames record neither — and a TS row shows
  the target's own rotation folded, so an agreeing pair reads identically. A disk row whose sky rotation fails
  the plan's carries the warning **`framing` badge** (filter on it to enumerate every stray framing).
  **Every row-scoped badge** (`camera` · `cam≠` · `framing` — `Badges.IsRowScoped`) displays at the
  **deepest visible level** (user rule 2026-07-29): always on the target summary row; on a collapsed rollup
  (the triggering line is hidden inside it); on the triggering line itself once expanded — the rollup then
  hands it down rather than repeating it. Flagging and header aggregation use the full union and are
  unaffected by expansion; target-scope badges are untouched (their trigger IS the whole target). A
  one-frame framing row is intended, not noise — it is the PixInsight reference-frame hazard, quantified by
  its own count.

- **Mark** (column 0, 24 px, unlabeled): the sync-direction mark on every row level — `←` = arrived changed
  from BIRDWATCHER (pull diff, sticky for the session) · `→` = unpushed local writes (journal: manual edits
  *and* write-back stamps) · `⇄` = both · blank = clean. Headers roll up the union of their subtree; a mosaic
  project edit marks the **parent only**, never panels; disk-plane leaves are structurally blank (marks key on
  the plan; target/project changes mark the header). Tooltip: per-field `old → new` on leaves, direction
  counts on headers. Cleared: `→` by Push/Discard; `←` at the next open's pull. (Mechanics:
  `SUBSYSTEMS.md` → *Sync-direction marks*.) The `←`/`→`/`⇄` set is the app's **one sync vocabulary and is
  not grid-only**: the same marks lead every field row of the schema-generated flyouts (a blank mark still
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
order** on Target/Project/Filter (`NaturalComparer` — "IC 405" before "IC 1318", "Abell 6" before "Abell 21";
Purpose compares plain ordinal). Project
only separates same-named targets in different projects. Structural keys sit outside the column order: a mosaic's
**panels** stay under their parent, and **plane** (TS above Disk) is the final tiebreak within a cell.

**One deliberate exception** (2026-07-27, openspec `capture-config-keys`): the **capture-configuration columns
(Camera · Gain · Offset · Bin · Rot) sit left of Filter but sort *after* Seconds.** Sorting them in column
position would group every gain-53 row across *all* filters ahead of every gain-0 row, splitting one filter's
rows apart — exactly when you have expanded a target to follow that filter's story. Keeping them late leaves
each filter's rows contiguous, with configuration breaking ties *within* a filter (Rot, 2026-07-29, follows the
same rule and sits outside sort precedence entirely). Full precedence:
`Target → Project → Panel → Filter → Purpose → Seconds → Gain → Offset → Bin → Camera → Plane`. The toolbar
sort picker's other modes (`remaining` / `disk` / `Δ ↓`) are **number-first** with a natural `Target → Project`
tiebreak. `NaturalComparer` is pure-managed (no `shlwapi` P/Invoke).

## em-dash convention

`—` = this row's plane has nothing for that cell (like an empty DataGridView cell). A Disk-only row shows `—`
under Desired/TS → "no TS plan for this exposure"; that signal is load-bearing — keep it. **Exception — Actual
(2026-07-23):** the disk scan is a measurement over everything, so a TS row's Actual is a real `0` (zero frames
captured), never `—`. The convention is asymmetric on purpose: authored plan-side absence = `—` (no plan ≠ a
goal of zero); measured disk-side absence = `0`.

## Visual language

- **Signed Hours (additive):** every row's Hours is its signed contribution to its parent's total, so a parent
  is the literal sum of its children. TS rows show **−(desired×sec)** (deficit), Disk rows **+(frames×sec)**,
  Both rollups the **disk−desired gap**. Tiny non-zero values render F2 so they never read `0.0`
  (`Format.Hours`); a positive Both gap is prefixed `+`.
- **Fills** (`ThemeBrushes`): **caution** = needs telescope time / outstanding commitment · **success** (green)
  = goal met · **critical** = data that shouldn't exist (e.g. a desired-0 plan). Disk lines stay **plain** —
  quiet positive facts. Dark-theme fills are intentionally subtle (stronger brushes are a one-line swap in
  `ThemeBrushes.cs`).
- **Pills** (rounded fill behind a cell): Seconds reads **`mixed`** with a caution pill when a rollup spans 2+
  sub-lengths; Hours carries the caution/success fill by sign.
- **Badges** (rightmost, **two-tier color per token** — 2026-07-26, openspec `badge-severity-color`):
  - **warning** (caution foreground) = repairable authoring, fix it by hand in NINA's TS UI:
    `duplicate · name≠ · ambiguous · multi-plan · acc≠acq · no-coords`
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
- Numeric **columns** right-align (`TextAlignment="Right"`); text columns left; the enable checkbox centers in its
  36 px gutter. (Numeric **edit boxes** center — see *Editing*.)

## Editing

- **Edit-only — never structural.** Every TSM editor changes *fields on rows that already exist*; it never
  adds, deletes, or duplicates a TS row at any level (target, project, plan, template). Structural change is
  TS's own job, done by hand in NINA's TS UI. Two of the 2026-07-06 flyout efforts declared it a Non-Goal
  independently — "no add/delete/duplicate (TS function, user decision)"
  (`openspec/changes/archive/2026-07-06-template-manager/design.md`) and "project add/delete (user rule:
  major surgery is a TS function)" (`…/2026-07-06-project-settings-flyout/design.md`) — and the resolver
  rejection re-confirmed it (2026-07-08). `ruleWeights` is out for the same reason — a one-to-many table, so editing it is TS-side
  surgery, not a per-field commit. Don't re-propose an add/delete/duplicate verb for the Templates picker or
  any flyout. This is the editor-surface form of the membership rule under *TS authoring conventions* below
  (membership is the user's planning intent), extended to every row kind — templates and plans included.
- **In/at the grid only.** A docked dossier panel was built then dropped; `WinUI.TableView` was evaluated and
  rejected (the grid is a hierarchical tree a flat data-grid can't render). **Do not re-litigate.** The edit
  flyout (below) is per-invocation and anchored to the clicked row — a popup answering one gesture, not a
  persistent panel.
- **Direct in-grid controls** (high-frequency scalars): the **target-enable checkbox** (column 1, immediately
  right of the sync-mark gutter; on target headers only — hidden on disk-only + mosaic-parent rows) and **Desired** (a `NumberBox` on 1:1 plan leaf rows;
  read-only on headers, disk rows, and mixed rollups — **each plan is inline-editable in exactly one place**, the
  row showing its own exposure time, so a mixed rollup's box moves down to the plan's detail line (TS or nested
  Both); the rollup keeps its flyout/pencil as the deliberate secondary gesture, 2026-07-23).
- **Edit flyout** (everything else, 2026-07-06): a **hover-revealed pencil glyph** (Opacity 0→1 via the
  template-root pointer handlers; `x:Name="EditGlyph"`) and a **right-click menu** ("Edit target…" / "Edit
  exposure plan…", built in code — `Row_RightTapped` — so items gate on row data; this menu is the extension
  point for future row actions). Both open a row-anchored `Flyout` hosting `Controls/TsFieldsEditor` — a form
  **generated from `TsEditableSchema`** (Bool→ToggleSwitch, Whole/Real→NumberBox clamped to schema Min/Max,
  Enum→ComboBox from `EnumValues`, Text→TextBox; Unit beside, Notes as tooltip; cadence-breaking fields commit
  directly (see the cadence convention below); **Guarded** fields — `rotation` — start disabled behind an arm-to-edit checkbox on their line, re-locked every open). Values seed fresh from the current db; **each field commits itself** on
  change/focus-loss (so light-dismiss can never lose work — no Apply button, ever); a failed write reverts the
  control. **A committing surface stays interactive** — never disable it to prevent overlap: disabling moves
  focus, which re-fires the `LostFocus` commit re-entrantly (the cure invokes the disease). Commit *ordering*
  is solved by `CommitChain`, never by `IsEnabled`
  (`openspec/changes/archive/2026-07-24-serial-commits/design.md` D3). Fields with a direct in-grid control also appear in the flyout — both paths converge on the same
  setters, so the grid mirrors in place. **Sentinel columns** (TS stores a reserved −1 meaning "defer to the
  default": plan `exposure` → template, template `gain`/`offset`/`readoutmode` → camera) render as their meaning
  — a "use default (…)" checkbox over the number box, never the raw −1; checked ⇔ the column holds −1 (box
  disabled, showing the resolved value when known), unchecking arms the box (the override commits only when a
  number commits). **Editing `rotation` re-keys rows on the next load** (2026-07-29, openspec
  `rotation-framing-key`): rotation is a reconciliation key, so after a rotation edit the framing pairing
  re-evaluates — clusters that matched may separate (old-framing frames stop serving the re-framed plan) and
  vice versa. The first edit that changes row *identity* rather than a value; designed behavior, not drift.
- **Mosaics are a special case (user decision 2026-07-06):** a mosaic *parent* row is a grouping node (no TS
  target), so its flyout edits the two whole-mosaic knobs — **"Enable all panels"** (fan-out `target.active`
  to every TS-backed panel; tri-state display when panels disagree; each write individually guarded + audited)
  and **project priority** (one `project.priority` write; per-panel priority overrides survive — mechanism in
  `ARCHITECTURE.md` → *Key facts* / Mosaics). **Panels are normal targets**: standard target
  glyph/flyout on the panel mini-header rows.
- **Cadence-breaking edits write directly - no confirm (user decision 2026-07-07):** plan `enabled` (checkbox
  on 1:1 filter rows + flyout) and project `filterswitchfrequency` (project flyout) commit like any field.
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
  flyout title and the push-review label always state the **blast radius** — "Template '<name>' — used by
  N plan(s)" — because one template edit affects every plan using it. Caution: editing a template's
  `filtername` re-keys its write-back cells at the next resolve (legitimate, but know what it does).
- **Projects are a column, not rows — so their editor is right-click-only (user decision 2026-07-06):**
  "Edit project…" appears in the context menu of any row that resolves a TS project key (target groups,
  panels, plan rows) and opens the schema-generated project flyout; no second hover glyph (the hover-reveal
  is one glyph per row, and the pencil stays the row's own editor). All cadence-safe project fields are
  editable, `state` included — verified against the TS source that state is a plain enum column (no
  date-stamping on transitions). One courtesy rides the commits, **warn-never-block**: when
  `Min time > 2 × Meridian window` (with a window in use), TS would never select the project — TS's own Save
  refuses this pair; TSM's per-field commit instead writes the value and shows a persistent caution in the
  flyout (clears when a commit fixes the pair) plus a status note.
- Edits write to the **local** TS copy through one guarded path (refuse open-sidecar / read-only, read-back
  verify, audit, journal-for-push) and apply **in place** (no grid rebuild — scroll + a half-typed next cell
  survive). Nothing reaches BIRDWATCHER until the reviewed Push.
- **Mirror rule (user-set, 2026-07-06):** any flyout-editable value that is also a visible grid column must
  reflect **immediately on commit** (flyout still open), never waiting for a reload — including header
  re-aggregation. Row **positions hold** even when the edit changes a sort key (order refreshes on the next
  reload/filter pass; rows never jump mid-edit). When a mirror value isn't locally derivable (reverting an
  overridden exposure to the template sentinel), it is **resolved from the db** (plan→template join via
  `ReadPlanEffectiveSecondsAsync`), not left stale.
- **Integer edit boxes are sized to their digit budget.** Real/decimal fields are exempt — they need room
  for the ".". Two cases, two different controls:
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

- **Sync badge** (toolbar, always visible): `synced HH:mm · N unpushed` — last pull/push time + the collapsed
  journal count; `BIRDWATCHER offline · …` when the probe failed; `never pulled` before a first pull. State is
  *displayed*, never recalled.
- **Push…** (caution-colored — this is the moment writes reach the rig): enabled exactly when unpushed edits
  exist; opens the review `ContentDialog` — write-back count stamps first (**decreases first**, caution-colored,
  `▼ target · filter @secs — TS old → new`), then manual edits (`label — column old → new`), with an InfoBar
  warning when BIRDWATCHER changed since the pull (warn, not block) or an error bar when its db is busy. The
  ellipsis is the dialog convention: Push… always reviews first.
- **Pull now**: the skip-heuristic override; routed through the dirty guard (unpushed edits prompt first).
- **Open-with-dirty dialog**: push (default) / discard-and-pull / not-now — shown BEFORE any pull can overwrite
  local edits; same review body as Push….
- **Reload (rescan)** keeps meaning "rescan disk + re-read local" — it never pulls.

## TS authoring conventions (user-side; decided 2026-07-08)

The charter behind all of them: **TS is a picker** — given a menu of targets and conditions, choose, order,
shoot; everything else in TS is noise here. So: TS's *membership* (which targets/projects exist) is the user's
planning intent — TSM never adds or removes it unasked; TS's *facts about members* (names, counts) mirror disk.

- **One TS row per sky position, no exceptions** (within the 0.5° match tolerance), spelled the same in TS
  and as the disk directory's catalog token (`IC 1795`, not `FishHead` — name validation is token-based;
  concatenations fail). There is **no alias escape**: the fold mechanism (deliberate second names for one
  object auto-resolving unflagged) was removed 2026-07-23 — its sole instance (M27 + Dumbell) was adjudicated
  2026-07-08 as never intentional ("explained ≠ approved"; NOTEBOOK correction), and any multi-claim now
  surfaces as a flagged duplicate to consolidate by hand.
- **Explained ≠ approved — a structurally odd state must surface, even when the numbers reconcile.** The
  standing lesson behind the alias removal, and a forward constraint on anything that resolves ambiguity: a
  mechanism must never quietly auto-fold a multi-claim just because the totals still add up. The fold read as
  benign for weeks and was in fact an unintended twin the user wanted raised
  (NOTEBOOK.md 2026-07-08 late: *"this was not intentional and should be brought to my attention"*). Surface
  it for the decision; don't decide for them.
- **One exposure plan per (filter, purpose, whole-second exposure) per target.** Same filter at *different*
  seconds is fine (different cells, auto-resolve); a same-key second plan makes disk-credit undecidable.
- Under these two conventions the write-back manual tray is provably empty; a non-zero tray means a convention
  slipped. **Fixes happen by hand in NINA's TS UI on BIRDWATCHER** — TSM surfaces ambiguities (report/badges)
  but has no structural edit verbs (resolver rejected 2026-07-08; see
  `docs/2026-07-08-resolver-rejection-isp-lane.md` for why). `desired` is likewise user-owned planning
  intent — never derived from disk (TSM's grid does edit the value; see Editing).
- TS's `acquiredimage`/`imagedata`/`flathistory` are disposable noise (grading lives in PixInsight; disk is the
  graded truth); TSM never reads or writes them.
- TS structure reference (tables, columns, identity semantics): **`TS-SCHEMA.md`**.

## Chrome

- **Toolbar:** Reload (rescan) · progress ring · Cancel (shown while any cancellable phase runs) · sync badge · Push… ·
  Pull now · Templates… · Ambiguities… · **Visible Tonight:** (Duration + Floor up-downs + Tonight). (The old
  toolbar load-summary text was removed 2026-07-23 when the Visible-Tonight group replaced it.) Ambiguities…
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
- **Ctrl+N** opens the Diagnostics window (notes + screenshot into `tsm.log`); the floating accelerator
  hover-hint is suppressed. **Capture in 5 s** hides the window for the countdown so transient light-dismiss UI
  (edit flyouts, context menus) can be opened and survives into the shot — plain Capture can never contain one
  (focus shift dismisses it).

## WinUI gotchas (and the workarounds)

Platform landmines we've hit — they're *why* some of the rules above look the way they do. (The author runs and
screenshots the app to confirm visual fixes; the build only proves the code compiles.)

- **Implicit `TextBlock` styles apply unevenly inside a `ListView` `DataTemplate`** and leak into control
  internals — so vertical centering is set **explicitly per cell**, not via a window-wide implicit style. (Tried
  the implicit route 2026-06-20; it produced uneven columns and was reverted.)
- **A `NumberBox` can't center its text via XAML.** `TextAlignment` doesn't reach its template-internal TextBox
  (microsoft-ui-xaml [#7399](https://github.com/microsoft/microsoft-ui-xaml/issues/7399) /
  [#2896](https://github.com/microsoft/microsoft-ui-xaml/issues/2896)). Workaround: one shared `Loaded` handler
  (`NarrowNumberBox_Loaded`, used by every narrow box — the grid's Desired cell and the Visible-Tonight knobs)
  walks to the inner `TextBox` and sets `TextAlignment=Center` on the instance, trimming its `Padding`/`MinWidth`
  so digits fit a narrow box. **Zeroing that `MinWidth` is what makes a narrow `Width` take effect at all.**
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

## When you add a UI element — checklist

1. New **column**? Add it to the ONE ruler (`GridColumns.cs` — name + width; table position = column
   index), then place the cell in each row template and the header caption (cell `Grid.Column` indexes
   stay per-template; renumber those that shifted). The four grids stamp their `ColumnDefinitions` from
   the ruler (2026-07-24, openspec `grid-column-ruler`) — never hand-edit widths in XAML. Its sort slot
   follows its header position (left-to-right, per *Sorting*) **unless it belongs to the capture-configuration
   block**, whose documented exception sorts after Seconds. If the column is a **reconciliation key** (rows
   separate on it), it must be legible on the rows that separated and must render `mixed` on a rollup whose
   children disagree — a separation the reader cannot explain is worse than no column.
2. New **cell**? Add `VerticalAlignment="Center"`. Right-align if numeric. Text conventions come from
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
4. New **fill / color**? Use `ThemeBrushes` (caution / success / critical fills; `CautionText` / `Secondary`
   foregrounds) — don't hard-code. Note `Run`/`Inline` foregrounds can't use `Opacity`; reach for `Secondary`.
5. New **count / number**? Decide its plane (TS / Disk / Both) and show `—` when the plane is empty; right-align.
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
