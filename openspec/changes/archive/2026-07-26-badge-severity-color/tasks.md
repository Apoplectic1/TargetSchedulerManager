## 1. Badge vocabulary (pure, testable core)

- [x] 1.1 Add `TargetSchedulerManager.App/Models/Badges.cs`: internal static class owning `Separator = " · "`, a
      const per token (`Mosaic`, `NoData`, `NoCoords`, `Duplicate`, `NameMismatch`, `Ambiguous`, `MultiPlan`,
      `AccNeAcq` — values exactly the strings shipping today), `IsWarning(string token)`, and
      `Split(string badge) => IEnumerable<(string Token, bool IsWarning)>` / `Join(IEnumerable<string>)`.
- [x] 1.2 Add `TargetSchedulerManager.App.Tests/BadgesTests.cs`: severity table over all eight tokens
      (warning = duplicate/name≠/ambiguous/multi-plan/acc≠acq/no-coords; informative = mosaic/no data),
      `Split` on empty string yields nothing, `Split`/`Join` round-trip, and an unknown token classifies as
      informative rather than throwing.
- [x] 1.3 Point `ReconciliationLoader.BuildRows` at the `Badges` consts and `Badges.Join` instead of inline
      literals and a local `" · "` (`:165-174`, `:265`) — no behaviour change, badge text byte-identical.

## 2. Severity rendering

- [x] 2.1 Add `ThemeBrushes.Secondary => Lookup("TextFillColorSecondaryBrush")` with a doc comment naming it
      the informative-badge / quiet-fact foreground (and why `Opacity` is unavailable on a `Run`).
- [x] 2.2 Add `TargetSchedulerManager.App/Controls/BadgeRuns.cs`: attached `Tokens` string DP whose change
      handler clears `TextBlock.Inlines` and appends one `Run` per `Badges.Split` token
      (`ThemeBrushes.CautionText` when warning, `ThemeBrushes.Secondary` otherwise) with separator `Run`s at
      informative severity. Null/empty value leaves the cell empty. Follow the `GridColumns.ApplyRuler` shape.
- [x] 2.3 Swap all three row templates (`MainWindow.xaml:80`, `:157`, `:203`) from
      `Text="{x:Bind Badge}"` + hard-coded `Foreground="{ThemeResource SystemFillColorCautionBrush}"` to
      `local:BadgeRuns.Tokens="{x:Bind Badge}"`, keeping `Margin`, `VerticalAlignment`, and `TextTrimming`.

## 3. Flagged classification: `no-coords`

- [x] 3.1 `ReconciliationLoader.cs:175` — add `|| isUnanchored` to the `flagged` expression (the path where an
      unanchored target carries exposure plans, so it has cells).
- [x] 3.2 `ReconciliationLoader.cs:265` — the no-cells fallback row passes `isFlagged: isUnanchored` instead of
      the literal `false`.
- [x] 3.3 Update the `ReconciliationRow.IsFlagged` doc comment (`:102`) to list the six warning states.
- [x] 3.4 `BuildRowsTests` — add `Assert.True(r.IsFlagged)` to the `no-coords` case (`:109`) and
      `Assert.False(r.IsFlagged)` to the `no data` case (`:97`); add a case for an unanchored target that
      *does* carry an exposure plan, asserting its cell rows are flagged and badged `no-coords`.

## 4. Header rollup dedupes tokens

- [x] 4.1 `RowAggregates.Compute` (`:45`) — union child badges at token level via `Badges.Split`/`Join`,
      `Distinct()` preserving first-appearance order, replacing the whole-string `Distinct()`.
- [x] 4.2 Add a `RowAggregatesTests` case: children `"mosaic"` + `"mosaic · multi-plan"` roll up to
      `"mosaic · multi-plan"`; and a case asserting an unanchored child bubbles `IsFlagged` to the header.

## 5. Verify + document

- [x] 5.1 `dotnet build` clean and the full app test suite green (expect the prior count plus the new cases).
- [x] 5.2 `DOMAIN.md` — rewrite the Badges bullet (`:85-89`) for the two-tier colour convention, `IsFlagged`
      now including `no-coords`, and the header rollup being a true token-level distinct union; extend the
      "add a UI element" checklist step 3 (decide the severity tier, not just whether it sets `IsFlagged`) and
      step 4 (`ThemeBrushes` gained `Secondary`).
- [x] 5.3 `CHANGELOG.md` — newest-first entry, explicitly naming that flagged-only counts can rise because
      unanchored targets now qualify (so a surprising count has a findable cause).
- [x] 5.4 Commit code + docs together; then hand off for the author's visual check — per-token colours in all
      three row kinds, and whether `TextFillColorSecondaryBrush` reads as "quiet fact" rather than "disabled".
