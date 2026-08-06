# filter-colored-rows — Design

## Context

See `proposal.md` — Why. Current state: `FilterRowTemplate`'s root `Grid` carries the literal
`Background="Transparent"` (load-bearing: empty cells must hit-test so the row hover handlers can
reveal the edit glyph). All grid color routes through `ThemeBrushes` (system theme-resource lookups
only); domain constants have no color home yet. The template is virtualized/recycled, so any per-row
visual must be binding-driven, never build-once.

## Goals / Non-Goals

**Goals:** filter identity legible at a glance across an expanded tree; zero disturbance to the
existing state language (fills, pills, badges, marks) and interactions (hover, selection, editing).

**Non-Goals:** light-theme tuning (must not be broken; not the target) · coloring the Filter letter or
headers · any semantic behavior (search/flag/sort/keys untouched) · configurability (fixed palette).

## Decisions

**D1 — Full-row wash, not letter foreground or accent stripe.** Explored all three (2026-08-05).
The user's ask is scanning-oriented and explicit: the entire row background. Letter-foreground
(identity-only signal) and accent stripe (unclaimed visual channel, dodges the fills collision)
were presented and declined. Accepted consequences, eyes open: a `G` row's green wash coexists with
green-means-goals-met, and `H`/`S` (both pure reds) converge at low alpha — the low alpha plus the
pills' stronger fills keep the state layer distinct.

**D2 — Low alpha, dark-theme tuned, author sign-off is the arbiter.** Full saturation kills text
contrast and masks the ListViewItem hover chrome the edit-glyph affordance rides on. The wash paints
*over* the item container's hover visual, so alpha low enough to read through is a hard constraint,
not taste. Exact value lands via the user's visual pass (rule: build proves code, the author's run
proves look-and-feel). Light theme reuses the same brushes unless the sign-off says otherwise.

**D3 — Palette home: `Models/FilterBrushes.cs`, not `ThemeBrushes`.** These are domain constants
(passband hues), not system theme lookups — `ThemeBrushes`' charter is resolving system resources.
A small static map (filter code → `SolidColorBrush` at wash alpha, `null` for L/unknown) beside
`Badges`/`Format`, per the one-plausible-home rule. UI.md checklist item 4 gains the sibling:
filter-identity color comes from `FilterBrushes`, state color from `ThemeBrushes`.

**D4 — A column-band `Border` underlay, not the template root.** (Revised 2026-08-05 after the first
render: the user scoped the wash to the **Camera→Actual columns inclusive** — the columns carrying
the filter's own story — leaving the identity text left of Camera and Hours/Plans/Badges right of
Actual unwashed.) `ReconciliationRow` exposes `FilterWash`, consumed by a first-child `Border`
spanning columns 5–15 (`IsHitTestVisible="False"`; first child = bottom of z-order, cells render on
top). The root `Grid` keeps its literal `Transparent` background, so the hit-test contract is
untouched rather than preserved-by-convention. L/unknown rows bind the shared transparent brush —
never `null`. Recycle-safe by construction (plain binding, no imperative cell build).

**D5 — Filter-code match is the row's own `Filter` display code.** The wash keys off the same
single-letter code the Filter column renders (the row model's existing notion), so wash and letter
can never disagree. No normalization layer, no alias table.

## Risks / Trade-offs

- `H` vs `S` near-indistinguishable at low alpha — accepted (D1); the letter still disambiguates.
- Selection/hover chrome interaction is theme- and alpha-dependent — that's exactly what the visual
  sign-off is for; if the wash fights the hover reveal, the alpha drops before anything structural.
- A future filter code outside the palette silently renders plain — by design (spec), not a gap.
