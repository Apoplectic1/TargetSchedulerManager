## Why

The Badges column paints every token amber (`SystemFillColorCautionBrush`, hard-coded in all three row
templates) — so `mosaic`, a neutral structural fact, reads exactly as urgent as `duplicate`, which is broken
authoring the user must fix by hand in NINA's TS UI. The column's whole job is triage, and it currently
refuses to triage: everything shouts. Colouring by severity turns the rightmost column back into a scan
target — amber means "go fix something", quiet means "just so you know".

Two smaller truths surface with it: `no-coords` (a TS target with no RA/Dec — unschedulable by TS, can never
accrue disk credit) is broken authoring but is classified today as unremarkable, and the header rollup's
"distinct union" of child badges dedupes whole strings rather than tokens, so a mosaic with one multi-plan
filter renders `mosaic · mosaic · multi-plan`.

## What Changes

- **Two-tier badge colour, per token.** Each badge token is coloured on its own inside one `TextBlock`:
  - **Warning** (amber, `ThemeBrushes.CautionText`): `duplicate` · `name≠` · `ambiguous` · `multi-plan` ·
    `acc≠acq` · `no-coords`.
  - **Informative** (dimmed, `TextFillColorSecondaryBrush`): `mosaic` · `no data`.
  A row reading `mosaic · multi-plan` now renders the two tokens differently rather than uniformly amber.
- **New `Models/Badges.cs`** — one home for the badge vocabulary: the eight token literals (today typed
  inline in `ReconciliationLoader`), the `" · "` separator (today duplicated in the loader and
  `RowAggregates`), the severity predicate, and the `Split`/`Join` pair the renderer and tests share.
- **New `Controls/BadgeRuns.cs`** — an attached property that fills a `TextBlock`'s `Inlines` with one
  coloured `Run` per token, following the `GridColumns.ApplyRuler` precedent. Replaces the hard-coded
  `Foreground` on all three row templates; `TextTrimming` and the 150 px column are unchanged.
- **`no-coords` becomes genuinely flagged** — it sets `IsFlagged`, so it enters the flagged-only filter and
  bubbles to headers. Without this, an amber row would be *hidden* by flagged-only, leaving the colours and
  the filter contradicting each other.
- **Header badge rollup dedupes tokens, not strings** — `mosaic · mosaic · multi-plan` becomes
  `mosaic · multi-plan`, making DOMAIN.md's long-standing "distinct union" description true.

Not breaking: the badge *text* (and therefore the search vocabulary `Matches()` exposes) is unchanged.

## Capabilities

### New Capabilities

None — this extends the existing grid-presentation contract.

### Modified Capabilities

- `reconciliation-grid`: gains a requirement that badge tokens render at one of two severities with a single
  authoritative classification, and a requirement that the match-state classification driving the
  flagged-only filter includes an unanchored (coordinate-less) TS target.

## Impact

Code (all in `TargetSchedulerManager.App`; no library change, no schema change, no TS write-path change):

| File | Change |
|---|---|
| `Models/Badges.cs` | **new** — tokens, separator, severity, `Split`/`Join` |
| `Controls/BadgeRuns.cs` | **new** — attached property filling `TextBlock.Inlines` |
| `ThemeBrushes.cs` | one added lookup: `Secondary` → `TextFillColorSecondaryBrush` |
| `Services/ReconciliationLoader.cs` | badge literals via `Badges`; `isUnanchored` joins the `flagged` expression (`:175`) and the no-cells fallback (`:265`) |
| `ViewModels/Rows/RowAggregates.cs` | token-level distinct in the header rollup (`:45`) |
| `ViewModels/Rows/ReconciliationRow.cs` | `IsFlagged` doc comment |
| `MainWindow.xaml` | three row templates (`:80`, `:157`, `:203`): `Text=` + hard-coded `Foreground` → the attached property |

Behaviour change to be aware of: the flagged-only filter and header flag rollup now include unanchored
targets, so flagged counts can rise for a database carrying coordinate-less TS targets.

Tests: `BadgesTests` (new); two assertions added in `BuildRowsTests`; one header-dedupe assertion in
`RowAggregatesTests`.

Docs (same commit): `DOMAIN.md` badge bullet + "add a UI element" checklist steps 3-4, `CHANGELOG.md`.

Not verifiable by build: the colours themselves — in particular whether `TextFillColorSecondaryBrush` reads
as "quiet fact" rather than "disabled" in the author's theme.
