# flyout-field-marks — Tasks

## 1. Resolver

- [x] 1.1 `SyncMarks.ForField(table, tsKey, column)` — per-field glyph + old→new tooltip (unattributed;
      new-row entries excluded); reuse existing `Get`/`AddLines`/`Glyph` internals

## 2. TsFieldsEditor mark column

- [x] 2.1 Leading Grid column 0 (`Auto`, MinWidth ~18, title spans 3): centered mark TextBlock per field
      row, tooltip only when marked; optional `MarkResolver` delegate
      (`IReadOnlyList<string> columns → per-column (glyph, tooltip)`) — no resolver, no mark column
- [x] 2.2 `RefreshMarks()` — one resolver pass over all rendered columns, applied at construction and in
      the `CommitChain` continuation after every commit (verified, refused, failed)

## 3. Wiring

- [x] 3.1 `ShowEditFlyoutAsync` injects the resolver: one `ViewModel.BuildMarks()` per pass +
      `ForField(table, key, column)` per column
- [x] 3.2 Mosaic flyout: mark slot on both rows — master enable = union over panels' `target.active`
      (per-panel tooltip lines), priority = `ForField(Project, key, "priority")`; refresh after commits

## 4. Tests

- [x] 4.1 `SyncMarksTests.ForField`: outbound-only, inbound-only, exact-field `⇄` (sibling field stays
      single-direction), clean blank, new-row excluded, tooltip grammar

## 5. Docs + verification

- [x] 5.1 `ARCHITECTURE.md` marks section notes the per-field surface; `CHANGELOG.md` entry
- [x] 5.2 Build + tests green; user verifies visually (flyout mark column + alignment, live `→` on
      commit, `⇄` collision, mosaic rows) — behavior-changing, archive waits for the verify word
