# grid-column-ruler — one definition of the grid's column geometry

## Why

Presentation-readiness P1 (2026-07-24 consultation), the highest-leverage item for a UI-heavy future:
the reconciliation grid's 14-column ruler (`24, 36, 110, *, 170, 60, 70, 80, 88, 60, 60, 60, 45, 150`)
exists as **four byte-identical copies** — the three row DataTemplates plus the hand-rolled header Grid
(verified by extraction/diff before this change). Cells across row kinds align only because the four
blocks are kept in sync by hand; a width tweak is four edits, and adding a column means four
`ColumnDefinitions` inserts plus renumbering — the reason DOMAIN.md needs an "add a UI element"
checklist at all. The alignment invariant itself has never been written down.

## What Changes

- **`GridColumns`** (new, app root namespace so `local:` reaches it in XAML): the one named column ruler —
  a `(name, width)` table documenting all 14 columns — exposed as an attached property
  (`local:GridColumns.ApplyRuler="True"`) whose change-callback stamps `ColumnDefinitions` onto the Grid
  at parse time.
- The four XAML `ColumnDefinitions` blocks (~16 lines each) are replaced by the one attribute; the
  header comment "mirrors the row template's column widths" becomes literally true by construction.
- **Scope honesty:** this single-sources the *ruler* (widths, count, order). Cell `Grid.Column` indexes
  stay per-template — unavoidable with DataTemplates — so adding a column still edits each template's
  cells, but against one authoritative ruler instead of four parallel ones. The DOMAIN checklist updates
  to match.
- Mechanism rationale (why not width resources): WinUI's XAML won't share `ColumnDefinition` instances,
  and `StaticResource` into a `GridLength` property is unreliable across the UWP lineage; the attached
  property is guaranteed, runs once per Grid instance at parse (before layout, before children), and
  costs nothing per frame — recycled containers keep their stamped definitions.

## Capabilities

### New Capabilities
- `reconciliation-grid`: seeded with the codified (not new) alignment invariant — the header and every
  row template SHALL render one shared column geometry so cells align across row kinds. The natural home
  for future grid-presentation requirements.

### Modified Capabilities
(none)

## Impact

- **App**: new `GridColumns.cs`; `MainWindow.xaml` (4 blocks → 4 attributes). No code-behind, no VM, no
  behavior change intended.
- **Tests**: none possible — XAML rendering is headless-untestable; the 230 stay the regression floor.
- **Verification split:** build + XamlCompiler prove wiring; the pixels need your eyes — **the visual
  pass GATES archive** (no auto-archive): columns align exactly as before across header / group / filter
  / panel / detail rows, the Target star-column still absorbs resize, hover glyph + inline Desired + the
  Hours pill unaffected.
- **Docs**: DOMAIN.md "add a UI element" checklist updated (widths now one place); CHANGELOG/ROADMAP.
