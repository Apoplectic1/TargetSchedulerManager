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
