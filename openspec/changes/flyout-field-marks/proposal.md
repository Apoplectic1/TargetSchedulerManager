# flyout-field-marks — Proposal

## Why

The grid's column-0 marks say *a row* has pending changes; the flyout is where the user actually meets the
*fields* — and today it shows no sync state at all. Opening a flyout on a marked row gives no clue which
field is unpushed (`→`), which arrived changed from the rig (`←`), or — the high-value case — which exact
field is a **collision** (`⇄`: an unpushed local edit and a rig-side change on the *same field*, which a
push will overwrite). All the facts already exist per (table, key, column) in the journal + inbound store;
`SyncMarks` merely aggregates them to row level. User asked for this 2026-07-26 after the
`template-change-marks` round.

## What Changes

- Every field row in the schema-generated edit flyouts (target, project, exposure plan, template — all
  rendered by `TsFieldsEditor`) gains a **leading mark column**: `←`/`→`/`⇄`, blank when clean, fixed-width
  so labels stay mutually aligned; the field's old→new lines as tooltip.
- New `SyncMarks.ForField(table, key, column)` — per-field resolution over the same snapshotted facts.
- `TsFieldsEditor` gains a `MarkResolver` delegate seam (same style as `CommitField`/`EffectiveValue`),
  closed over (table, key) by `ShowEditFlyoutAsync`.
- **Live feedback:** after each in-flyout commit, all field marks re-resolve from fresh facts — toggling a
  field flips its `→` on immediately (and the grid's row mark updates in place as today).
- The custom mosaic-project flyout's two rows (master enable, priority) are hand-wired to the same marks.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `schema-driven-field-editor`: the generated form gains the leading per-field mark column (render +
  alignment + live re-resolve after commits + tooltips).
- `edit-direction-marks`: adds the per-field resolution surface (`ForField`) and its semantics — same
  glyph language, field granularity, `⇄` = exact-field collision.
- `target-and-plan-flyouts`: the mosaic-project flyout's custom rows carry the same marks.

## Impact

- `TargetSchedulerManager.App\Services\SyncMarks.cs` — `ForField` (~15 lines over existing lookups).
- `TargetSchedulerManager.App\Controls\TsFieldsEditor.cs` — third Grid column, mark TextBlock per row,
  `MarkResolver` delegate, re-resolve pass after each commit.
- `TargetSchedulerManager.App\MainWindow.Flyouts.cs` — `ShowEditFlyoutAsync` passes the resolver;
  mosaic flyout hand-wiring.
- Tests: `ForField` units in `SyncMarksTests`; the `TsFieldsEditor` layout half is XAML-runtime
  (visual-verify only, no unit tests today — unchanged).
- No library (`Astronomy.Catalog`) changes.
