# DOMAIN.md — TargetSchedulerManager

**Charter:** the human/strategy home — the TS-management domain + UX/design language + conventions that fit
neither `ARCHITECTURE.md` (how it works) nor `ROADMAP.md` (what's next). Today its main content is the
**UI conventions** (the grid's settled look-and-feel + the "when you add a UI element" checklist); other
domain/strategy notes accrue here. **Current state only** — *how we got here* → `ROADMAP.md`; *why the code
is shaped this way* → `ARCHITECTURE.md`. Not a frozen spec; look-and-feel is still idea→implement→adjust
(reflects the grid as of the ambiguity-report work, 2026-07-08).

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

`[mark] · [enable] · Source · Target · Project · Filter · Purpose · Seconds · Desired · TS · Actual · Hours · Plans · Badges`

- **Mark** (column 0, 24 px, unlabeled): the sync-direction mark on every row level — `←` = arrived changed
  from BIRDWATCHER (pull diff, sticky for the session) · `→` = unpushed local writes (journal: manual edits
  *and* write-back stamps) · `⇄` = both · blank = clean. Headers roll up the union of their subtree; a mosaic
  project edit marks the **parent only**, never panels; disk-plane leaves are structurally blank (marks key on
  the plan; target/project changes mark the header). Tooltip: per-field `old → new` on leaves, direction
  counts on headers. Cleared: `→` by Push/Discard; `←` at the next open's pull. (Mechanics:
  `ARCHITECTURE.md` → *Sync-direction marks*.)
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
**panels** stay under their parent, and **plane** (TS above Disk) is the final tiebreak within a cell. The toolbar
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
- **Badges** (caution-colored, rightmost): `mosaic · duplicate · name≠ · ambiguous · no-coords · no data ·
  multi-plan · acc≠acq` (`no data` = a target with neither plans nor scanned frames; `no-coords` when it is
  also unanchored). Built in `ReconciliationLoader.BuildRows`; they **bubble to the header** (distinct
  union, `RowAggregates`). `IsFlagged` (duplicate / name≠ / ambiguous / multi-plan / acc≠acq) drives the
  **flagged-only** filter. (The `alias` badge died with the fold mechanism, 2026-07-23.)

## Alignment & spacing

- **Every item on a line is vertically centered, line by line** — `VerticalAlignment="Center"` **explicitly per
  cell** (not a window-wide implicit style; see *WinUI gotchas*). Add it on every new cell.
- Numeric **columns** right-align (`TextAlignment="Right"`); text columns left; the enable checkbox centers in its
  36 px gutter. (Numeric **edit boxes** center — see *Editing*.)

## Editing

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
  control. Fields with a direct in-grid control also appear in the flyout — both paths converge on the same
  setters, so the grid mirrors in place. **Sentinel columns** (TS stores a reserved −1 meaning "defer to the
  default": plan `exposure` → template, template `gain`/`offset`/`readoutmode` → camera) render as their meaning
  — a "use default (…)" checkbox over the number box, never the raw −1; checked ⇔ the column holds −1 (box
  disabled, showing the resolved value when known), unchecking arms the box (the override commits only when a
  number commits).
- **Mosaics are a special case (user decision 2026-07-06):** a mosaic *parent* row is a grouping node (no TS
  target), so its flyout edits the two whole-mosaic knobs — **"Enable all panels"** (fan-out `target.active`
  to every TS-backed panel; tri-state display when panels disagree; each write individually guarded + audited)
  and **project priority** (one `project.priority` write; per-panel priority overrides survive — mechanism in
  `ARCHITECTURE.md` → *Key facts* / Mosaics). **Panels are normal targets**: standard target
  glyph/flyout on the panel mini-header rows.
- **Cadence-breaking edits write directly - no confirm (user decision 2026-07-07):** plan `enabled` (checkbox
  on 1:1 filter rows + flyout) and project `filterswitchfrequency` (project flyout) commit like any field.
  Safety is structural, not dialog-based — the library clears the invalidated `filtercadenceitem` rows in the
  same transaction as the write (mechanism: `ARCHITECTURE.md` → *TS write-back* + the `TsEditGate` paragraph; the
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
- **Integer edit boxes are sized to their digit budget.** Fixed `Width` + trimmed inner padding, text
  **centered in code-behind** via `NarrowNumberBox_Loaded` (a NumberBox can't center via XAML, and its
  template-internal `TextBox` `MinWidth` otherwise overflows a narrow `Width` — see *WinUI gotchas*).
  Real/decimal fields are exempt — they need room for the ".". Two cases:
  - **No spin buttons** (`SpinButtonPlacementMode="Hidden"` — the grid's Desired cell): **~3 characters,
    `Width` ~40 px** (fits 999; ≥ 1000 clips in the box but the full value still commits).
  - **Inline spin buttons** (the Visible-Tonight knobs): digits sit **left-aligned** (WinForms-up-down
    style, *not* centered — see the overlap gotcha below) and the box budgets `Width` = digits + 42 px
    (4 px left pad + 38 px right pad clearing the chevron pair): Duration (max 480) `Width="68"`, Floor
    (max 89) `Width="60"`. `NarrowNumberBox_Loaded` shrinks the pair from its stock 76 px (MinWidth 32 +
    4 px margins per button) to ≈ 36 (MinWidth 16, 2 px margins) — full height, just narrower to hit —
    and sets the padding. Rejected 2026-07-26: `Compact` placement (spinners hidden behind hover) and
    full-size Inline (no-clip minimum 104/96 px ⇒ no visible shrink over the ~110 px stock box).

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

- **Toolbar:** Reload (rescan) · progress ring · Cancel pull (shown only while pulling) · sync badge · Push… ·
  Pull now · Templates… · Ambiguities… · **Visible Tonight:** (Duration + Floor up-downs + Tonight). (The old
  toolbar load-summary text was removed 2026-07-23 when the Visible-Tonight group replaced it.) Ambiguities…
  (enabled once a load exists) writes a dated printable Markdown report of every
  TS/disk ambiguity — what · why · the hand fix in NINA's TS UI — to `%APPDATA%\TargetSchedulerManager\Reports\`
  and opens it; the status line carries `· N ambiguities` when the tripwire is non-zero.
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
  so digits fit a narrow box. **Zeroing that `MinWidth` is what makes a narrow `Width` take effect at all** —
  the template forces 120 (inline spinners) / 64 without. The same handler shrinks the **inline spin
  buttons** and right-pads the text clear of them, because **the template draws the spin buttons ON TOP of
  the input `TextBox` with no reservation whatsoever** — the stock control avoids overlap only because its
  120 px minimum keeps short left-aligned text far from the buttons. Two consequences: a narrow inline box
  must supply that clearance itself (the handler's 38 px right padding), and its text must stay
  **left-aligned** — centering positions digits at the middle of the *full* box, i.e. under the chevrons
  (bug obs-1fe4, 2026-07-26). All fix-ups must be **per-instance**: shadowing `NumberBoxSpinButtonStyle`
  in app resources can't work, because a `StaticResource` referenced inside a framework `ControlTemplate`
  resolves within `generic.xaml`, not against `Application.Resources`.
- **`NumberBox`/`TextBox` vertical centering** breaks under a fixed `Height` (the inner ScrollViewer top-aligns).
  Give the box **no fixed `Height`** (let it auto-size; center the box with `VerticalAlignment`) — or template the
  `ContentElement` ScrollViewer to `VerticalAlignment=Center`.

## When you add a UI element — checklist

1. New **column**? Add it to the ONE ruler (`GridColumns.cs` — name + width; table position = column
   index), then place the cell in each row template and the header caption (cell `Grid.Column` indexes
   stay per-template; renumber those that shifted). The four grids stamp their `ColumnDefinitions` from
   the ruler (2026-07-24, openspec `grid-column-ruler`) — never hand-edit widths in XAML. Its sort slot
   follows its header position (left-to-right, per *Sorting*).
2. New **cell**? Add `VerticalAlignment="Center"`. Right-align if numeric. Text conventions come from
   `Models\Format.cs` — the one home (2026-07-24, `presentation-conventions`): `Format.Dash`/`CountOrDash`
   for empty cells (— means "nothing to say"; a measured 0 renders 0), `Format.Hours`, `Format.When`,
   `Format.Cell` ("H @900s"), `Format.Label` ("target · filter" — journal-persisted, shape is contract).
   Code-side brush lookups go through `ThemeBrushes` (app root; defensive null-on-missing), never raw
   `Application.Current.Resources` casts. Editor numeric inputs come from `TsFieldsEditor.MakeNumberBox`.
3. New **state worth flagging**? Add a badge in `BuildRows`, decide whether it sets `IsFlagged`, confirm it bubbles via `RowAggregates`.
4. New **fill / color**? Use `ThemeBrushes` (caution / success / critical) — don't hard-code.
5. New **count / number**? Decide its plane (TS / Disk / Both) and show `—` when the plane is empty; right-align.
6. New **integer edit box**? Size it to its digit budget (+ 42 px if it shows inline spin buttons) and wire `Loaded="NarrowNumberBox_Loaded"` — the handler lets a narrow `Width` stick, and per placement mode centers the digits (hidden spinners) or shrinks the chevrons and keeps left-aligned digits clear of them (inline). See *WinUI gotchas*.
7. Touching **look-and-feel**? The build verifies code; **visual correctness is the author's call** — they
   run/screenshot the app (don't do it unprompted).
8. New **editable field whose value shows in a grid column**? Wire its in-place mirror (an `Apply*` on the row
   + owner re-aggregation) — the mirror rule above is a hard convention, not polish.
