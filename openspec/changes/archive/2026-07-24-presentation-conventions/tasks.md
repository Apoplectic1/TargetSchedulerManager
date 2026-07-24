# presentation-conventions — tasks

## 1. Format home (P3)

- [x] 1.1 `Models/Format.cs`: add `Dash`, `CountOrDash`, `When`, `Cell`, `Label` (design D1).
- [x] 1.2 Route the sites: row models (dash arms + count texts), `SyncMarks` tooltips,
      `ReconciliationLoader` no-data row, `AmbiguityReport` (`Cell` delegate), `MainViewModel.Sync`
      (`FormatWhen` deleted → `Format.When`), the 9 label sites in `MainViewModel.Edits` +
      `MainWindow.Flyouts`.

## 2. Factories + brushes (P4, P5)

- [x] 2.1 `TsFieldsEditor`: `MakeNumberBox` + `UnitLabel`; `BuildNumber`/`SentinelCell`/`WithUnit` consume.
- [x] 2.2 `ThemeBrushes` → app root namespace + `CautionText`; the two raw resource-cast sites route
      through it (defensive posture adopted, design D2).

## 3. Verify + docs + archive

- [x] 3.1 Build + full test run (RowTests/AggregateHeaderRowTests/AmbiguityReportTests + label-asserting
      suites are the locks).
- [x] 3.2 DOMAIN.md: conventions homes documented (Format for display text, ThemeBrushes for code-side
      brushes, the editor factory); CHANGELOG + ROADMAP digest. Same commit.
- [x] 3.3 Auto-archive (standing rule — test-locked; visuals unchanged by construction) — sync the
      reconciliation-grid delta.
