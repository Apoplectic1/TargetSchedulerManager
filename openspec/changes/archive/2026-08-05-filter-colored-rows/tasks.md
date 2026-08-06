# filter-colored-rows — Tasks

## 1. Palette

- [x] 1.1 Add `Models/FilterBrushes.cs`: filter code → `SolidColorBrush` at the dark-tuned wash
      alpha (O/H/S/B/G/R from the design palette); L and unknown codes return the transparent brush
      (never null — hit-test contract, design D4). One alpha constant, trivially adjustable for the
      sign-off round.

## 2. Row wiring

- [x] 2.1 Expose the wash brush on `ReconciliationRow` (keyed off the row's own Filter display code,
      design D5; transparent on non-filter/L/unknown rows).
- [x] 2.2 Bind `FilterRowTemplate` root `Grid.Background` to it (replacing the literal
      `Transparent`); confirm hover reveal + row click still work. Headers/panels untouched.

## 3. Tests

- [x] 3.1 Unit-test the brush selection: each palette code maps to its color, L/unknown/empty map to
      transparent, and the wash never affects `IsFlagged`/search/sort inputs (no key participation).

## 4. Docs + verify

- [x] 4.1 UI.md: Visual language gains the wash layer (palette, L-plain rule, identity-under-state
      layering); checklist item 4 gains the `FilterBrushes` vs `ThemeBrushes` split. Same commit as
      code.
- [x] 4.2 Build + full app test suite green.
- [x] 4.3 User visual sign-off (dark theme): wash strength, H-vs-S legibility, pills/hover/selection
      reading through — alpha adjusted on feedback before archive.
