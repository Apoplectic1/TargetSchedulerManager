# disk-row-adoption — delta for assign-template-adoption

## REMOVED Requirements

### Requirement: The plan's template is auto-matched and held when unclear

**Reason**: Field-rejected (obs 3dfe, 2026-08-03): adoption assigns existing curated templates; TSM never
creates or edits one. The matcher's gatekeeper role (silent unique match, ambiguity/zero-match holds, the
pre-filled template creation form) is replaced by an always-shown assignment dialog where matching only
preselects a dropdown.

**Migration**: none — the adoption entry point is unchanged; the selection step becomes the assignment
dialog defined below. Historical cells whose configuration no template expresses are adopted by assigning
the nearest curated template and knowingly accepting split rendering, or by first creating a template in
TS (the authoring surface for templates).

## ADDED Requirements

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

## MODIFIED Requirements

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
