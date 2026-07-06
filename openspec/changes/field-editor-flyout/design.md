# Design: field-editor-flyout

## Context

The editing stack is reference-driven end to end: `TsEditableSchema` (declarative field dictionary with
`Label`/`Type`/`Min`/`Max`/`Unit`/`EnumName`/`Notes`) → `TargetSchedulerEditor.SetField`/`ReadField`
(whitelisted, read-back-verified) → TSM's `TsEditGate.ApplyAsync` (guarded, audited, off-thread, LIVE/LOCAL
aware) → per-cell UI. The shipped `desired` cell and `active` checkbox prove the pattern; this change
generalizes the last hop from "one hand-built control per field" to "one generated form per entity."

Grid anatomy (fixed by prior decisions — dossier panel dropped, editing stays in/at the grid):
`TargetGroupRow` (has `TsTargetKey`, may be null for disk-only) → optional panel groups → `ReconciliationRow`
filter rows (`PlanTsKey`, null for disk-only). Projects are a text column, not rows. Explored and settled in
conversation (2026-07-06): flyout host, hover glyph + right-click menu triggers, per-field commit.

## Goals / Non-Goals

**Goals:**
- One reusable `TsFieldsEditor`: `(TsTable, tsKey)` in, schema-generated form out; zero per-field UI code.
- Both triggers (glyph, context menu) on target group rows and filter rows; flyout anchored to the row.
- Per-field commit through the existing gate with the existing outcome surfacing; no new write semantics.
- Additive library support: enum `(code, label)` maps keyed by `EnumName`.
- Ship target `priority`/`rotation`/`roi` and plan `exposure` editing (with `desired`/`active` also present
  in the form — one code path, no exclusion list).

**Non-Goals:**
- No project-row editing (Part 2), no template manager (Part 3), no cadence-breaking fields (Part 4 —
  excluded via `IsCadenceBreaking`), no batch/Apply commit, no new grid columns, no library write changes.

## Decisions

### D1 — One generated form; no per-entity XAML
`TsFieldsEditor` builds its controls at open time from `TsEditableSchema.For(table)`:
`Bool`→`ToggleSwitch`, `Whole`→`NumberBox` (integer spin, Min/Max), `Real`→`NumberBox` (decimal, Min/Max),
`Enum`→`ComboBox` over the new enum map, `Text`→`TextBox`; `Unit` renders beside the control, `Notes` as
tooltip. *Why generated, not four XAML forms:* the schema is the single source of truth — Parts 2/3 and any
future field become zero-UI-work; hand XAML would drift. *Alternative rejected:* `ItemsRepeater` +
per-type `DataTemplate`s is equivalent; either is fine, but generation keeps the control self-contained and
the templates out of the already-heavy `MainWindow.xaml`.

### D2 — All editable cadence-safe fields render, including ones with in-grid controls
The form shows every schema field for the entity except cadence-breaking ones (`IsCadenceBreaking` filter).
`desired`/`active` therefore appear in both places. *Why:* "review and optionally change the
context-appropriate fields" is the point of the flyout; an exclusion list is special-casing that buys
nothing. Consistency is already guaranteed — both paths converge on the same gate and the same row-refresh
hooks (`ApplyDesired`, `_targetActiveEdits`).

### D3 — Enum maps live in the library as declarative data
`TsEditableSchema` gains `EnumValues(string enumName)` → ordered `(int Code, string Label)` list for
`TargetPriority` (−1 Default, 0 Low, 1 Normal, 2 High), `ProjectState`, `ProjectPriority` (authored from TS
source, consumer-neutral, same maintenance story as the field rows). *Why library, not TSM:* the codes are TS
contract knowledge, exactly what the reference exists to encode; Part 3 adds `TwilightLevel` the same way.
*Why not reuse `Astronomy.Catalog.Schema` enums:* those are catalog-schema enums that happen to align; the
editor needs TS-code truth including `TargetPriority`, which the catalog deliberately coerces away
(`SafeTargetPriority`). Keeping the map beside the field dictionary keeps one file to audit against TS.

### D4 — Seeding reads through the gate's editor, off-thread
New `TsEditGate.ReadFieldsAsync(table, key)`: opens the editor on the current source path, calls the existing
`ReadField` per schema field, returns column→value. Runs off the UI thread like writes; flyout shows its form
only after values arrive (single read burst, local or SMB). *Why read fresh rather than from row VMs:* rows
don't carry rotation/roi/priority/exposure, and fresh reads make the flyout correct even when another writer
(NINA) changed the db since load — consistent with TSM's "disk is truth" ethos.

### D5 — Commit per field, immediately; flyout stays open
Each control commits on change/focus-loss via `ApplyAsync` with the entity label (existing audit format).
Success: keep value, refresh in-grid mirrors (`ApplyDesired`/recompute, active-checkbox state). Refusal or
failure: revert the control to the last-known value and surface the existing `RefusalText`/status message.
Light-dismiss is therefore always safe — there is never uncommitted state. *Alternative rejected (Apply
button):* fakes atomicity the single-field write path doesn't have; adds dirty-state tracking for no benefit.

### D6 — Triggers: hover glyph + `MenuFlyout`, gated by key presence
Target group rows get glyph + "Edit target…" menu item when `TsTargetKey` is non-null; filter rows get glyph
+ "Edit exposure plan…" when `PlanTsKey` is non-null (the `CanEditDesired` pattern). Disk-only rows show
neither. Both triggers open the same `Flyout` anchored at the row element. The context menu is the
extensibility point (Part 3 adds "Edit template…" on filter rows; the cadence proposal's reset action fits
here too). Click-on-name stays what it is today (expansion toggle) — no gesture collision.

## Risks / Trade-offs

- **[Flyout light-dismiss mid-typing]** → per-field commit on focus-loss means a dismissal commits-or-reverts
  the focused control deterministically; nothing else can be dirty. Verified by the user in-app.
- **[NumberBox free-typing outside Min/Max]** → clamp on commit using schema Min/Max (the same bounds the
  library documents); reject non-numeric input at the control level.
- **[Enum map drifts from TS]** → same risk class as the field dictionary itself; the map lives in the same
  file with the same TS-source pinning, and the editor's read-back verification catches a write TS's CHECKs
  would reject.
- **[Two edit paths for `desired`/`active`]** → both converge on the same gate + refresh hooks; a divergence
  would be a bug in the hook, visible immediately in-grid.
- **[SMB read burst on flyout open]** → one editor open + a handful of single-column reads; acceptable
  latency for an explicit user gesture, and it buys freshness (D4).

## Migration Plan

Additive throughout; no schema/back-compat concerns. Library first (enum map + tests), then TSM. If the
parked `cadence-safe-ts-edits` ships first, its `CadenceSafe`→`Clears` rename touches the same file — the
`IsCadenceBreaking` seam keeps this change's code unaffected either way.

## Open Questions

- None blocking. Glyph iconography/placement and flyout width are implementation-time visual calls
  (user-verified in-app, per DOMAIN.md's UI checklist).
