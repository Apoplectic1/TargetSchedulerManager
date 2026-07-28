# reconciliation-grid Specification

## Purpose

Presentation requirements for the reconciliation grid — the header + row-template rendering contract.
Seeded 2026-07-24 (`grid-column-ruler`) with the column-alignment invariant; future grid-presentation
requirements accrue here.

## Requirements

### Requirement: One column geometry across the header and every row kind
The reconciliation grid's column header and every row presentation (target group, mosaic panel, filter
row, and their nested detail lines) SHALL render on one shared column geometry — the same column count,
order, and widths — so cells align vertically across row kinds. The geometry SHALL have a single
authoritative definition; header and row templates SHALL consume it rather than restate it.

#### Scenario: Cells align across row kinds
- **WHEN** a target group row, a filter row, and a mosaic panel row render under the header
- **THEN** every column's cells align vertically with the header's captions

#### Scenario: A width change propagates everywhere
- **WHEN** one column's width is changed in the authoritative definition
- **THEN** the header and all row kinds render the new width with no other edit

### Requirement: Absent values render as the em dash; real zeros render as zeros
A cell whose value is absent (no plan side, no disk side, unknown) SHALL render as the em dash ("—") —
never blank and never a fabricated 0 — while a measured zero (e.g. zero frames on disk for a TS-only
row) SHALL render as 0: the dash means "nothing to say", the zero is a fact. Hours SHALL render with one
decimal, except small non-zero magnitudes (< 0.05 h) with two — so a short-frame total reads as small
rather than missing. These conventions SHALL have a single authoritative definition consumed by every
renderer.

#### Scenario: No plan side shows dashes, not zeros
- **WHEN** a disk-only row renders its Desired/TS cells
- **THEN** they show "—" (no goal exists), while its Actual cell shows the real frame count

#### Scenario: Small hours read as small, not missing
- **WHEN** a row's hours total is 0.03
- **THEN** it renders "0.03", not "0.0"

### Requirement: Badge tokens render at one of two severities, per token
Each match-state badge token SHALL render at one of exactly two severities: **warning** — the state is
authoring the user must repair outside TSM (a duplicate, a name mismatch, an ambiguous match, multiple plans
on one filter/purpose, an accepted/acquired divergence, a coordinate-less TS target, an unrecognised camera
directory, or a capture directory disagreeing with the camera recorded inside its frames) — or **informative** —
the state is a fact carrying no call to action (a mosaic, or a target with neither plans nor scanned frames).
Repair "outside TSM" SHALL be read to include repairs made on disk or in the image-management tooling, not
only in the Target Scheduler's own interface.
Warning tokens SHALL be visually emphasised; informative tokens SHALL be visually quiet. Severity SHALL be
resolved **per token**, so a row carrying both kinds shows each at its own severity rather than promoting the
whole cell to the higher one. The token vocabulary and its severity classification SHALL have a single
authoritative definition consumed by every renderer, and the badge **text** SHALL be unchanged by severity —
the searchable badge vocabulary is unaffected.

#### Scenario: A mixed row shows each token at its own severity
- **WHEN** a row's badges are `mosaic · multi-plan`
- **THEN** `mosaic` renders quiet and `multi-plan` renders emphasised, in one cell

#### Scenario: An informative-only row does not read as a warning
- **WHEN** a mosaic parent's only badge is `mosaic`
- **THEN** the cell renders entirely quiet, with no warning emphasis anywhere in it

#### Scenario: Badge text survives severity colouring
- **WHEN** a row carrying badges is matched against a search term naming one of its tokens
- **THEN** the row still matches — severity changes presentation only, never the badge string

#### Scenario: A camera-provenance token reads as a warning
- **WHEN** a row carries `camera` or `cam≠`
- **THEN** it renders emphasised and counts as flagged, on the same footing as a name mismatch

### Requirement: A header's badge rollup is a distinct union of tokens
A collapsible header row SHALL show the union of its descendant leaves' badge **tokens**, each appearing at
most once, in first-appearance order. Deduplication SHALL operate on individual tokens, not on whole joined
badge strings, so a token common to several leaves cannot repeat in the header.

#### Scenario: A token shared by leaves appears once in the header
- **WHEN** a mosaic target's leaves carry `mosaic` and `mosaic · multi-plan` respectively
- **THEN** the target group header shows `mosaic · multi-plan`, not `mosaic · mosaic · multi-plan`

### Requirement: An unanchored TS target counts as flagged
A TS target that could not be anchored for want of usable coordinates SHALL be classified as flagged —
included in the flagged-only filter and rolled up into its ancestors' flag state — on the same footing as a
duplicate, a name mismatch, an ambiguous match, a multi-plan filter, or an accepted/acquired divergence. Such
a target is unschedulable by the Target Scheduler and can never accrue disk credit, so it is repairable
authoring rather than a neutral fact. The classification SHALL hold whether the target carries exposure plans
or none at all.

#### Scenario: A coordinate-less TS target survives the flagged-only filter
- **WHEN** the flagged-only filter is active and a TS target has no usable coordinates
- **THEN** its rows remain visible, and its warning-severity badge is consistent with the filter that kept it

#### Scenario: An unanchored target with no plans is still flagged
- **WHEN** a coordinate-less TS target has neither exposure plans nor scanned frames, rendering as a single
  bare row
- **THEN** that row is flagged and its ancestors' flag state reflects it

#### Scenario: A target with no plans and no frames but valid coordinates stays unflagged
- **WHEN** a target has usable coordinates but neither exposure plans nor scanned frames
- **THEN** it renders informative and is **not** flagged — it is queued work, not broken authoring

### Requirement: The capture configuration is visible wherever it separates rows
The grid SHALL display camera, gain, offset and binning as their own columns, positioned together between Project and Filter. Because these values decide whether rows separate, a row that stands apart from its siblings SHALL always show why: the values responsible SHALL be legible on the row itself, never left to be inferred from a difference in counts.

#### Scenario: A separated row shows the value that separated it
- **WHEN** one filter's frames render as two rows differing only in gain
- **THEN** both rows display their gain, so the reason for the separation is readable without expanding anything further

#### Scenario: A TS row shows the template's configuration
- **WHEN** a TS row renders
- **THEN** its gain, offset and binning cells show the exposure template's values, and its camera cell shows the em dash

### Requirement: Row order keeps one filter's rows contiguous
Row ordering SHALL be target, project, panel, filter, purpose, exposure, then capture configuration, then plane. The capture-configuration columns SHALL be **excluded** from sort precedence despite sitting to the left of Filter, so that every row describing one filter stays together. This is a deliberate exception to the grid's convention that sort order follows column order, and SHALL be documented as such wherever that convention is stated.

#### Scenario: A filter's configurations stay adjacent
- **WHEN** a target has frames for two filters, each captured at two gains
- **THEN** the rows read as filter-major — both of the first filter's rows, then both of the second's — rather than grouping every row of one gain together across filters

#### Scenario: Configuration still breaks ties
- **WHEN** two rows agree on target, project, panel, filter, purpose and exposure
- **THEN** their capture configuration determines their relative order

### Requirement: A rollup row shows a uniform value, or that its children disagree
A collapsible row SHALL render each capture-configuration cell as the shared value when all of its descendants agree, and as a `mixed` marker at caution emphasis when they do not. A rollup SHALL NOT render such a cell blank merely because its children disagree: silence reads as "nothing to say" when the fact to convey is "these differ".

#### Scenario: A uniform value surfaces on the rollup
- **WHEN** every frame beneath a target header was captured on one camera at one binning
- **THEN** the header's camera and binning cells show those values

#### Scenario: Disagreement surfaces before expanding
- **WHEN** the rows beneath a header carry two different offsets
- **THEN** the header's offset cell reads `mixed` at caution emphasis, identifying the inconsistent dimension before the header is expanded

#### Scenario: A rollup distinguishes which dimension differs
- **WHEN** a header's descendants share a camera and binning but differ in gain and offset
- **THEN** the camera and binning cells show their values while only the gain and offset cells read `mixed`

### Requirement: A badge marks the rows it describes and their ancestors
A badge arising from a specific row's frames SHALL appear on that row and on every collapsible row above it, and SHALL NOT appear on sibling rows it does not describe. This SHALL hold alongside badges describing a whole target, which continue to appear on all of that target's rows.

#### Scenario: A per-row badge does not spread to siblings
- **WHEN** one of a target's several filter rows draws frames from an unrecognised camera directory
- **THEN** that row carries the `camera` badge, its ancestors show it in their rollup, and the target's other filter rows do not carry it

#### Scenario: A target-scope badge still marks every row
- **WHEN** a target is a duplicate or has a name mismatch
- **THEN** every one of its rows carries that badge, unchanged by this requirement
