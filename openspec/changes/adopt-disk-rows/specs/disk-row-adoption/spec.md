## Purpose

User-initiated adoption of a disk-only reconciliation row into TS: a right-click action that creates the
missing TS exposure plan (and, for a fully disk-only target, the TS target via a project picker) so the
disk history becomes visible to TS's planner and the row reads `Both`. Always per-row, never a sweep.

## ADDED Requirements

### Requirement: Eligible disk-only rows offer an adoption context-menu action
A disk-only filter row SHALL offer a right-click context-menu adoption action exactly when its target has
**no** TS exposure plan at the row's `(filter, purpose, whole-second exposure)` — today's unplanned-frames
condition. A disk row that separated from an existing same-`(filter, purpose, seconds)` plan by a
capture-config or framing disagreement SHALL offer no adoption item (creating one would mint a same-key
duplicate; the separation is the diagnostic). One invocation SHALL adopt exactly one cell. Rows under a
fully disk-only mosaic parent (no TS project) SHALL offer no adoption item (mosaic adoption is out of
scope); a disk-only cell under a TS-backed mosaic panel is an ordinary eligible cell of that panel target.

#### Scenario: Unplanned cell offers the action
- **WHEN** the user right-clicks a disk-only "OIII 600s" row under a target whose TS plans cover only Ha
- **THEN** the menu offers "Add TS plan…" for that cell

#### Scenario: Split row offers nothing
- **WHEN** the user right-clicks a disk row that split from an existing same-`(filter, purpose, seconds)` plan by a gain or framing disagreement
- **THEN** no adoption item appears (the row stays menu-governed by existing rules)

#### Scenario: One click, one cell
- **WHEN** a target has three unplanned disk cells and the user adopts one
- **THEN** exactly one plan is created; the other two cells still offer their own adoption actions

### Requirement: The plan's template is auto-matched and held when unclear
The adopted plan's exposure template SHALL be selected from the existing templates by the pairing rule:
same filter, same purpose (the `"Stars "` name-prefix convention), and agreement on every dimension the
template expresses (gain/offset/binning; a `-1` use-camera-default sentinel expresses nothing and is
compatible). When zero or two-or-more templates qualify, the adoption SHALL be refused with a message
naming the cell and the candidate situation — never guessed, and never resolved by creating or editing a
template. The plan's exposure override SHALL be the `-1` use-template-default sentinel when the matched
template's default equals the cell's whole-second exposure, else the cell's whole seconds explicitly.

#### Scenario: Unique match is taken silently
- **WHEN** exactly one template matches the cell's filter, purpose, and expressed capture dimensions
- **THEN** the plan is created referencing it, with no template picker shown

#### Scenario: Gain disagreement disqualifies a template
- **WHEN** the only same-filter template expresses gain 139 and the disk cell's frames carry gain 100
- **THEN** the adoption is refused with a message naming the mismatch; no plan and no template are created

#### Scenario: Ambiguous templates hold
- **WHEN** two templates both match the cell
- **THEN** the adoption is refused naming both candidates; nothing is written

#### Scenario: Exposure override only when needed
- **WHEN** the matched template's default exposure equals the cell's seconds
- **THEN** the created plan carries the use-template-default sentinel, not a redundant explicit override

### Requirement: An adopted plan is born complete
The created plan SHALL record `desired` = `acquired` = `accepted` = the cell's disk file count, and SHALL
be enabled. TS therefore sees the history as satisfied and schedules nothing until the user raises
`desired` through the normal editing surfaces.

#### Scenario: Born-complete counts
- **WHEN** a disk cell with 42 frames is adopted
- **THEN** the plan holds desired 42, acquired 42, accepted 42, enabled

#### Scenario: Raising desired later reopens the plan
- **WHEN** the user later edits the adopted plan's desired to 60
- **THEN** the edit flows through the normal plan-edit path (journal, marks, push) like any plan

### Requirement: Adopting under a disk-only target creates the target through a project picker
When the adopted row's target has no TS target, the action SHALL present a dialog before writing anything:
a picker over the existing TS projects (no project is ever created), the target name prefilled from the
disk target, and the coordinates shown for confirmation — the disk RA/Dec centroid, RA converted from
degrees to TS's hours. On confirm, the target row SHALL be created (minted guid) in the chosen project and
the cell's plan created under it, as one atomic local operation. When the adopted cell's framing cluster
expresses a sky rotation, the new target's rotation SHALL be seeded from it; a mechanical or unknown
camera angle SHALL never be converted to sky and seeds nothing. Cancel SHALL write nothing.

#### Scenario: Target plus plan in one confirmed step
- **WHEN** the user adopts a cell under a disk-only target, picks a project, and confirms
- **THEN** one TS target (disk centroid coords, RA in hours) and one born-complete plan exist locally, both journaled

#### Scenario: Cancel is a no-op
- **WHEN** the user dismisses the picker without confirming
- **THEN** no row is created and no journal entry exists

#### Scenario: Sky framing seeds rotation
- **WHEN** the adopted cell's framing cluster carries a sky rotation
- **THEN** the created target's rotation is that angle, so the cell pairs immediately under the framing rules

### Requirement: Adoption is an edit in the sync model
The adoption SHALL write only the local TS db through the guarded edit funnel: it is refused while a bulk
operation runs (busy exclusion), it journals its inserts (dirty badge, push review, replay), its rows mark
outbound (`→`), and after the local write the grid SHALL re-reconcile so the adopted cell reads `Both`
without a pull. The subsequent write-back passes treat the adopted plan as an ordinary existing plan.

#### Scenario: Adopted row turns Both immediately
- **WHEN** an adoption lands locally
- **THEN** the refreshed grid shows the cell as one `Both` row marked `→`, and the unpushed count includes the inserts

#### Scenario: Busy exclusion refuses adoption
- **WHEN** the user invokes adoption while a pull is in flight
- **THEN** the adoption is refused like any row edit; nothing is written

#### Scenario: Undo is discard
- **WHEN** the user regrets an unpushed adoption and chooses discard-and-pull at the dirty prompt (or Pull-now discard flow)
- **THEN** the inserted rows vanish with the refreshed local copy and the journal entries clear
