## ADDED Requirements

### Requirement: Filter-keyed row background wash on filter-level rows
Every filter-level row (filter leaf, mixed rollup, and nested detail line) SHALL render a background
wash spanning the **Camera through Actual columns inclusive** (the capture-configuration + filter +
count band; the identity/text columns left of Camera and the Hours/Plans/Badges columns right of
Actual stay unwashed), keyed by its filter code, from the fixed palette:
`O` (0.00, 0.82, 1.00) cyan · `H` (1.00, 0.00, 0.06) red · `S` (1.00, 0.00, 0.50) crimson ·
`B` (0.00, 0.27, 1.00) blue · `G` (0.00, 1.00, 0.24) green · `R` (1.00, 0.47, 0.00) orange
(normalized RGB, rendered at a low alpha tuned for the dark theme; hues are contrast-separated from
the natural passband colors — at wash alpha luminance vanishes, so neighboring filters split by hue). Target group headers and mosaic
panel mini-headers span filters and SHALL stay plain. `L` and any filter code outside the palette
SHALL render plain — no wash, no fallback hue, no warning: plain is the designed answer.

The wash is an identity layer beneath the grid's existing state language: cell-scoped fills
(caution / success / critical, `mixed` pills) SHALL render on top of it unchanged, row hover
feedback SHALL remain visible through it, and the wash SHALL NOT participate in search, flagging,
sorting, or any reconciliation key. Final wash strength is settled by the author's visual sign-off.

#### Scenario: Palette filter tints
- **WHEN** a target expands and an H-filter plan row renders
- **THEN** the row's Camera→Actual column band carries the low-alpha H wash, and sibling O/S/R/G/B rows each carry their own palette wash

#### Scenario: L and unknown filters stay plain
- **WHEN** an L-filter row or a row whose filter code is outside the palette renders
- **THEN** the row background is plain, indistinguishable from the pre-wash rendering

#### Scenario: Headers stay plain
- **WHEN** a target group header or mosaic panel mini-header renders above expanded filter rows
- **THEN** it carries no filter wash regardless of the filters beneath it

#### Scenario: State fills render above the wash
- **WHEN** a washed row carries a caution Hours pill or a `mixed` Seconds pill
- **THEN** the pill renders on top of the wash with its meaning and legibility intact
