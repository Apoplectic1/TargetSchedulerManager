# disk-row-adoption — delta for adopt-target-rollup

> Note for sync/archive: the main spec's Purpose says "Always per-row, never a sweep" — amend to name the
> two grains (per-cell touch-up, per-target bulk) when this delta lands. Purpose edits don't travel in a
> delta; edit `openspec/specs/disk-row-adoption/spec.md` directly at sync time.

## MODIFIED Requirements

### Requirement: Eligible disk-only rows offer an adoption context-menu action
A disk-only filter row SHALL offer a right-click context-menu adoption action exactly when its target has
**no** TS exposure plan at the row's `(filter, purpose, whole-second exposure)` — today's unplanned-frames
condition. A disk row that separated from an existing same-`(filter, purpose, seconds)` plan by a
capture-config or framing disagreement SHALL offer no adoption item (creating one would mint a same-key
duplicate; the separation is the diagnostic). One invocation of the **per-cell** action SHALL adopt
exactly one cell; adopting several cells at once is the target-rollup action's job (defined below). Rows
under a fully disk-only mosaic parent (no TS project) SHALL offer no adoption item (mosaic adoption is out
of scope); a disk-only cell under a TS-backed mosaic panel is an ordinary eligible cell of that panel
target.

#### Scenario: Unplanned cell offers the action
- **WHEN** the user right-clicks a disk-only "OIII 600s" row under a target whose TS plans cover only Ha
- **THEN** the menu offers "Add TS plan…" for that cell

#### Scenario: Split row offers nothing
- **WHEN** the user right-clicks a disk row that split from an existing same-`(filter, purpose, seconds)` plan by a gain or framing disagreement
- **THEN** no adoption item appears (the row stays menu-governed by existing rules)

#### Scenario: One click, one cell
- **WHEN** a target has three unplanned disk cells and the user adopts one via the per-cell action
- **THEN** exactly one plan is created; the other two cells still offer their own adoption actions

## ADDED Requirements

### Requirement: A target rollup with eligible cells offers a bulk adoption action
A **target rollup row** SHALL offer a right-click adoption action exactly when at least one of its child
cells is individually eligible under the per-cell gate: labelled "Add to TS…" when the target has no TS
target (the action will create it), "Add TS plans…" when it does (bulk-adopting the remaining unplanned
cells). One invocation SHALL present **every** individually-eligible child cell in one combined dialog —
ineligible cells (planned, split, zero-second) never appear. A mosaic parent SHALL offer no bulk adoption
action (consistent with the per-cell mosaic exclusion); its TS-backed panels keep their per-cell actions
only. A rollup with zero eligible cells SHALL offer no adoption item.

#### Scenario: Disk-only target offers the creating form
- **WHEN** the user right-clicks the rollup of a target with no TS target and four disk-only cells
- **THEN** the menu offers "Add to TS…" and invoking it presents all four cells in one dialog

#### Scenario: Partially planned target offers the plans form
- **WHEN** the user right-clicks the rollup of a TS-backed target whose disk history holds two unplanned cells
- **THEN** the menu offers "Add TS plans…" presenting exactly those two cells

#### Scenario: Fully planned rollup offers nothing
- **WHEN** every child cell of a rollup is planned in TS or ineligible (split rows)
- **THEN** no bulk adoption item appears

#### Scenario: Mosaic parent offers nothing
- **WHEN** the user right-clicks a mosaic parent whose panels hold disk-only cells
- **THEN** no bulk adoption item appears; eligible panel cells keep their per-cell actions

### Requirement: Bulk adoption presents one combined assignment dialog
The bulk action SHALL open a single combined dialog: the owning TS **project** chosen once at the top —
locked to the owner when the TS target exists, a picker over existing projects with the target-creation
facts (prefilled name, disk centroid coordinates) when it does not, exactly the per-cell rules — followed
by one row per eligible cell showing the cell's facts (filter, purpose, seconds, disk count) plus its own
**template dropdown** governed by the existing per-cell machinery: strict same-filter/same-bin scope
within the chosen project's profile, best-match preselect, and the inline non-pairing caution that never
blocks. Each servable cell row SHALL carry an **include checkbox**, checked by default; an unchecked cell
is excluded from the write and keeps its per-cell action. A cell whose template scope is empty in the
chosen project's profile (no template of its filter/bin, or non-square binning) SHALL render disabled with
the reason shown and SHALL be excluded from the write. Switching the project SHALL re-scope every cell's
dropdown to the new profile, re-deriving preselects, cautions, and servability. Accept SHALL be enabled
exactly when at least one included, servable cell remains; Cancel SHALL write nothing.

#### Scenario: Combined dialog composes the per-cell machinery
- **WHEN** the bulk dialog opens over Ha/OIII/SII cells whose scopes each hold a pairing template
- **THEN** each row shows its own dropdown with the pairing template preselected, and one Accept covers all three

#### Scenario: Unservable cell greys, the rest proceed
- **WHEN** one cell's filter has no template at its binning in the chosen profile
- **THEN** that row renders disabled with the reason, Accept stays enabled, and accepting writes only the servable included cells

#### Scenario: Unchecking excludes a cell
- **WHEN** the user unchecks a cautioned cell and accepts
- **THEN** no plan is created for that cell; its grid row still offers the per-cell adoption action afterwards

#### Scenario: Project switch re-scopes
- **WHEN** the user switches the project picker on a target-creating bulk adoption
- **THEN** every cell's dropdown re-scopes to the new project's profile, including cells turning servable or unservable

#### Scenario: Nothing to write disables Accept
- **WHEN** every cell is unservable or unchecked
- **THEN** Accept is disabled; nothing can be written

### Requirement: Bulk accept writes one atomic batch
Accept SHALL write the included, servable cells as **one atomic local batch** through the guarded edit
funnel: the target payload first when the TS target does not exist (minted guid, chosen project, disk
centroid with RA in hours — the per-cell target-creation rules), then one born-complete plan per accepted
cell, each following the existing per-cell plan rules (born-complete counts, `-1` exposure sentinel on
template-default agreement, enabled). The batch SHALL journal as one group, mark its rows outbound, and
re-reconcile the grid once — each accepted cell rendering `Both` when its assigned template pairs or a TS
row beside the disk row when accepted under the caution. When the target is created, its rotation SHALL be
seeded from a sky rotation expressed by an included cell's framing cluster per the per-cell seeding rule;
mechanical or unknown angles never seed. A structural backstop refusal while building any cell's payload
SHALL abort the entire batch, naming the offending cell — no partial adoption is ever written. Busy
exclusion and discard-undo apply exactly as for per-cell adoption.

#### Scenario: Target plus all plans in one confirmed step
- **WHEN** the user accepts a target-creating bulk adoption of three cells
- **THEN** one TS target and three born-complete plans exist locally as one journaled group, and the refreshed grid shows each cell's pairing outcome

#### Scenario: Backstop refusal aborts the whole batch
- **WHEN** a cell's payload build hits a structural refusal (e.g. the retained graph lost the disk target)
- **THEN** nothing is written — no target, no plans — and the refusal names the cell

#### Scenario: One journal group, one undo
- **WHEN** the user discards unpushed changes after a bulk adoption
- **THEN** the target and every plan from that batch vanish together with the refreshed local copy
