# DOMAIN.md — TargetSchedulerManager

**Charter:** the human/strategy home — the TS-management domain + UX/design language + conventions that fit
neither `ARCHITECTURE.md` (how it works) nor `ROADMAP.md` (what's next). Today its main content is the
**UI conventions** (the grid's settled look-and-feel + the "when you add a UI element" checklist); other
domain/strategy notes accrue here. **Current state only** — *how we got here* → `ROADMAP.md`; *why the code
is shaped this way* → `ARCHITECTURE.md`. Not a frozen spec; look-and-feel is still idea→implement→adjust
(reflects the grid as of the natural-sort + edit-box work, 2026-06-21).

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

`[enable] · Source · Target · Project · Filter · Purpose · Seconds · Desired · TS · Actual · Hours · Plans · Badges`

- **Desired** = TS goal · **TS** = TS's recorded `acquired` (the count TS schedules on with the grader off) ·
  **Actual** = on-disk frames (ground truth). TS `accepted` is **not** a column — write-back keeps it ==
  acquired; a drift shows as an `acc≠acq` badge. (Full rationale: `ARCHITECTURE.md` → *Grid count columns*.)
- **Source** column shows the row's *plane*: `TS` / `Disk` / `Both`. The `TS` count-header deliberately doubles
  this token (author's call).
- Widths are fixed per column except **Target** (`*`). The header `Grid` and all three templates must stay in
  lockstep — changing a column means editing **four grids** and renumbering `Grid.Column`.

## Sorting

Sort precedence follows the columns **left-to-right**: `Target → Project → Filter → Purpose → Seconds`, **natural
order** on the text columns (`NaturalComparer` — "IC 405" before "IC 1318", "Abell 6" before "Abell 21"). Project
only separates same-named targets in different projects. Structural keys sit outside the column order: a mosaic's
**panels** stay under their parent, and **plane** (TS above Disk) is the final tiebreak within a cell. The toolbar
sort picker's other modes (`remaining` / `disk` / `Δ ↓`) are **number-first** with a natural `Target → Project`
tiebreak. `NaturalComparer` is pure-managed (no `shlwapi` P/Invoke).

## em-dash convention

`—` = this row's plane has nothing for that cell (like an empty DataGridView cell). A Disk-only row shows `—`
under Desired/TS → "no TS plan for this exposure"; that signal is load-bearing — keep it.

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
- **Badges** (caution-colored, rightmost): `mosaic · alias · duplicate · name≠ · ambiguous · no-coords ·
  multi-plan · acc≠acq`. Built in `ReconciliationLoader.BuildRows`; they **bubble to the header** (distinct
  union, `RowAggregates`). `IsFlagged` (duplicate / name≠ / ambiguous / multi-plan / acc≠acq) drives the
  **flagged-only** filter.

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
- **Direct in-grid controls** (high-frequency scalars): the **target-enable checkbox** (leftmost, on target
  headers only — hidden on disk-only + mosaic-parent rows) and **Desired** (a `NumberBox` on 1:1 plan leaf rows;
  read-only on headers, disk rows, and mixed rollups).
- **Edit flyout** (everything else, 2026-07-06): a **hover-revealed pencil glyph** (Opacity 0→1 via the
  template-root pointer handlers; `x:Name="EditGlyph"`) and a **right-click menu** ("Edit target…" / "Edit
  exposure plan…", built in code — `Row_RightTapped` — so items gate on row data; this menu is the extension
  point for future row actions). Both open a row-anchored `Flyout` hosting `Controls/TsFieldsEditor` — a form
  **generated from `TsEditableSchema`** (Bool→ToggleSwitch, Whole/Real→NumberBox clamped to schema Min/Max,
  Enum→ComboBox from `EnumValues`, Text→TextBox; Unit beside, Notes as tooltip; cadence-breaking fields excluded
  until their confirm flow ships; **Guarded** fields — `rotation` — start disabled behind an arm-to-edit checkbox on their line, re-locked every open). Values seed fresh from the current db; **each field commits itself** on
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
  and **project priority** (one `project.priority` write — TS-native cascade: panels at priority Default (−1)
  inherit it in scoring, per-panel overrides survive). **Panels are normal targets**: standard target
  glyph/flyout on the panel mini-header rows.
- **Cadence-breaking edits confirm first (shipped 2026-07-07):** plan `enabled` (checkbox on 1:1 filter rows
  + flyout) and project `filterswitchfrequency` (project flyout) show a scope-aware confirm before ANY write
  ("resets TS's filter rotation for this target" / "…of EVERY target in this project"; lands locally, reaches
  BIRDWATCHER at push). The library clears `filtercadenceitem` atomically with the write; a target with a
  hand-authored override exposure order refuses (re-author in the TS editor). Trigger = `IsCadenceBreaking`.
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
- **Integer edit boxes are ~3 characters wide** (fit 999; ≥ 1000 clips in the box but the full value still
  commits). Real/decimal fields are exempt — they need room for the ".". Fixed `Width` (~40 px) + trimmed inner
  padding; the text is **centered in code-behind** (a NumberBox can't center via XAML — see *WinUI gotchas*). The
  clear (✕) button doesn't appear on these, so there's nothing to suppress.

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

## Chrome

- **Toolbar:** Reload (rescan) · progress ring · sync badge · Push… · Pull now · Templates… · summary line.
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
  [#2896](https://github.com/microsoft/microsoft-ui-xaml/issues/2896)). Workaround: a `Loaded` handler
  (`DesiredBox_Loaded`) walks to the inner `TextBox` and sets `TextAlignment=Center` on the instance, trimming its
  `Padding`/`MinWidth` so digits fit a narrow box.
- **`NumberBox`/`TextBox` vertical centering** breaks under a fixed `Height` (the inner ScrollViewer top-aligns).
  Give the box **no fixed `Height`** (let it auto-size; center the box with `VerticalAlignment`) — or template the
  `ContentElement` ScrollViewer to `VerticalAlignment=Center`.

## When you add a UI element — checklist

1. New **column**? Edit all four grids (header + 3 templates) in lockstep; renumber `Grid.Column`; pick a fixed width. Its sort slot follows its header position (left-to-right, per *Sorting*).
2. New **cell**? Add `VerticalAlignment="Center"`. Right-align if numeric.
3. New **state worth flagging**? Add a badge in `BuildRows`, decide whether it sets `IsFlagged`, confirm it bubbles via `RowAggregates`.
4. New **fill / color**? Use `ThemeBrushes` (caution / success / critical) — don't hard-code.
5. New **count / number**? Decide its plane (TS / Disk / Both) and show `—` when the plane is empty; right-align.
6. New **integer edit box**? ~3 chars wide; center its text in code-behind (NumberBox can't via XAML — see *WinUI gotchas*).
7. Touching **look-and-feel**? The build verifies code; **visual correctness is the author's call** — they
   run/screenshot the app (don't do it unprompted).
8. New **editable field whose value shows in a grid column**? Wire its in-place mirror (an `Apply*` on the row
   + owner re-aggregation) — the mirror rule above is a hard convention, not polish.
