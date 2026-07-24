# presentation-conventions — one home each for text conventions, editor control config, and code-side brushes

## Why

Presentation lanes P3+P4+P5 (2026-07-24 consultation), folded per the user's call. The house display
style is real but scattered: the "—" empty-cell convention exists at **15 sites across 5 files**
(`ReconciliationRow`, `AggregateHeaderRow`, `SyncMarks` tooltips, `ReconciliationLoader`'s no-data row),
the `target · filter` identity-label convention at **9 sites across 2 files**, `FormatWhen` hides as a
private in the VM's sync partial, and the `"H @900s"` cell naming is private to `AmbiguityReport`.
`TsFieldsEditor` builds its NumberBox configuration twice (plain + sentinel) and its unit label twice.
Code-side caution-brush lookups are raw `Application.Current.Resources` casts in two window partials
while `ThemeBrushes` (the existing defensive-lookup home) sits namespaced under `Rows\`. With format
changes expected to dominate future work, each convention should be a one-place edit.

## What Changes

- **P3 — `Models/Format.cs` becomes the display-convention home:** gains `Dash` ("—"),
  `CountOrDash(int?)`, `When(DateTimeOffset)` (from the VM's private `FormatWhen`),
  `Cell(filter, purpose, seconds)` (from `AmbiguityReport`'s private), and `Label(left, right)` (the
  `·` identity convention). All 15 dash sites, 9 label sites, and both privates route through it.
- **P4 — `TsFieldsEditor` factories:** `MakeNumberBox(value, enabled)` (the config block existed twice)
  and `UnitLabel(unit)` (the styled TextBlock existed twice).
- **P5 — `ThemeBrushes` promoted to the app root namespace** (enclosing-namespace lookup keeps the row
  models' unqualified references compiling) and gains `CautionText` (the foreground caution brush);
  the two raw resource-cast sites (`MainWindow.Flyouts` pair-warn, `MainWindow.Dialogs` decrease lines)
  route through it — adopting its defensive null-on-missing posture over the raw cast's throw.
- **DOMAIN.md** checklist/conventions updated to match: text conventions live in `Format`, code-side
  brushes in `ThemeBrushes`, editor numeric inputs via the factory.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `reconciliation-grid`: codifies (not changes) the empty-cell and hours display conventions the
  consolidation must preserve — absent values render as the em dash (never blank, never a fabricated 0 —
  a real 0 like disk's is a fact and renders as 0), and hours render F1 with the small-nonzero F2
  exception.

## Impact

- **App**: `Models/Format.cs`, row models, `SyncMarks`, `ReconciliationLoader`, `AmbiguityReport`,
  `MainViewModel.Sync/.Edits`, `MainWindow.Flyouts/.Dialogs`, `TsFieldsEditor`, `ThemeBrushes` (moved).
- **Tests**: the dash/hours/cell conventions are locked by `RowTests`/`AggregateHeaderRowTests`/
  `AmbiguityReportTests`; the factory/brush moves are byte-identical property sets, compile-checked.
- **Verification**: test-locked where it matters ⇒ auto-archive per the standing rule; the editor/dialog
  visuals are unchanged by construction — worth a casual glance next time the app is open, not a gate.
- **Labels caution**: `Format.Label` output is byte-identical to the inline strings — journal labels are
  persisted display text, so the construction path changes but never the output.
