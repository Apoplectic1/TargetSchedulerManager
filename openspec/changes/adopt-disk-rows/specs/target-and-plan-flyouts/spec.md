## MODIFIED Requirements

### Requirement: TS-backed rows offer two edit triggers
Target group rows with a TS key and filter rows with a TS plan key SHALL offer both an edit glyph revealed
on pointer hover and a right-click context menu item ("Edit target…" / "Edit exposure plan…"). Disk-only
rows (no TS key) SHALL offer neither **edit** trigger — no hover glyph and no edit menu items — but an
adoption-eligible disk-only row SHALL offer the right-click adoption action defined by the
`disk-row-adoption` capability (composed through the same additive, data-gated menu). Existing gestures
(expansion toggle, in-grid `desired` cell, `active` checkbox) SHALL be unaffected.

#### Scenario: TS-backed target row
- **WHEN** the pointer hovers a target group row whose `TsTargetKey` is non-null
- **THEN** the edit glyph appears, and right-click shows "Edit target…"

#### Scenario: Disk-only row
- **WHEN** the pointer hovers or right-clicks a row with no TS key
- **THEN** no glyph appears and no **edit** menu item is offered; the menu contains the adoption action exactly when the row is adoption-eligible, and nothing otherwise

## ADDED Requirements

### Requirement: The plan flyout completes the capture spec with a write-through template section
The exposure-plan flyout SHALL append an editable section for the capture columns of the template behind
the plan (gain, offset, bin), headed by the template's identity **and blast radius** ("template '<name>' —
used by N plan(s)"). An edit there SHALL be an ordinary template edit — written through the guarded gate
to the `exposuretemplate` row, journaled as a template change (so direction marks light every plan row
sharing the template), with the template's per-field marks on the section's rows. The section renders only
the capture columns; the full template form remains the "Edit template…" flyout.

#### Scenario: Gain edited from the plan flyout re-keys all users visibly
- **WHEN** the user opens the "M 81 · Ha" plan flyout and changes the template section's gain
- **THEN** the write lands on the shared template, and every filter row using that template marks `→`

#### Scenario: The blast radius is visible at the point of edit
- **WHEN** the plan flyout opens for a plan whose template backs 79 plans
- **THEN** the section header reads "template '<name>' — used by 79 plan(s)"
