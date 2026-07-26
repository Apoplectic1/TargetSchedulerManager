## ADDED Requirements

### Requirement: Badge tokens render at one of two severities, per token

Each match-state badge token SHALL render at one of exactly two severities: **warning** — the state is
authoring the user must repair outside TSM (a duplicate, a name mismatch, an ambiguous match, multiple plans
on one filter/purpose, an accepted/acquired divergence, or a coordinate-less TS target) — or **informative** —
the state is a fact carrying no call to action (a mosaic, or a target with neither plans nor scanned frames).
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
