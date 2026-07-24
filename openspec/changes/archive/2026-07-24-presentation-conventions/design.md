# presentation-conventions — design

## Context

See proposal inventory. Existing homes: `Models/Format.cs` (only `Hours`), `ViewModels/Rows/ThemeBrushes.cs`
(Caution/Success/Critical background fills, defensive lookup).

## Goals / Non-Goals

**Goals:** each convention defined once; every output byte-identical; DOMAIN documents the homes.

**Non-Goals:** styles (`BodyStrongTextBlockStyle` lookups stay — P5 is brushes); `TsValueText` (the
journal-value rule is sync-side, deliberately separate); `AmbiguityReport.PlanRow` (report-specific);
XAML-side `ThemeResource` uses (declarative theme-awareness is already right).

## Decisions

- **D1 — `Format` members:** `public const string Dash = "—"`; `CountOrDash(int?)` (`?.ToString() ?? Dash`);
  `When(DateTimeOffset)` (verbatim from `FormatWhen`); `Cell(string filter, FilterPurpose purpose, int seconds)`
  (verbatim from `AmbiguityReport`); `Label(string left, string right)` (`$"{left} · {right}"` — the
  grid/journal identity convention; the doc comment notes labels persist in the journal, so the OUTPUT is
  contract). `SecondsText`/`HoursText` dash arms use `Format.Dash` directly (their conditions aren't
  null-coalescing shapes).
- **D2 — `ThemeBrushes` move:** new file at project root, namespace `TargetSchedulerManager.App`; old file
  deleted. Row-model references stay unqualified — C# enclosing-namespace lookup resolves
  `TargetSchedulerManager.App.ThemeBrushes` from inside `…App.ViewModels.Rows`. New member `CautionText`
  (`SystemFillColorCautionBrush` — foreground, vs the existing background fills). The two raw-cast sites
  adopt the defensive posture: a missing resource key renders default-colored text instead of throwing —
  consistent with the class's charter, and a missing *system* theme key is effectively unreachable.
- **D3 — editor factories:** `MakeNumberBox(double value, bool enabled = true)` and `UnitLabel(string unit)`,
  both private static on `TsFieldsEditor`; `BuildNumber`, `SentinelCell`, and `WithUnit` consume them.

## Risks / Trade-offs

- [Label strings feed persisted journal labels] → `Format.Label` is character-identical to the inline
  interpolations; the existing journal/label tests (gate + sync suites assert exact labels) lock it.
- [Brush sites change throw→null on missing key] → deliberate (D2); system theme keys don't go missing.

## Migration Plan

None. Clean rebuild; auto-archive per the standing rule (test-locked conventions; byte-identical config moves).

## Open Questions

None.
