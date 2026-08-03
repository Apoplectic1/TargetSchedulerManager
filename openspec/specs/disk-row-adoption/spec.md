# disk-row-adoption Specification

## Purpose

User-initiated adoption of a disk-only reconciliation row into TS: a right-click action that creates the
missing TS exposure plan (and, for a fully disk-only target, the TS target via a project picker) so the
disk history becomes visible to TS's planner and the row reads `Both`. Always per-row, never a sweep.

## Requirements

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
same filter, same purpose (the `"Stars "` name-prefix convention), and gain/offset/bin **expressed and
equal** to the cell's. A `-1` use-camera-default sentinel SHALL NOT qualify — per the capture-config merge
rule, an unspecified value can never be asserted to agree, so a plan from such a template would land
beside the disk row instead of merging with it. When zero or two-or-more templates qualify, the adoption
SHALL be refused with a message naming the cell and the candidate situation — including any near-miss
templates of the **same filter, purpose, and binning** (differing or camera-default gain/offset) so the
fix is evident; a different-binning template is a different integration and SHALL never be suggested —
never guessed, and never resolved by silently creating or editing a template. A hold SHALL be presented to
the user in its own right — an explicit menu action that silently declines reads as "nothing happened" —
not only on a passive status line. A **zero-match** hold SHALL open the **template creation form**
(historical cells shot under configurations no current template expresses are the normal case): the full
schema-generated template form pre-filled with the cell's gain/offset/bin, a default exposure equal to the
cell's seconds (the plan then defers via the sentinel), a name derived from those values, and every other
policy field cloned from a same-profile donor template (donor preference: same filter/purpose/binning
family, then any same-binning template, then any) — plus the plan's **desired** count, prefilled with the
disk count and raisable (acquired/accepted stay the disk count). Every field SHALL be reviewable and
editable before anything exists — commits land in a draft, and light-dismiss/Cancel writes nothing (the
deliberate exception to per-field commit semantics: a creation is atomic). A draft whose gain/offset/bin
leave the cell's values SHALL warn that the plan would not pair — warn, never block. Create lands template
+ (target +) plan as one atomic local batch; an empty or profile-duplicate template name refuses. Ambiguity,
non-square-binning, and missing-centroid holds show a message-only dialog. The plan's exposure override
SHALL be the `-1` use-template-default sentinel when the effective template default equals the cell's
whole-second exposure, else the cell's whole seconds explicitly.

#### Scenario: Unique match is taken silently
- **WHEN** exactly one template matches the cell's filter, purpose, and expressed capture dimensions
- **THEN** the plan is created referencing it, with no template picker shown

#### Scenario: Gain disagreement disqualifies a template
- **WHEN** the only same-filter template expresses gain 139 and the disk cell's frames carry gain 100
- **THEN** the adoption is refused with a message naming the mismatch; no plan and no template are created

#### Scenario: Ambiguous templates hold
- **WHEN** two templates both match the cell
- **THEN** the adoption is refused naming both candidates; nothing is written

#### Scenario: A camera-default template never pairs
- **WHEN** the only same-filter/purpose template carries the `-1` use-camera-default gain/offset sentinel
- **THEN** the adoption is refused, the message naming the template and its camera-default values — a plan
  built on it could never merge with the disk row

#### Scenario: A zero-match hold opens the pre-filled creation form
- **WHEN** a Stars B cell at gain 53 finds no matching template while a 'Stars B' (gain 0, bin 1) template exists
- **THEN** the creation form opens pre-filled — name "Stars B g53 o10", gain 53, offset 10, bin 1, default
  60 s, policy fields from 'Stars B', desired = the disk count — every field editable; Create lands the
  template and the plan referencing it as one atomic local batch; Cancel writes nothing

#### Scenario: Different-binning templates are never suggested
- **WHEN** a bin-1 cell holds and the only same-filter/purpose template is a 2×2 variant
- **THEN** the near-miss listing omits it and the donor preference passes over it (any same-binning
  template outranks it)

#### Scenario: Desired can be raised at creation
- **WHEN** the user sets Desired to 60 in the form over a 42-frame cell
- **THEN** the created plan holds desired 60 with acquired = accepted = 42 (history recorded, more requested)

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
