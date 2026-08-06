# disk-row-adoption Specification

## Purpose

User-initiated adoption of disk-only reconciliation rows into TS: right-click actions that create the
missing TS exposure plans (and, for a fully disk-only target, the TS target) through an assignment dialog,
so the disk history becomes visible to TS's planner — rendering `Both` when the assigned template pairs
with the cell. Two explicit grains, never an unasked sweep: per-cell (one row, one plan) and per-target
(a rollup's eligible cells through one combined dialog, one atomic batch).

## Requirements

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

### Requirement: Adoption always presents one assignment dialog

The adoption action SHALL always open a single assignment dialog — never a silent write, never a
refusal-by-hold — presenting exactly two choices: the owning TS **project** and the plan's **exposure
template**, each a dropdown over existing rows only (nothing is ever created from this dialog), with
Accept and Cancel. The template dropdown SHALL be strictly scoped to templates of the cell's filter and
square binning (a different-binning template is a different integration and SHALL NOT be listed). The
best match SHALL be preselected: a template whose purpose and expressed gain/offset agree with the cell
outranks a same-purpose near-miss, which outranks the rest. When the strict scope is empty, the dialog
SHALL state that no template of that filter and binning exists — the remedy (creating one) belongs in TS —
and Accept SHALL be disabled. A non-square-binning cell has an empty scope by definition. The dialog SHALL
show the selected template's capture values (gain, offset, bin, default exposure) read-only beside the
cell's disk values; no plan field is editable here — adjustments happen in the plan editor afterward.
Accept SHALL write the adoption as one atomic local batch; Cancel SHALL write nothing.

#### Scenario: Matching template is preselected, still reviewed
- **WHEN** the user adopts a cell whose filter and bin scope contains a template whose purpose and expressed gain/offset agree with the cell
- **THEN** the dialog opens with that template preselected, and nothing is written until Accept

#### Scenario: Empty scope refuses honestly
- **WHEN** the cell's filter has no template at the cell's binning
- **THEN** the dialog states that and Accept is disabled; nothing is written

#### Scenario: Cancel is a no-op
- **WHEN** the user cancels or light-dismisses the dialog
- **THEN** no row is created and no journal entry exists

### Requirement: A non-pairing assignment cautions, never blocks

When the selected template would not merge with the disk cell under the reconciliation pairing rules —
its purpose or an expressed gain/offset disagrees with the cell, or it carries a use-camera-default
sentinel (an unspecified value can never be asserted to agree) — the dialog SHALL display an inline
caution stating that the created plan will appear as a separate TS row beside the disk row rather than
merging into `Both`. The caution SHALL update with the dropdown selection and SHALL never prevent Accept:
rendering the split is the informed choice being offered.

#### Scenario: Gain disagreement cautions
- **WHEN** the user selects a gain-0 template over a gain-53 disk cell
- **THEN** the caution states the plan will render beside the disk row, and Accept remains enabled

#### Scenario: Agreement clears the caution
- **WHEN** the user switches the dropdown to a template whose purpose and expressed values agree with the cell
- **THEN** the caution disappears

### Requirement: An adopted plan's counts are seeded by pairing
When the assigned template **pairs** with the disk cell under the reconciliation pairing rules, the created
plan SHALL record `desired` = `acquired` = `accepted` = the cell's disk file count (born complete): TS sees
the history as satisfied and schedules nothing until the user raises `desired`. When the assigned template
would **not** pair — the cautioned outcome — the created plan SHALL record `desired` = `acquired` =
`accepted` = 0: no disk files correspond to the plan being created, and disk is truth from the plan's first
moment, not something a later write-back pass has to correct. Either way the plan SHALL be enabled, and
`desired` remains entirely the user's number afterward.

#### Scenario: Pairing assignment is born complete
- **WHEN** a disk cell with 42 frames is adopted under a template that pairs with it
- **THEN** the plan holds desired 42, acquired 42, accepted 42, enabled

#### Scenario: Non-pairing assignment is born empty
- **WHEN** the user accepts a gain-0 template over an 18-frame gain-53 disk cell (the cautioned split)
- **THEN** the plan holds desired 0, acquired 0, accepted 0, enabled — the pushed row carries no counts the
  disk cannot back

#### Scenario: Raising desired later reopens the plan
- **WHEN** the user later edits the adopted plan's desired to 60
- **THEN** the edit flows through the normal plan-edit path (journal, marks, push) like any plan

### Requirement: Adopting under a disk-only target creates the target through a project picker
The assignment dialog's project dropdown SHALL be live exactly when the adopted row's target has no TS
target: a picker over the existing TS projects (no project is ever created), the target name prefilled
from the disk target, and the coordinates shown for confirmation — the disk RA/Dec centroid, RA converted
from degrees to TS's hours. On Accept, the target row SHALL be created (minted guid) in the chosen project
and the cell's plan created under it, as one atomic local operation. When the target already exists in TS
— including one created by an earlier adoption of another filter of the same target — the project SHALL be
shown locked to the owning project, not chosen: all of a target's plans live in one project. When the
adopted cell's framing cluster expresses a sky rotation, the new target's rotation SHALL be seeded from
it; a mechanical or unknown camera angle SHALL never be converted to sky and seeds nothing. Cancel SHALL
write nothing.

#### Scenario: Target plus plan in one confirmed step
- **WHEN** the user adopts a cell under a disk-only target, picks a project and template, and accepts
- **THEN** one TS target (disk centroid coords, RA in hours) and one born-complete plan exist locally, both journaled

#### Scenario: Second filter locks to the first adoption's project
- **WHEN** the user adopts a second filter row of a target whose first adoption created the TS target in project P
- **THEN** the dialog shows P locked; the new plan lands under the existing target with no second target created

#### Scenario: Sky framing seeds rotation
- **WHEN** the adopted cell's framing cluster carries a sky rotation
- **THEN** the created target's rotation is that angle, so the cell pairs immediately under the framing rules

### Requirement: Adoption is an edit in the sync model
The adoption SHALL write only the local TS db through the guarded edit funnel: it is refused while a bulk
operation runs (busy exclusion), it journals its inserts (dirty badge, push review, replay), its rows mark
outbound (`→`), and after the local write the grid SHALL re-reconcile without a pull — the adopted plan
appearing as one `Both` row when it pairs with the disk cell, or as a TS row beside the disk row when the
assigned template does not pair (the cautioned outcome). The plan's exposure override SHALL be the `-1`
use-template-default sentinel when the template's default exposure equals the cell's whole-second
exposure, else the cell's whole seconds explicitly. The subsequent write-back passes treat the adopted
plan as an ordinary existing plan.

#### Scenario: Pairing adoption turns Both immediately
- **WHEN** an adoption whose template pairs with the cell lands locally
- **THEN** the refreshed grid shows the cell as one `Both` row marked `→`, and the unpushed count includes the inserts

#### Scenario: Cautioned adoption renders the split it promised
- **WHEN** an adoption accepted under the non-pairing caution lands locally
- **THEN** the refreshed grid shows a new TS plan row marked `→` beside the still-separate disk row

#### Scenario: Busy exclusion refuses adoption
- **WHEN** the user invokes adoption while a pull is in flight
- **THEN** the adoption is refused like any row edit; nothing is written

#### Scenario: Undo is discard
- **WHEN** the user regrets an unpushed adoption and chooses discard-and-pull at the dirty prompt (or Pull-now discard flow)
- **THEN** the inserted rows vanish with the refreshed local copy and the journal entries clear

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
centroid with RA in hours — the per-cell target-creation rules), then one plan per accepted cell, each
following the existing per-cell plan rules (counts seeded by that cell's own pairing outcome — born
complete when its template pairs, zeros when accepted under the caution — `-1` exposure sentinel on
template-default agreement, enabled). The batch SHALL journal as one group, mark its rows outbound, and
re-reconcile the grid once — each accepted cell rendering `Both` when its assigned template pairs or a TS
row beside the disk row when accepted under the caution. When the target is created, its rotation SHALL be
seeded from a sky rotation expressed by an included cell's framing cluster per the per-cell seeding rule;
mechanical or unknown angles never seed. A structural backstop refusal while building any cell's payload
SHALL abort the entire batch, naming the offending cell — no partial adoption is ever written. Busy
exclusion and discard-undo apply exactly as for per-cell adoption.

#### Scenario: Target plus all plans in one confirmed step
- **WHEN** the user accepts a target-creating bulk adoption of three cells
- **THEN** one TS target and three plans exist locally as one journaled group, each seeded by its own
  pairing outcome, and the refreshed grid shows each cell's pairing outcome

#### Scenario: Mixed pairing outcomes seed per cell
- **WHEN** a bulk adoption accepts two pairing cells of 30 and 12 frames and one cautioned non-pairing cell
- **THEN** the pairing plans hold 30/30/30 and 12/12/12 while the cautioned plan holds 0/0/0

#### Scenario: Backstop refusal aborts the whole batch
- **WHEN** a cell's payload build hits a structural refusal (e.g. the retained graph lost the disk target)
- **THEN** nothing is written — no target, no plans — and the refusal names the cell

#### Scenario: One journal group, one undo
- **WHEN** the user discards unpushed changes after a bulk adoption
- **THEN** the target and every plan from that batch vanish together with the refreshed local copy
