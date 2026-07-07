# Proposal: field-editor-flyout

## Why

TSM can already write any field in the library's declarative `TsEditableSchema` through the guarded
`TsEditGate`, but the UI exposes only two of them (`target.active` checkbox, `exposureplan.desired` in-grid
cell). The remaining cadence-safe fields — target `priority` (the queued roadmap item), `rotation`, `roi`,
plan `exposure` — have write plumbing and no surface. Adding one in-grid column per field does not scale
(13 fixed columns already). This change builds the **context-sensitive editing foundation**: a single
schema-generated field-editor control hosted in a flyout, triggered from the row the user points at. It is
Part 1 of the editing-surface plan — Part 2 (project settings) and Part 3 (exposure-template manager) reuse
the same control and triggers; Part 4 (`cadence-safe-ts-edits`, already proposed) plugs its confirm dialog
into the same seam.

## What Changes

- **Library (`Astronomy.Catalog`, small)**: `TsEditableSchema` gains a declarative enum-values map — ordered
  `(code, label)` lists keyed by the existing `TsField.EnumName` strings (`TargetPriority` incl. `Default = -1`,
  `ProjectState`, `ProjectPriority`) — so a consumer can build a dropdown without hard-coding TS codes.
- **TSM**: a reusable `TsFieldsEditor` control that, given `(TsTable, tsKey)`, generates its form from the
  schema (Bool→ToggleSwitch, Whole/Real→NumberBox with Min/Max/Unit, Enum→ComboBox, Text→TextBox), seeds
  current values from the TS db, and commits **per field** through the existing `TsEditGate` (read-back
  verified, audited, off the UI thread), surfacing refusals/failures like existing edits.
- **TSM**: two triggers on the two TS-backed row types — a hover **edit glyph** and a **right-click context
  menu** ("Edit target…" on target group rows; "Edit exposure plan…" on filter rows) — both opening the
  flyout anchored at the row. Rows without a TS key (disk-only actuals) offer neither.
- Cadence-breaking fields (`exposureplan.enabled` today) are **excluded from the rendered form** until the
  parked `cadence-safe-ts-edits` change ships its confirm-dialog flow; the filter is
  `TsEditableSchema.IsCadenceBreaking`, so that change lights them up without rework here.
- No new grid columns; the flyout is the review-and-edit surface for these fields.

## Capabilities

### New Capabilities

- `schema-driven-field-editor`: the reusable editor contract — schema-generated controls, enum value maps,
  current-value seeding, per-field guarded commit, outcome surfacing, cadence-breaking exclusion seam.
- `target-and-plan-flyouts`: the Part 1 surfaces — hover glyph + right-click menu on target group rows and
  filter rows, flyout anchoring, and which fields each entity presents.

### Modified Capabilities

_None (no existing specs)._

## Impact

- **Two repos**: `..\Library\Astronomy.Catalog\TargetScheduler\TsEditableSchema.cs` (+ tests) for the enum
  map; TSM `MainWindow.xaml` row templates, `MainViewModel`, `TsEditGate` (a read-side entry to seed the
  flyout via the editor's existing `ReadField`), new `TsFieldsEditor` control. Session needs `--add-dir ..\Library`.
- **Library API**: additive only (no breaking change; independent of the parked `CadenceSafe`→clear-scope
  rename — whichever ships second reconciles trivially).
- **Ships the roadmap item** "target `priority` editing" as the first visible payoff.
- **Tests**: library tests for the enum map; TSM seam tests for the read-seeding path and outcome handling.
  Visual behavior (glyph hover, flyout interaction, commit feedback) is user-verified by running the app.
- **Not in scope**: project-row editing (Part 2 — projects have no row anchor in today's grid, only a text
  column; the anchor question belongs to Part 2), template manager (Part 3), cadence-breaking fields (Part 4),
  new grid columns, `ExposureTemplateId` reassignment.
