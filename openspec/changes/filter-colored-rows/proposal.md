# filter-colored-rows — Proposal

## Why

Expanded filter rows all look alike: scanning a tall expanded tree to follow one filter's story (all
the H rows, all the O rows) means reading the Filter letter row by row. The filter identity has a
natural, universally-understood visual channel in astro imaging — the passband/emission hue — and the
grid doesn't use it. Coloring each expanded row's background by its filter makes the filter structure
legible at a glance.

## What Changes

- Every `FilterRowTemplate` row (filter leaves, mixed rollups, nested detail lines) gets a **low-alpha
  full-row background wash** keyed by its filter code. Target group headers and mosaic panel
  mini-headers span filters and stay plain.
- Palette (user-supplied normalized RGB, the natural passband/emission hues):
  - Narrowband: `O` (0.00, 1.00, 0.71) · `H` (1.00, 0.00, 0.06) · `S` (0.78, 0.00, 0.00)
  - Broadband: `B` (0.00, 0.27, 1.00) · `G` (0.00, 1.00, 0.24) · `R` (1.00, 0.08, 0.00)
- **`L` is deliberately untouched** (plain row, no wash) — and any filter code outside the palette
  renders plain the same way. No warning, no fallback hue: plain is the designed answer.
- Wash alpha is **tuned for dark theme** (the daily driver); the exact alpha is settled by the user's
  visual sign-off, not by the build. Light theme must not be broken but is not the tuning target.
- Existing cell-scoped fills (caution/success/critical pills, `mixed` pills) render **on top of** the
  wash unchanged — the wash is a new identity layer beneath the existing state language, not a change
  to it. Accepted tension (explored, user-accepted): a `G` row's green wash coexists with
  green-means-goals-met; the alpha keeps the two visually distinct.
- Hover hit-testing and hover chrome survive: the row root's background stays non-null (today's
  `Transparent` becomes the wash brush), and the low alpha lets the ListViewItem hover visual read
  through.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `reconciliation-grid`: new presentation requirement — filter-keyed row background wash on filter-level
  rows (scope: which row kinds tint, palette + L/unknown plain rule, wash-under-state-fills layering).

## Impact

- `TargetSchedulerManager.App/MainWindow.xaml` — `FilterRowTemplate` root `Grid` background binds to a
  per-row brush (currently the literal `Transparent` that keeps empty cells hit-testable).
- New palette home beside the other presentation conventions (e.g. `Models/FilterBrushes.cs`): domain
  color constants + alpha, distinct from `ThemeBrushes` (system theme lookups). UI.md checklist item 4
  gets the documented sibling.
- `ReconciliationRow` — exposes the row's wash brush (recycle-safe via binding; no imperative cell
  builds).
- Docs: `UI.md` Visual language (the wash layer + palette + L-plain rule) in the same commit.
- No library (`Astronomy.Catalog`) changes; display-only, no keys, no search/flag behavior touched.
