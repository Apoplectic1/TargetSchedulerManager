# grid-column-ruler — design

## Context

Four byte-identical 14-column `ColumnDefinitions` blocks: `GroupRowTemplate` (line ~31),
`FilterRowTemplate` (~105), `PanelRowTemplate` (~200), and the header Grid (~358). Column map (from the
header labels + template cells): 0 Mark·24 · 1 Enable·36 · 2 Source·110 · 3 Target·\* · 4 Project·170 ·
5 Filter·60 · 6 Purpose·70 · 7 Seconds·80 · 8 Desired·88 · 9 TS·60 · 10 Actual·60 · 11 Hours·60 ·
12 Plans·45 · 13 Badges·150.

## Goals / Non-Goals

**Goals:** one authoritative, *named* ruler; the four consumers stamped from it; zero visual change.

**Non-Goals:** single-sourcing cell `Grid.Column` indexes (impossible without abandoning DataTemplates —
explicitly out); column reordering/resizing features; touching any cell content.

## Decisions

- **D1 — attached property, not width resources.** `StaticResource` feeding `GridLength` is unreliable
  in the UWP/WinUI XAML lineage (no implicit double→GridLength conversion on resource assignment), and
  `ColumnDefinition` instances can't be shared. An attached property (`GridColumns.ApplyRuler`) is
  deterministic: the parser sets it on the Grid element, the callback stamps definitions immediately —
  before children are added, before layout — and per-instance cost is 14 struct-wrapped adds at
  container creation only (recycled containers keep their definitions; the property never refires).
- **D2 — the ruler is a named table, not a bare array.** `(string Name, GridLength Width)[]` with the
  column indexes in a doc comment — so the class is also the grid's column *documentation*, and a future
  "add a column" starts by editing a self-describing list.
- **D3 — home + namespace:** `GridColumns.cs` at the App project root, namespace
  `TargetSchedulerManager.App`, so the existing `xmlns:local` reaches it without a new prefix.
  (`DesiredBox_Loaded` set the precedent for small code-side view fix-ups.)

## Risks / Trade-offs

- [No test net — XAML rendering] → the ruler values are transcribed once from a verified byte-identical
  extraction; the XamlCompiler validates the attribute wiring; the user's visual pass gates archive.
- [A future template with the attribute forgotten] → fails loudly and obviously (all its cells collapse
  into column 0) rather than silently drifting — strictly better than the old failure mode (subtle
  misalignment from a missed width edit).

## Migration Plan

None. Clean rebuild.

## Open Questions

None.
